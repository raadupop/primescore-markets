using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrimeScore.Modules.Classification.Consensus;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Classification.Storage;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Classification.Pipeline;

internal sealed class ClassifyPendingSignalsHandler(ClassificationRunner runner) : ICommandHandler<ClassifyPendingSignals, ClassifyPendingAck>
{
    public Task<ClassifyPendingAck> HandleAsync(ClassifyPendingSignals command, CancellationToken cancellationToken) =>
        runner.RunAsync(cancellationToken);
}

/// <summary>
/// Newly recorded signals are classified before the ingestion request returns (SRS NFR-001). A
/// failure here never undoes or fails the ingestion that already happened: it is logged and the
/// scheduled run classifies whatever is still pending.
/// </summary>
internal sealed partial class SignalBatchAcceptedHandler(ClassificationRunner runner, ILogger<SignalBatchAcceptedHandler> logger)
    : IIntegrationEventHandler<SignalBatchAccepted>
{
    public async Task HandleAsync(SignalBatchAccepted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        try
        {
            await runner.RunAsync(integrationEvent.SignalIds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(integrationEvent.SignalIds.Count, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Classification of {Count} newly recorded signals failed; the scheduled run will retry: {Error}")]
    private partial void LogFailed(int count, string error);
}

/// <summary>
/// Classifies anything left pending at startup (signals recorded before classification ran)
/// and retries pending outcomes every <c>Classification:RetryMinutes</c> (default 10; 0 disables).
/// </summary>
internal sealed partial class ClassificationScheduler(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ClassificationScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = configuration.GetValue("Classification:RetryMinutes", 10.0);
        if (minutes <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var ack = await scope.ServiceProvider.GetRequiredService<ClassificationRunner>().RunAsync(stoppingToken).ConfigureAwait(false);
                    if (ack.Classified + ack.Fallbacks + ack.Unavailable > 0)
                    {
                        LogRun(ack.Classified, ack.Fallbacks, ack.Unavailable, ack.Remaining);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // A failed run (e.g. a full disk) is retried on the next tick; it never stops the engine.
                    LogFailed(exception.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Classification run: {Classified} classified, {Fallbacks} fallbacks, {Unavailable} unavailable, {Remaining} left pending")]
    private partial void LogRun(int classified, int fallbacks, int unavailable, int remaining);

    [LoggerMessage(Level = LogLevel.Error, Message = "Classification run failed; retrying on the next tick: {Error}")]
    private partial void LogFailed(string error);
}

/// <summary>
/// The latest outcome per signal, newest observation first. <see cref="GetAssessments.AsOf"/>
/// filters on the signal's observation time (SRS SIG-004).
/// </summary>
internal sealed class GetAssessmentsHandler(ClassificationReadStore reads) : IQueryHandler<GetAssessments, IReadOnlyList<AssessmentView>>
{
    public async Task<IReadOnlyList<AssessmentView>> HandleAsync(GetAssessments query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var context = reads.Open();
        var rows = context.Assessments.Where(row => row.Sequence == context.Assessments
            .Where(other => other.SignalId == row.SignalId)
            .Max(other => other.Sequence));
        if (query.AvailableOnly)
        {
            rows = rows.Where(row => row.Available);
        }

        if (query.SignalId is { } signalId)
        {
            var id = signalId.ToString("D");
            rows = rows.Where(row => row.SignalId == id);
        }

        if (query.AsOf is { } asOf)
        {
            var cut = asOf.ToUnixTimeMilliseconds();
            rows = rows.Where(row => row.ObservedAtMs <= cut);
        }

        if (!string.IsNullOrWhiteSpace(query.Instrument))
        {
            rows = rows.Where(row => row.Instrument == query.Instrument);
        }

        var ordered = rows.OrderByDescending(row => row.ObservedAtMs).ThenByDescending(row => row.Sequence);
        var page = await (query.Take is { } take ? ordered.Take(Math.Max(1, take)) : ordered)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return page.Select(AssessmentViews.From).ToArray();
    }
}

internal sealed class GetSignalOutcomesHandler(ClassificationReadStore reads) : IQueryHandler<GetSignalOutcomes, IReadOnlyDictionary<Guid, AssessmentView>>
{
    public async Task<IReadOnlyDictionary<Guid, AssessmentView>> HandleAsync(GetSignalOutcomes query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var ids = query.SignalIds.Select(id => id.ToString("D")).ToArray();
        await using var context = reads.Open();
        var rows = await context.Assessments.Where(row => ids.Contains(row.SignalId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(row => row.SignalId)
            .ToDictionary(group => Guid.Parse(group.Key), group => AssessmentViews.From(group.MaxBy(row => row.Sequence)!));
    }
}

internal sealed class GetConsensusStatusHandler(ConsensusBook consensus) : IQueryHandler<GetConsensusStatus, ConsensusStatus>
{
    public Task<ConsensusStatus> HandleAsync(GetConsensusStatus query, CancellationToken cancellationToken) =>
        Task.FromResult(new ConsensusStatus(
            consensus.Directory,
            consensus.Status().Select(file => new ConsensusFileView(file.Indicator, file.Rows, file.RejectedRows, file.Error)).ToArray()));
}

internal sealed class GetClassificationSummaryHandler(ClassificationReadStore reads, ClassificationGate gate) : IQueryHandler<GetClassificationSummary, ClassificationSummary>
{
    public async Task<ClassificationSummary> HandleAsync(GetClassificationSummary query, CancellationToken cancellationToken)
    {
        await using var context = reads.Open();
        var latest = context.Assessments.Where(row => row.Sequence == context.Assessments
            .Where(other => other.SignalId == row.SignalId)
            .Max(other => other.Sequence));
        var groups = await latest
            .GroupBy(row => new { row.Category, row.Available, row.IsFallback, row.Reason })
            .Select(group => new { group.Key.Category, group.Key.Available, group.Key.IsFallback, group.Key.Reason, Count = group.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var newest = await context.Assessments.MaxAsync(row => (long?)row.AssessedAtMs, cancellationToken).ConfigureAwait(false);
        var outcomes = groups
            .Select(group => new OutcomeCount(
                Enum.Parse<SourceCategory>(group.Category),
                group.Available ? (group.IsFallback ? "fallback" : "assessed") : group.Reason ?? "unavailable",
                group.Count))
            .GroupBy(count => (count.Category, count.Outcome))
            .Select(same => new OutcomeCount(same.Key.Category, same.Key.Outcome, same.Sum(count => count.Signals)))
            .OrderBy(count => count.Category).ThenBy(count => count.Outcome, StringComparer.Ordinal)
            .ToArray();
        return new ClassificationSummary(outcomes, newest is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null, gate.LastRun);
    }
}

internal static class AssessmentViews
{
    public static AssessmentView From(AssessmentRow row) => new(
        Guid.Parse(row.AssessmentId),
        Guid.Parse(row.SignalId),
        row.Sequence,
        new CorrelationId(Guid.Parse(row.CorrelationId)),
        Enum.Parse<SourceCategory>(row.Category),
        row.Instrument,
        row.Variant,
        DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs),
        DateTimeOffset.FromUnixTimeMilliseconds(row.AssessedAtMs),
        row.Available,
        row.Reason is null ? null : Enum.Parse<UnavailableReason>(row.Reason),
        row.Detail,
        row.Score,
        row.ScoreType,
        row.Certainty,
        row.HistorySufficiency,
        row.TemporalRelevance,
        row.ClassificationMethod,
        row.EventTaxonomy,
        row.IsFallback,
        row.StalenessSeconds,
        row.FallbackOf is null ? null : Guid.Parse(row.FallbackOf),
        CanonicalJson.Deserialize<string[]>(row.Flags),
        row.ReasoningTrace,
        row.ComputedMetrics,
        row.ReferenceWindowLength,
        row.Consensus is null ? null : CanonicalJson.Deserialize<ConsensusUsed>(row.Consensus));
}
