using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Decision.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Decision.Features;

/// <summary>Decisions by observation time (the time each decision refers to, SIG-004), newest first.</summary>
internal sealed class GetDecisionsHandler(DecisionReadStore reads) : IQueryHandler<GetDecisions, IReadOnlyList<DecisionView>>
{
    public async Task<IReadOnlyList<DecisionView>> HandleAsync(GetDecisions query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = reads.Open();
        var rows = db.Decisions.AsQueryable();
        if (query.From is { } from)
        {
            var cut = from.ToUnixTimeMilliseconds();
            rows = rows.Where(row => row.AsOfMs >= cut);
        }

        if (query.To is { } to)
        {
            var cut = to.ToUnixTimeMilliseconds();
            rows = rows.Where(row => row.AsOfMs <= cut);
        }

        if (!string.IsNullOrWhiteSpace(query.Context))
        {
            var context = query.Context.Trim().ToLowerInvariant();
            rows = rows.Where(row => row.Context == context);
        }

        if (query.Outcome is { } outcome)
        {
            var name = outcome.ToString();
            rows = rows.Where(row => row.Outcome == name);
        }

        if (query.CorrelationId is { } correlation)
        {
            var id = correlation.ToString();
            rows = rows.Where(row => row.CorrelationId == id);
        }

        var ordered = rows.OrderByDescending(row => row.AsOfMs).ThenByDescending(row => row.Sequence);
        var page = await (query.Take is { } take ? ordered.Take(Math.Max(1, take)) : ordered).ToListAsync(cancellationToken).ConfigureAwait(false);
        return page.Select(DecisionViews.From).ToArray();
    }
}

internal sealed class GetDecisionOutcomesHandler(DecisionReadStore reads) : IQueryHandler<GetDecisionOutcomes, IReadOnlyList<DecisionOutcomePoint>>
{
    public async Task<IReadOnlyList<DecisionOutcomePoint>> HandleAsync(GetDecisionOutcomes query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = (query.Context ?? "").Trim().ToLowerInvariant();
        var from = (query.From ?? DateTimeOffset.MinValue).ToUnixTimeMilliseconds();
        var to = (query.To ?? DateTimeOffset.MaxValue).ToUnixTimeMilliseconds();
        await using var db = reads.Open();
        var rows = await db.Decisions
            .Where(row => row.Context == context && row.AsOfMs >= from && row.AsOfMs <= to)
            .OrderBy(row => row.AsOfMs).ThenBy(row => row.Sequence)
            .Select(row => new { row.AsOfMs, row.Sequence, row.Outcome, row.CompositeScore, row.Scenario, row.ReferenceInstrument })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new DecisionOutcomePoint(DateTimeOffset.FromUnixTimeMilliseconds(row.AsOfMs), row.Sequence,
            Enum.Parse<DecisionOutcome>(row.Outcome), row.CompositeScore, row.Scenario, row.ReferenceInstrument)).ToArray();
    }
}

internal sealed class GetDecisionHandler(DecisionReadStore reads) : IQueryHandler<GetDecision, DecisionView?>
{
    public async Task<DecisionView?> HandleAsync(GetDecision query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var id = query.DecisionId.ToString("D");
        await using var db = reads.Open();
        var row = await db.Decisions.SingleOrDefaultAsync(decision => decision.DecisionId == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : DecisionViews.From(row);
    }
}

/// <summary>
/// The decision audit trail (SRS AUD-001) in ledger order. Every entry is a <c>DecisionMade</c>
/// ledger entry, so the hash chain makes a modified entry detectable (AUD-002). From and To
/// bound the recording time.
/// </summary>
internal sealed class GetAuditEntriesHandler(DecisionReadStore reads) : IQueryHandler<GetAuditEntries, IReadOnlyList<AuditEntryView>>
{
    public async Task<IReadOnlyList<AuditEntryView>> HandleAsync(GetAuditEntries query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = reads.Open();
        var rows = db.Decisions.AsQueryable();
        if (query.EntityId is { } entity)
        {
            var id = entity.ToString("D");
            rows = rows.Where(row => row.DecisionId == id);
        }

        if (query.EventType is { } type)
        {
            var outcome = (type == AuditEventType.DeployDecision ? DecisionOutcome.Deploy : DecisionOutcome.Idle).ToString();
            rows = rows.Where(row => row.Outcome == outcome);
        }

        if (query.From is { } from)
        {
            var cut = from.ToUnixTimeMilliseconds();
            rows = rows.Where(row => row.RecordedAtMs >= cut);
        }

        if (query.To is { } to)
        {
            var cut = to.ToUnixTimeMilliseconds();
            rows = rows.Where(row => row.RecordedAtMs <= cut);
        }

        var ordered = await rows.OrderBy(row => row.Sequence).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ordered.Select(DecisionViews.Audit).ToArray();
    }
}

internal static class DecisionViews
{
    public static DecisionView From(DecisionRow row)
    {
        var payload = CanonicalJson.Deserialize<DecisionMadePayload>(row.Payload);
        return new DecisionView(
            payload.DecisionId,
            row.Sequence,
            new CorrelationId(Guid.Parse(row.CorrelationId)),
            new ConfigVersion(row.ConfigVersion),
            payload.Context,
            payload.Outcome,
            payload.Scenario,
            payload.CompositeId,
            payload.CompositeScore,
            payload.DislocationId,
            payload.DislocationValue,
            payload.DislocationThreshold,
            payload.ReferenceInstrument,
            payload.MarketObservedIv,
            payload.SignalImpliedIv,
            payload.TriggerSignalId,
            payload.Conditions,
            payload.TopContributing,
            payload.Dissenting,
            payload.Explanation,
            payload.AsOf,
            DateTimeOffset.FromUnixTimeMilliseconds(row.RecordedAtMs),
            payload.LevelPercentile);
    }

    /// <summary>Input: what the decision read. Output: what it produced. Both straight from the ledger payload.</summary>
    public static AuditEntryView Audit(DecisionRow row)
    {
        var payload = CanonicalJson.Deserialize<DecisionMadePayload>(row.Payload);
        var input = new
        {
            context = payload.Context,
            as_of = payload.AsOf,
            config_version = row.ConfigVersion,
            composite_id = payload.CompositeId,
            composite_score = payload.CompositeScore,
            contributing_categories = payload.ContributingCategories,
            dislocation_id = payload.DislocationId,
            dislocation_value = payload.DislocationValue,
            dislocation_threshold = payload.DislocationThreshold,
            reference_instrument = payload.ReferenceInstrument,
            market_observed_iv = payload.MarketObservedIv,
            trigger_signal_id = payload.TriggerSignalId,
            deploy_conditions = payload.ConditionsConfigured,
        };
        var output = new
        {
            outcome = payload.Outcome == DecisionOutcome.Deploy ? "DEPLOY" : "IDLE",
            scenario = payload.Scenario,
            conditions_evaluated = payload.Conditions,
            top_contributing_signals = payload.TopContributing.Select(signal => signal.SignalId),
            dissenting_signals = payload.Dissenting.Select(signal => signal.SignalId),
            explanation = payload.Explanation,
        };
        return new AuditEntryView(
            payload.DecisionId,
            payload.Outcome == DecisionOutcome.Deploy ? AuditEventType.DeployDecision : AuditEventType.IdleDecision,
            payload.DecisionId,
            CanonicalJson.Serialize(input),
            CanonicalJson.Serialize(output),
            DateTimeOffset.FromUnixTimeMilliseconds(row.RecordedAtMs),
            row.Sequence);
    }
}
