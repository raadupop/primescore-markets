using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Classification.Storage;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Registry;
using AggregationKind = PrimeScore.Modules.Configuration.Contracts.Aggregation;

namespace PrimeScore.Modules.Classification.Aggregation;

/// <summary>
/// Records a composite and its dislocation for every new assessment of a context member,
/// at that signal's observation time and under its correlation id, oldest observation first
/// (ADR-0004 §7). Runs after each classification pass, a page at a time under the
/// classification gate, so ingestion, classification and aggregation of a batch finish before
/// the request returns (SRS NFR-001).
/// </summary>
internal sealed partial class CompositeRunner(
    ClassificationReadStore reads,
    IQueryHandler<GetActiveSettings, SettingsVersion> settings,
    IQueryHandler<GetObservationSeries, ObservationSeries> series,
    IndicatorRegistry registry,
    ILedger ledger,
    ILogger<CompositeRunner> logger)
{
    private const int PageSize = 500;
    private const string ImpliedVolatility = "IMPLIED_VOLATILITY";

    /// <summary>
    /// Records up to one page of pending composites per context, oldest observation first. The
    /// caller holds the classification gate for one page at a time, so a newly recorded batch
    /// never waits for a whole catch-up (for example after a backfill).
    /// </summary>
    /// <returns>Composites recorded, and whether any context may have more pending.</returns>
    public async Task<(int Recorded, bool More)> CatchUpPageAsync(CancellationToken cancellationToken)
    {
        var active = await settings.HandleAsync(new GetActiveSettings(), cancellationToken).ConfigureAwait(false);
        var recorded = 0;
        var more = false;
        foreach (var context in active.Settings.Contexts)
        {
            var pending = await PendingAsync(context, cancellationToken).ConfigureAwait(false);
            if (pending.Count == 0)
            {
                continue;
            }

            var parameters = Parameters(active.Settings, context);
            var reference = new ReferenceSeries(series, context.ReferenceInstrument, ReferenceLength(context.ReferenceInstrument));
            foreach (var trigger in pending)
            {
                await RecordAsync(active, context, parameters, reference, trigger, cancellationToken).ConfigureAwait(false);
                recorded++;
            }

            more |= pending.Count == PageSize;
        }

        return (recorded, more);
    }

    /// <summary>The newest available outcome of each member signal that has no composite in this context yet.</summary>
    private async Task<IReadOnlyList<AssessmentRow>> PendingAsync(ContextSettings context, CancellationToken cancellationToken)
    {
        // Membership is by (category, instrument) pair, matched in SQL so a page never repeats.
        var pairs = Pairs(context);
        await using var db = reads.Open();
        return await db.Assessments
            .Where(row => row.Available && pairs.Contains(row.Category + "|" + row.Instrument)
                && row.Sequence == db.Assessments.Where(other => other.SignalId == row.SignalId).Max(other => other.Sequence)
                && !db.Composites.Any(composite => composite.Context == context.Name && composite.TriggerAssessmentId == row.AssessmentId))
            .OrderBy(row => row.ObservedAtMs).ThenBy(row => row.Sequence)
            .Take(PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordAsync(
        SettingsVersion active,
        ContextSettings context,
        CompositeParameters parameters,
        ReferenceSeries reference,
        AssessmentRow trigger,
        CancellationToken cancellationToken)
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(trigger.ObservedAtMs);
        var (assessments, unavailable) = await InputsAsync(context, parameters, at, cancellationToken).ConfigureAwait(false);
        var composite = CompositeCalculator.Compute(at, parameters, assessments, unavailable);
        var compositeId = Guid.NewGuid();
        var correlation = new CorrelationId(Guid.Parse(trigger.CorrelationId));
        var entries = new List<LedgerAppend>
        {
            LedgerAppend.Create(LedgerKinds.CompositeComputed, compositeId, correlation, active.Version, CompositeSummary(context, composite, at),
                new CompositeComputedPayload(
                    compositeId, context.Name, Guid.Parse(trigger.AssessmentId), Guid.Parse(trigger.SignalId), at, composite.Score,
                    active.Settings.Weighting.SchemeId, active.Settings.Weighting.Aggregation.ToString(),
                    composite.Contributing.Select(View).ToArray(),
                    composite.Absent.Select(absent => new AbsentCategoryView(absent.Category, absent.Reason)).ToArray())),
        };

        if (await reference.AtAsync(at, cancellationToken).ConfigureAwait(false) is { } level)
        {
            var dislocation = DislocationCalculator.Compute(composite.Score, level.Point.Value, level.History, Dislocation(context));
            var dislocationId = Guid.NewGuid();
            entries.Add(LedgerAppend.Create(LedgerKinds.DislocationComputed, dislocationId, correlation, active.Version,
                DislocationSummary(context, dislocation),
                new DislocationComputedPayload(
                    dislocationId, context.Name, compositeId, at, composite.Score, context.ReferenceInstrument,
                    dislocation.MarketObservedIv, level.Point.ObservedAt, level.Point.SignalId, dislocation.Regime,
                    dislocation.RegimePercentile, dislocation.RegimeHistory, dislocation.SensitivityFactor,
                    dislocation.SignalImpliedIv, dislocation.DislocationValue, dislocation.Threshold, dislocation.ThresholdBreached)));
        }
        else
        {
            LogNoReference(context.Name, context.ReferenceInstrument, at);
        }

        await ledger.AppendAsync(entries, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The latest outcome of each member signal observed within the longest configured window
    /// ending at <paramref name="at"/>; the calculator applies each category's exact window.
    /// </summary>
    private async Task<(IReadOnlyList<AssessmentInput> Assessments, IReadOnlyList<OutcomeInput> Unavailable)> InputsAsync(
        ContextSettings context, CompositeParameters parameters, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var pairs = Pairs(context);
        var to = at.ToUnixTimeMilliseconds();
        var from = parameters.Expected.Min(category => CompositeCalculator.WindowStart(at, parameters.Windows[category])).ToUnixTimeMilliseconds();
        await using var db = reads.Open();
        var rows = await db.Assessments
            .Where(row => pairs.Contains(row.Category + "|" + row.Instrument) && row.ObservedAtMs >= from && row.ObservedAtMs <= to)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var latest = rows
            .GroupBy(row => row.SignalId, StringComparer.Ordinal)
            .Select(group => group.MaxBy(row => row.Sequence)!)
            .ToArray();
        var assessments = latest.Where(row => row.Available).Select(row => new AssessmentInput(
            Guid.Parse(row.AssessmentId), Guid.Parse(row.SignalId), Enum.Parse<SourceCategory>(row.Category), row.Instrument,
            DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs), row.Score ?? 0, row.Certainty ?? 0, row.IsFallback)).ToArray();
        var unavailable = latest.Where(row => !row.Available).Select(row => new OutcomeInput(
            Enum.Parse<SourceCategory>(row.Category), DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs),
            ReasonLabel(row.Reason))).ToArray();
        return (assessments, unavailable);
    }

    /// <summary><c>Category|Instrument</c> keys as stored in <c>cls_assessments</c> (category by enum name).</summary>
    private static string[] Pairs(ContextSettings context) =>
        context.Members
            .SelectMany(member => member.Value.Select(instrument =>
                (SourceCategoryNames.TryParseWireName(member.Key, out var category) ? category.ToString() : member.Key) + "|" + instrument))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal static CompositeParameters Parameters(EngineSettings settings, ContextSettings context)
    {
        static SourceCategory Parse(string name) => SourceCategoryNames.TryParseWireName(name, out var category)
            ? category
            : throw new InvalidOperationException($"Unknown category '{name}' in validated settings.");
        static Span ToSpan(WindowSpan span) => new(span.TradingDays, span.Seconds);

        return new CompositeParameters(
            settings.Weighting.SchemeId,
            settings.Weighting.CategoryWeights.ToDictionary(pair => Parse(pair.Key), pair => pair.Value),
            settings.Weighting.Aggregation == AggregationKind.MaxConfirmedWeighted,
            settings.BypassPercentile,
            settings.CorroborationWindows.ToDictionary(pair => Parse(pair.Key), pair => ToSpan(pair.Value)),
            settings.ReportingIntervals.ToDictionary(pair => Parse(pair.Key), pair => ToSpan(pair.Value)),
            settings.DropoutSchedule.Select(tier => (tier.BelowSeconds, tier.Factor)).ToArray(),
            // Enum order, not dictionary order: cached and reloaded settings then sum and list alike.
            context.Members.Keys.Select(Parse).Distinct().Order().ToArray());
    }

    internal static DislocationParameters Dislocation(ContextSettings context) => new(
        context.DislocationThreshold,
        context.Sensitivity.LowVol,
        context.Sensitivity.Normal,
        context.Sensitivity.HighVol,
        context.Regime.Mode == RegimeMode.Percentile,
        context.Regime.LowBelow,
        context.Regime.HighAbove,
        context.Regime.LowVolUpper,
        context.Regime.HighVolLower);

    /// <summary><c>N_L</c> (or <c>N</c>) of the reference instrument's indicator class; 1260 when unregistered.</summary>
    private int ReferenceLength(string instrument) =>
        registry.TryGetSymbol(instrument, out var symbol) ? symbol.IndicatorClass.ReferenceWindowLength : 1260;

    internal static CategoryContribution View(CategoryResult category) => new(
        category.Category, category.Weight, category.Discount, category.StalenessSeconds, category.NetConviction,
        category.WeightedContribution, category.MaxPositive, category.MaxNegative, category.Confirmed, category.Unconfirmed);

    internal static string ReasonLabel(string? reason) => reason switch
    {
        nameof(UnavailableReason.AwaitingConsensus) => "awaiting consensus",
        nameof(UnavailableReason.RouteNotImplemented) => "classifier route not in v1",
        nameof(UnavailableReason.ClassifierUnreachable) or nameof(UnavailableReason.InvalidResponse) => "classifier unavailable",
        nameof(UnavailableReason.ClassifierRejected) => "rejected by the classifier",
        nameof(UnavailableReason.OutsideClassifiedHistory) => "recorded before the classified history",
        nameof(UnavailableReason.DuplicateObservation) => "same-day observation already recorded by another source",
        _ => reason ?? "not classified",
    };

    private static string CompositeSummary(ContextSettings context, CompositeResult composite, DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{context.Name} composite {composite.Score:+0.0000;-0.0000;0} from {(composite.Contributing.Count == 0 ? "no category" : string.Join(", ", composite.Contributing.Select(category => category.Category.ToWireName())))} as of {at.UtcDateTime:yyyy-MM-dd HH:mm} UTC");

    private static string DislocationSummary(ContextSettings context, DislocationResult dislocation) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{context.Name} dislocation {dislocation.DislocationValue:+0.00;-0.00;0} on {context.ReferenceInstrument} {dislocation.MarketObservedIv:0.00} ({dislocation.Regime}, k={dislocation.SensitivityFactor:0.##}); threshold {dislocation.Threshold:0.##} {(dislocation.ThresholdBreached ? "breached" : "not breached")}");

    [LoggerMessage(Level = LogLevel.Information, Message = "No {Reference} level observed by {At} for context {Context}; composite recorded without a dislocation")]
    private partial void LogNoReference(string context, string reference, DateTimeOffset at);

    /// <summary>
    /// The reference instrument's implied-volatility closes, loaded once per catch-up; each
    /// lookup takes the newest close at or before a time and the <c>N_L</c> closes before it.
    /// </summary>
    private sealed class ReferenceSeries(IQueryHandler<GetObservationSeries, ObservationSeries> series, string instrument, int length)
    {
        private IReadOnlyList<ObservationPoint>? _points;

        public async Task<(ObservationPoint Point, IReadOnlyList<double> History)?> AtAsync(DateTimeOffset at, CancellationToken cancellationToken)
        {
            _points ??= (await series.HandleAsync(
                new GetObservationSeries(instrument, SourceCategory.MarketData, ImpliedVolatility, DateTimeOffset.MaxValue, 1_000_000),
                cancellationToken).ConfigureAwait(false)).Points;
            var index = LastAtOrBefore(_points, at);
            if (index < 0)
            {
                return null;
            }

            var start = Math.Max(0, index - length);
            return (_points[index], _points.Skip(start).Take(index - start).Select(point => point.Value).ToArray());
        }

        private static int LastAtOrBefore(IReadOnlyList<ObservationPoint> points, DateTimeOffset at)
        {
            int low = 0, high = points.Count - 1, found = -1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                if (points[middle].ObservedAt <= at)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return found;
        }
    }
}
