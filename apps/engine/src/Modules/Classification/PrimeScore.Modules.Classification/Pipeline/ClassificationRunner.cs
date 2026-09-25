using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Classification.Storage;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Classification.Pipeline;

/// <summary>One classification page at a time across API requests and the scheduler; also the last run's result.</summary>
internal sealed class ClassificationGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    public ClassificationRunView? LastRun { get; set; }

    public void Dispose() => Semaphore.Dispose();
}

/// <summary>
/// Classifies every signal without a final outcome, oldest observation first. One append per
/// signal carries its outcome under the signal's correlation id (SRS OBS-001).
/// <list type="bullet">
/// <item>Final: a real assessment, or a reason that retrying cannot change (route not in v1, request rejected, unknown indicator).</item>
/// <item>Retried on later runs: awaiting consensus, classifier unreachable, invalid answer, and fallbacks.</item>
/// <item>A classifier failure reuses the series' last real assessment, flagged with its staleness (SRS CLS-004);
/// without one the signal is recorded as unavailable, never silently skipped.</item>
/// <item>Three consecutive failures (no answer or an invalid one) stop calls for the rest of the run; the remaining
/// signals still get their CLS-004 outcome locally. An unchanged outcome is not recorded again.</item>
/// <item>The scheduled pass takes the gate one page at a time, so a newly recorded batch never waits for a whole pass.</item>
/// <item>Composites and dislocations follow, also a page at a time (ADR-0004 §7).</item>
/// </list>
/// </summary>
internal sealed partial class ClassificationRunner(
    IQueryHandler<GetSignalKeysInObservationOrder, IReadOnlyList<SignalKey>> signalKeys,
    IQueryHandler<GetSignalsById, SignalPage> signalsById,
    SignalClassifier classifier,
    Aggregation.CompositeRunner composites,
    ClassificationReadStore reads,
    ILedger ledger,
    ClassificationGate gate,
    IClock clock,
    ILogger<ClassificationRunner> logger)
{
    private const int PageSize = 500;
    private const int BreakerThreshold = 3;

    private readonly SemaphoreSlim _gate = gate.Semaphore;

    /// <summary>Every recorded signal without a final outcome.</summary>
    public Task<ClassifyPendingAck> RunAsync(CancellationToken cancellationToken) => RunAsync(null, cancellationToken);

    /// <param name="only">Just these signals (a freshly recorded batch); the scheduled run picks up everything else.</param>
    public async Task<ClassifyPendingAck> RunAsync(IReadOnlyList<Guid>? only, CancellationToken cancellationToken)
    {
        var tally = new Tally();
        if (only is null)
        {
            for (var skip = 0; ; skip += PageSize)
            {
                var keys = await signalKeys.HandleAsync(new GetSignalKeysInObservationOrder(skip, PageSize), cancellationToken).ConfigureAwait(false);
                if (keys.Count == 0)
                {
                    break;
                }

                await UnderGateAsync(() => PendingPageAsync(keys.Select(key => key.SignalId).ToArray(), tally, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            foreach (var chunk in only.Chunk(PageSize))
            {
                await UnderGateAsync(() => PendingPageAsync(chunk, tally, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
        }

        if (tally.Breaker)
        {
            LogBreaker(tally.NotSent);
        }

        // Every new assessment of a context member gets its composite and dislocation (ADR-0004 §7).
        var recorded = 0;
        for (var more = true; more;)
        {
            var page = (Recorded: 0, More: false);
            await UnderGateAsync(async () => page = await composites.CatchUpPageAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            recorded += page.Recorded;
            more = page.More;
        }

        gate.LastRun = new ClassificationRunView(clock.UtcNow, only is null, tally.Classified, tally.Fallbacks, tally.Unavailable, tally.NotSent, tally.Breaker, recorded);
        return new ClassifyPendingAck(tally.Classified, tally.Fallbacks, tally.Unavailable, tally.NotSent, recorded);
    }

    private async Task UnderGateAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads the page's latest outcomes first; only signals without a final one are fetched and processed.</summary>
    private async Task PendingPageAsync(IReadOnlyList<Guid> ids, Tally tally, CancellationToken cancellationToken)
    {
        var latest = await LatestOutcomesAsync(ids.Select(id => id.ToString("D")).ToArray(), cancellationToken).ConfigureAwait(false);
        var pending = ids.Where(id => !IsFinal(latest.GetValueOrDefault(id.ToString("D")))).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        var page = await signalsById.HandleAsync(new GetSignalsById(pending), cancellationToken).ConfigureAwait(false);
        foreach (var signal in page.Signals)
        {
            await ProcessAsync(signal, latest.GetValueOrDefault(signal.SignalId.ToString("D")), tally, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessAsync(SignalView signal, AssessmentRow? previous, Tally tally, CancellationToken cancellationToken)
    {
        ClassificationAttempt attempt;
        if (tally.Breaker)
        {
            tally.NotSent++;
            attempt = new ClassificationAttempt(
                Classifier.ClassifierOutcome.Unavailable(UnavailableReason.ClassifierUnreachable, null,
                    "Not sent: the classifier failed three times in a row in this run; retried on the next run."),
                0, null, null);
        }
        else
        {
            try
            {
                attempt = await classifier.ClassifyAsync(signal, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A defect with one signal must not stop the others; it stays pending and is retried.
                LogSignalFailed(signal.SignalId, exception.Message);
                tally.NotSent++;
                return;
            }

            var failed = attempt.Outcome.Reason is UnavailableReason.ClassifierUnreachable or UnavailableReason.InvalidResponse;
            tally.ConsecutiveFailures = failed ? tally.ConsecutiveFailures + 1 : 0;
            tally.Breaker = tally.ConsecutiveFailures >= BreakerThreshold;
        }

        var outcome = attempt.Outcome;
        LedgerAppend? entry;
        if (outcome.Answer is { } answer)
        {
            entry = Recorded(signal, answer, attempt);
            tally.Classified++;
        }
        else if (outcome.Reason is UnavailableReason.ClassifierUnreachable or UnavailableReason.InvalidResponse)
        {
            if (previous is not null && (previous.IsFallback || previous.Reason == outcome.Reason.ToString()))
            {
                return;
            }

            var source = await LastRealAssessmentAsync(signal, cancellationToken).ConfigureAwait(false);
            entry = source is null ? Unavailable(signal, outcome) : Fallback(signal, source, outcome);
            if (source is null)
            {
                tally.Unavailable++;
            }
            else
            {
                tally.Fallbacks++;
            }
        }
        else
        {
            if (previous is { Available: false } && previous.Reason == outcome.Reason.ToString())
            {
                return;
            }

            entry = Unavailable(signal, outcome);
            tally.Unavailable++;
        }

        await ledger.AppendAsync([entry], cancellationToken).ConfigureAwait(false);
    }

    private static bool IsFinal(AssessmentRow? previous) =>
        previous is not null
        && ((previous.Available && !previous.IsFallback)
            || previous.Reason is nameof(UnavailableReason.RouteNotImplemented)
                or nameof(UnavailableReason.ClassifierRejected)
                or nameof(UnavailableReason.UnknownIndicator));

    private async Task<Dictionary<string, AssessmentRow>> LatestOutcomesAsync(string[] signalIds, CancellationToken cancellationToken)
    {
        await using var context = reads.Open();
        var rows = await context.Assessments
            .Where(row => signalIds.Contains(row.SignalId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows
            .GroupBy(row => row.SignalId)
            .ToDictionary(group => group.Key, group => group.MaxBy(row => row.Sequence)!, StringComparer.Ordinal);
    }

    /// <summary>
    /// The newest real assessment of the same series observed before the signal. A macro series
    /// is its indicator (its variant also names the reference period, which differs per print).
    /// </summary>
    private async Task<AssessmentRow?> LastRealAssessmentAsync(SignalView signal, CancellationToken cancellationToken)
    {
        var before = signal.ObservedAt.ToUnixTimeMilliseconds();
        var category = signal.Category.ToString();
        var series = SeriesKey(signal.Variant);
        var periodPrefix = series + "@";
        await using var context = reads.Open();
        return await context.Assessments
            .Where(row => row.Instrument == signal.Instrument && row.Category == category
                && (row.Variant == series || row.Variant.StartsWith(periodPrefix))
                && row.Available && !row.IsFallback && row.ObservedAtMs < before)
            .OrderByDescending(row => row.ObservedAtMs).ThenByDescending(row => row.Sequence)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string SeriesKey(string variant)
    {
        var at = variant.IndexOf('@', StringComparison.Ordinal);
        return at < 0 ? variant : variant[..at];
    }

    private static LedgerAppend Recorded(SignalView signal, Classifier.ClassifierAnswer answer, ClassificationAttempt attempt)
    {
        var id = Guid.NewGuid();
        var flags = answer.Flags.Count > 0 ? $" [{string.Join(", ", answer.Flags)}]" : "";
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"{signal.Instrument} severity {answer.Score:+0.0000;-0.0000;0}, certainty {answer.Certainty:0.0000} ({answer.ClassificationMethod}){flags}");
        return LedgerAppend.Create(LedgerKinds.AssessmentRecorded, id, signal.CorrelationId, ConfigVersion.None, summary,
            new AssessmentRecordedPayload(
                id, signal.SignalId, signal.Category, signal.Instrument, signal.Variant, signal.ObservedAt, answer.Score, answer.ScoreType,
                answer.Certainty, answer.HistorySufficiency, answer.TemporalRelevance, answer.ClassificationMethod, answer.EventTaxonomy,
                answer.ReasoningTrace, JsonSerializer.SerializeToElement(answer.ComputedMetrics), answer.Flags,
                IsFallback: false, StalenessSeconds: null, FallbackOf: null, FailureReason: null, FailureDetail: null,
                attempt.ReferenceWindowLength, attempt.ReferenceWindowLastUpdate, attempt.Consensus, attempt.MacroWindow));
    }

    private static LedgerAppend Fallback(SignalView signal, AssessmentRow source, Classifier.ClassifierOutcome outcome)
    {
        var id = Guid.NewGuid();
        var sourceObserved = DateTimeOffset.FromUnixTimeMilliseconds(source.ObservedAtMs);
        var staleness = (signal.ObservedAt - sourceObserved).TotalSeconds;
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"{signal.Instrument} fallback to the assessment of {sourceObserved:yyyy-MM-dd HH:mm} UTC after {outcome.Reason} (staleness {staleness:0} s)");
        using var metrics = JsonDocument.Parse(source.ComputedMetrics ?? "{}");
        return LedgerAppend.Create(LedgerKinds.AssessmentRecorded, id, signal.CorrelationId, ConfigVersion.None, summary,
            new AssessmentRecordedPayload(
                id, signal.SignalId, signal.Category, signal.Instrument, signal.Variant, signal.ObservedAt, source.Score ?? 0, source.ScoreType ?? "ANOMALY_DETECTION",
                source.Certainty ?? 0, source.HistorySufficiency, source.TemporalRelevance, source.ClassificationMethod ?? "RULE_BASED",
                source.EventTaxonomy,
                $"CLS-004 fallback: reused assessment {source.AssessmentId} because the classifier gave no valid answer ({outcome.Detail}). Original trace: {source.ReasoningTrace}",
                metrics.RootElement.Clone(), JsonSerializer.Deserialize<string[]>(source.Flags) ?? [],
                IsFallback: true, StalenessSeconds: staleness, FallbackOf: Guid.Parse(source.AssessmentId), outcome.Reason, outcome.Detail,
                source.ReferenceWindowLength, null, null, null));
    }

    private static LedgerAppend Unavailable(SignalView signal, Classifier.ClassifierOutcome outcome)
    {
        var id = Guid.NewGuid();
        var detail = outcome.Detail ?? outcome.Reason.ToString()!;
        return LedgerAppend.Create(LedgerKinds.AssessmentUnavailable, id, signal.CorrelationId, ConfigVersion.None,
            $"{signal.Instrument} not classified: {outcome.Reason}",
            new AssessmentUnavailablePayload(id, signal.SignalId, signal.Category, signal.Instrument, signal.Variant, signal.ObservedAt,
                outcome.Reason!.Value, outcome.HttpStatus, detail));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Classifier failed three times in a row; {NotSent} signals were not sent in this run and keep their CLS-004 outcome until the next run")]
    private partial void LogBreaker(int notSent);

    [LoggerMessage(Level = LogLevel.Error, Message = "Classification of signal {SignalId} failed and is left pending: {Error}")]
    private partial void LogSignalFailed(Guid signalId, string error);

    private sealed class Tally
    {
        public int Classified { get; set; }

        public int Fallbacks { get; set; }

        public int Unavailable { get; set; }

        public int NotSent { get; set; }

        public int ConsecutiveFailures { get; set; }

        public bool Breaker { get; set; }
    }
}
