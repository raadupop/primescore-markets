using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.Modules.Exits.Contracts;
using PrimeScore.Modules.Positions.Contracts;
using PrimeScore.Modules.Risk.Contracts;
using Dto = PrimeScore.Api.Contracts;

namespace PrimeScore.Engine.Host.Api.Controllers;

// v1 endpoints are implemented milestone by milestone; until then each states which one.

[Authorize(Policy = ApiPolicies.Read)]
public sealed class ClassificationController(
    IQueryHandler<GetAssessments, IReadOnlyList<AssessmentView>> assessments,
    IQueryHandler<GetComposite, CompositeView?> composites,
    IQueryHandler<GetDislocation, DislocationView?> dislocations,
    IQueryHandler<GetActiveSettings, SettingsVersion> settings) : Dto.ClassificationControllerBase
{
    /// <summary>
    /// The newest composite of the context observed at or before <c>as_of</c> (SRS CLS-002,
    /// SIG-004); 404 for an unknown context or when none has been computed yet.
    /// </summary>
    public override async Task<ActionResult<Dto.CompositeScore>> GetCompositeScore(
        DateTimeOffset? as_of,
        string? context = "equity",
        CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(context) ? "equity" : context;
        var view = await composites.HandleAsync(new GetComposite(name, as_of), cancellationToken).ConfigureAwait(false);
        return view is not null
            ? AggregateDtos.From(view)
            : await MissingAsync(name, "No composite has been computed for this context" + (as_of is null ? "" : " by as_of"), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The latest assessment of each signal; <c>as_of</c> filters on the signal's observation time
    /// (SRS SIG-004). Signals without an assessment (awaiting consensus, route not implemented,
    /// classifier unavailable without a fallback) are not listed.
    /// </summary>
    public override async Task<ActionResult<ICollection<Dto.SignalAssessment>>> GetAssessments(
        Guid? signal_id,
        DateTimeOffset? as_of,
        CancellationToken cancellationToken = default)
    {
        // The contract has no paging: every matching assessment is returned, filtered in the database.
        var views = await assessments.HandleAsync(new GetAssessments(signal_id, as_of, Take: null, AvailableOnly: true), cancellationToken).ConfigureAwait(false);
        return views.Select(AssessmentDtos.From).ToList();
    }

    /// <summary>
    /// The newest dislocation of the context observed at or before <c>as_of</c> (SRS CLS-006);
    /// 404 for an unknown context or when no reference level had been observed.
    /// </summary>
    public override async Task<ActionResult<Dto.IvDislocation>> GetDislocation(
        DateTimeOffset? as_of,
        string? context = "equity",
        CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(context) ? "equity" : context;
        var view = await dislocations.HandleAsync(new GetDislocation(name, as_of), cancellationToken).ConfigureAwait(false);
        return view is not null
            ? AggregateDtos.From(view)
            : await MissingAsync(name, "No dislocation has been computed for this context" + (as_of is null ? "" : " by as_of"), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ObjectResult> MissingAsync(string context, string message, CancellationToken cancellationToken)
    {
        var active = await settings.HandleAsync(new GetActiveSettings(), cancellationToken).ConfigureAwait(false);
        return active.Settings.Context(context) is null
            ? ApiResults.NotFound($"Unknown context '{context}'; configured: {string.Join(", ", active.Settings.Contexts.Select(known => known.Name))}.")
            : ApiResults.NotFound(message + ".");
    }
}

/// <summary>
/// Deploy and idle decisions of every context (SRS DEC-001 to DEC-003). <c>from</c> and <c>to</c>
/// bound the observation time each decision refers to, its <c>decided_at</c>.
/// </summary>
[Authorize(Policy = ApiPolicies.Read)]
public sealed class DecisionsController(
    IQueryHandler<GetDecisions, IReadOnlyList<DecisionView>> decisions,
    IQueryHandler<GetDecision, DecisionView?> decision) : Dto.DecisionsControllerBase
{
    public override async Task<ActionResult<ICollection<Dto.DecisionRecord>>> ListDecisions(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        // The contract has no paging: every matching decision is returned.
        var views = await decisions.HandleAsync(new GetDecisions(from, to, Take: null), cancellationToken).ConfigureAwait(false);
        return views.Select(DecisionDtos.From).ToList();
    }

    public override async Task<ActionResult<Dto.DecisionRecord>> GetDecision(Guid decision_id, CancellationToken cancellationToken = default)
    {
        var view = await decision.HandleAsync(new GetDecision(decision_id), cancellationToken).ConfigureAwait(false);
        return view is null ? ApiResults.NotFound($"No decision {decision_id}.") : DecisionDtos.From(view);
    }
}

/// <summary>
/// The decision audit trail (SRS AUD-001) in ledger order; <c>from</c> and <c>to</c> bound the
/// recording time. Approval and position events are Milestone B, so filtering on them returns
/// an empty list.
/// </summary>
[Authorize(Policy = ApiPolicies.Read)]
public sealed class AuditController(IQueryHandler<GetAuditEntries, IReadOnlyList<AuditEntryView>> audit) : Dto.AuditControllerBase
{
    public override async Task<ActionResult<ICollection<Dto.AuditEntry>>> ListAuditEntries(
        Guid? entity_id,
        Dto.AuditEventType? event_type,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        Modules.Decision.Contracts.AuditEventType? type = null;
        if (event_type is { } requested)
        {
            if (DecisionDtos.ToModule(requested) is not { } recorded)
            {
                return new List<Dto.AuditEntry>();
            }

            type = recorded;
        }

        var entries = await audit.HandleAsync(new GetAuditEntries(entity_id, type, from, to), cancellationToken).ConfigureAwait(false);
        return entries.Select(DecisionDtos.From).ToList();
    }

    public override Task<IActionResult> AuditPut(CancellationToken cancellationToken = default) => AppendOnly();

    public override Task<IActionResult> AuditPatch(CancellationToken cancellationToken = default) => AppendOnly();

    public override Task<IActionResult> AuditDelete(CancellationToken cancellationToken = default) => AppendOnly();

    /// <summary>The audit trail is append-only (SRS AUD-001).</summary>
    private Task<IActionResult> AppendOnly()
    {
        Response.Headers.Allow = "GET";
        return Task.FromResult<IActionResult>(StatusCode(StatusCodes.Status405MethodNotAllowed));
    }
}

/// <summary>
/// Each accepted change records a new configuration version with who, when and the diff
/// (SRS NFR-003); it applies from the next composite. A change the formulas cannot use is a
/// 400 listing every problem, and nothing is recorded.
/// </summary>
[Authorize(Policy = ApiPolicies.Admin)]
public sealed class ConfigurationController(
    ICommandHandler<SetWeightingScheme, SettingsChangeAck> setScheme,
    ICommandHandler<SetDislocationSettings, SettingsChangeAck> setDislocation,
    ICommandHandler<SetDeployConditions, SettingsChangeAck> setConditions) : Dto.ConfigurationControllerBase
{
    public override async Task<IActionResult> SetWeightingScheme(Dto.WeightingScheme body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (AggregateDtos.ToSettings(body) is not { } scheme)
        {
            return ApiResults.BadRequest("The weighting scheme was not changed.", ["aggregation: must be WEIGHTED_MEAN or MAX_CONFIRMED_WEIGHTED"]);
        }

        var ack = await setScheme.HandleAsync(
            new SetWeightingScheme(scheme, body.Source_dropout_penalty, User.Identity?.Name ?? "unknown"),
            cancellationToken).ConfigureAwait(false);
        return Result(ack, "The weighting scheme was not changed.");
    }

    /// <summary>
    /// Each listed condition replaces the one of the same name (composite_score, contributing_sources,
    /// top_signal_certainty, newest_observation_age_trading_days); unlisted ones keep their values.
    /// </summary>
    public override async Task<IActionResult> SetDeployConditions(Dto.DeployConditionsConfig body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var conditions = body.Conditions ?? [];
        if (conditions.Any(condition => condition is null))
        {
            return ApiResults.BadRequest("The deploy conditions were not changed.", ["conditions: an element is null"]);
        }

        var ack = await setConditions.HandleAsync(
            new SetDeployConditions(conditions.Select(DecisionDtos.ToSettings).ToArray(), User.Identity?.Name ?? "unknown"),
            cancellationToken).ConfigureAwait(false);
        return Result(ack, "The deploy conditions were not changed.");
    }

    public override async Task<IActionResult> SetDislocationThreshold(Dto.Body body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var ack = await setDislocation.HandleAsync(
            new SetDislocationSettings(body.Threshold, body.Reference_instrument, body.Sensitivity_factor_map, body.Regime_boundaries, User.Identity?.Name ?? "unknown"),
            cancellationToken).ConfigureAwait(false);
        return Result(ack, "The dislocation settings were not changed.");
    }

    public override Task<IActionResult> SetRiskLimits(Dto.RiskLimitsConfig body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotImplemented(RiskCapability.NotImplementedReason));

    public override Task<IActionResult> SetHoldingPeriod(Dto.Body2 body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotImplemented(ExitsCapability.NotImplementedReason));

    public override Task<IActionResult> SetExecutionMode(Dto.Body3 body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotImplemented(PositionsCapability.NotImplementedReason));

    public override Task<IActionResult> SetApprovalThreshold(Dto.Body4 body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotImplemented(ApprovalCapability.NotImplementedReason));

    private static IActionResult Result(SettingsChangeAck ack, string refused) =>
        ack.Accepted ? new OkResult() : ApiResults.BadRequest(refused, ack.Errors);
}

[Authorize(Policy = ApiPolicies.Admin)]
public sealed class ReplayController : Dto.ReplayControllerBase
{
    public override Task<ActionResult<Dto.ReplayResponse>> ReplayEvent(Dto.ReplayRequest body, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<Dto.ReplayResponse>>(ApiResults.NotYetBuilt("M5", "ANA-001"));
}
