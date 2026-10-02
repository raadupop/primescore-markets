using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Decision.Evaluation;
using PrimeScore.Modules.Decision.Storage;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Decision.Pipeline;

/// <summary>One decision page at a time across the classification pass and direct commands.</summary>
internal sealed class DecisionGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    public void Dispose() => Semaphore.Dispose();
}

/// <summary>
/// Records one <c>DecisionMade</c> per dislocation, in recording order, under the dislocation's
/// correlation id (SRS OBS-001) and the configuration version in force (brief §6). The cursor is
/// the newest dislocation already decided on, read from the decision table, so a restart or a
/// concurrent run never decides twice (a unique index backs this up).
/// </summary>
internal sealed partial class DecisionRunner(
    IQueryHandler<GetAggregatesAfter, IReadOnlyList<AggregateRecord>> aggregates,
    IQueryHandler<GetActiveSettings, SettingsVersion> settings,
    DecisionReadStore reads,
    ILedger ledger,
    DecisionGate gate,
    ILogger<DecisionRunner> logger)
{
    private const int PageSize = 500;

    public async Task<MakePendingDecisionsAck> CatchUpAsync(CancellationToken cancellationToken)
    {
        int deploy = 0, idle = 0;
        for (var more = true; more;)
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var page = await aggregates.HandleAsync(new GetAggregatesAfter(await CursorAsync(cancellationToken).ConfigureAwait(false), PageSize), cancellationToken).ConfigureAwait(false);
                if (page.Count == 0)
                {
                    break;
                }

                var active = await settings.HandleAsync(new GetActiveSettings(), cancellationToken).ConfigureAwait(false);
                var conditions = active.Settings.DeployConditions
                    ?? throw new InvalidOperationException("The active configuration has no deploy conditions; the configuration schema adds them on start.");
                var entries = new List<LedgerAppend>(page.Count);
                foreach (var aggregate in page)
                {
                    var result = DecisionEvaluator.Evaluate(aggregate, conditions);
                    entries.Add(Entry(aggregate, result, conditions, active.Version));
                    if (result.Outcome == DecisionOutcome.Deploy)
                    {
                        deploy++;
                    }
                    else
                    {
                        idle++;
                    }
                }

                await ledger.AppendAsync(entries, cancellationToken).ConfigureAwait(false);
                more = page.Count == PageSize;
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }

        if (deploy + idle > 0)
        {
            LogRecorded(deploy, idle);
        }

        return new MakePendingDecisionsAck(deploy, idle);
    }

    private async Task<long> CursorAsync(CancellationToken cancellationToken)
    {
        await using var db = reads.Open();
        return await db.Decisions.MaxAsync(row => (long?)row.DislocationSequence, cancellationToken).ConfigureAwait(false) ?? 0;
    }

    private static LedgerAppend Entry(AggregateRecord aggregate, DecisionResult result, IReadOnlyList<DeployCondition> conditions, SharedKernel.ConfigVersion version)
    {
        var id = Guid.NewGuid();
        var composite = aggregate.Composite;
        var dislocation = aggregate.Dislocation;
        var level = dislocation.RegimePercentile is { } percentile
            ? string.Create(CultureInfo.InvariantCulture, $"{dislocation.ReferenceInstrument} {dislocation.MarketObservedIv:0.00} at percentile {percentile:0.000}")
            : string.Create(CultureInfo.InvariantCulture, $"{dislocation.ReferenceInstrument} {dislocation.MarketObservedIv:0.00} without level history");
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"{dislocation.Context} {(result.Outcome == DecisionOutcome.Deploy ? "DEPLOY" : "IDLE")} as of {dislocation.AsOf.UtcDateTime:yyyy-MM-dd HH:mm} UTC: composite {composite.Score:+0.0000;-0.0000;0}, {level}, state {result.Scenario}; {result.Conditions.Count(condition => condition.Passed)} of {result.Conditions.Count} conditions held");
        return LedgerAppend.Create(LedgerKinds.DecisionMade, id, dislocation.CorrelationId, version, summary,
            new DecisionMadePayload(
                id, dislocation.Context, result.Outcome, result.Scenario, composite.CompositeId, composite.Score,
                composite.Contributing.Select(category => SharedKernel.SourceCategoryNames.ToWireName(category.Category)).ToArray(),
                dislocation.DislocationId, dislocation.LedgerSequence, dislocation.DislocationValue, dislocation.Threshold,
                dislocation.ReferenceInstrument, dislocation.MarketObservedIv, dislocation.SignalImpliedIv, composite.TriggerSignalId,
                dislocation.AsOf, conditions, result.Conditions, result.TopContributing, result.Dissenting, result.Explanation,
                dislocation.RegimePercentile));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Decisions recorded: {Deploy} DEPLOY, {Idle} IDLE")]
    private partial void LogRecorded(int deploy, int idle);
}

internal sealed class MakePendingDecisionsHandler(DecisionRunner runner) : ICommandHandler<MakePendingDecisions, MakePendingDecisionsAck>
{
    public Task<MakePendingDecisionsAck> HandleAsync(MakePendingDecisions command, CancellationToken cancellationToken) =>
        runner.CatchUpAsync(cancellationToken);
}

/// <summary>
/// The decision stage after each classification pass (brief §6). A failure is logged and never
/// fails the classification or ingestion that already happened; the next pass retries.
/// </summary>
internal sealed partial class AggregatesRecordedHandler(DecisionRunner runner, ILogger<AggregatesRecordedHandler> logger)
    : IIntegrationEventHandler<AggregatesRecorded>
{
    public async Task HandleAsync(AggregatesRecorded integrationEvent, CancellationToken cancellationToken)
    {
        try
        {
            await runner.CatchUpAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording decisions failed; the next classification pass retries: {Error}")]
    private partial void LogFailed(string error);
}
