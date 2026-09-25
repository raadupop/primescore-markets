using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Exits.Contracts;
using PrimeScore.Modules.Positions.Contracts;
using PrimeScore.Modules.Risk.Contracts;
using Dto = PrimeScore.Api.Contracts;

namespace PrimeScore.Engine.Host.Api.Controllers;

// v1 endpoints are implemented milestone by milestone; until then each states which one.

[Authorize(Policy = ApiPolicies.Read)]
public sealed class ClassificationController : Dto.ClassificationControllerBase
{
    public override Task<ActionResult<Dto.CompositeScore>> GetCompositeScore(
        DateTimeOffset? as_of,
        string? context = "equity",
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<Dto.CompositeScore>>(ApiResults.NotYetBuilt("M3", "CLS-002"));

    public override Task<ActionResult<ICollection<Dto.SignalAssessment>>> GetAssessments(
        Guid? signal_id,
        DateTimeOffset? as_of,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<ICollection<Dto.SignalAssessment>>>(ApiResults.NotYetBuilt("M2", "CLS-001"));

    public override Task<ActionResult<Dto.IvDislocation>> GetDislocation(
        DateTimeOffset? as_of,
        string? context = "equity",
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<Dto.IvDislocation>>(ApiResults.NotYetBuilt("M3", "CLS-006"));
}

[Authorize(Policy = ApiPolicies.Read)]
public sealed class DecisionsController : Dto.DecisionsControllerBase
{
    public override Task<ActionResult<ICollection<Dto.DecisionRecord>>> ListDecisions(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<ICollection<Dto.DecisionRecord>>>(ApiResults.NotYetBuilt("M4", "DEC-001 to DEC-003"));

    public override Task<ActionResult<Dto.DecisionRecord>> GetDecision(Guid decision_id, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<Dto.DecisionRecord>>(ApiResults.NotYetBuilt("M4", "DEC-001 to DEC-003"));
}

[Authorize(Policy = ApiPolicies.Read)]
public sealed class AuditController : Dto.AuditControllerBase
{
    public override Task<ActionResult<ICollection<Dto.AuditEntry>>> ListAuditEntries(
        Guid? entity_id,
        Dto.AuditEventType? event_type,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<ICollection<Dto.AuditEntry>>>(ApiResults.NotYetBuilt("M4", "AUD-001"));

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

[Authorize(Policy = ApiPolicies.Admin)]
public sealed class ConfigurationController : Dto.ConfigurationControllerBase
{
    public override Task<IActionResult> SetWeightingScheme(Dto.WeightingScheme body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotYetBuilt("M3", "CLS-002, NFR-003"));

    public override Task<IActionResult> SetDeployConditions(Dto.DeployConditionsConfig body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotYetBuilt("M4", "DEC-001, NFR-003"));

    public override Task<IActionResult> SetDislocationThreshold(Dto.Body body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotYetBuilt("M3", "CLS-006, NFR-003"));

    public override Task<IActionResult> SetRiskLimits(Dto.RiskLimitsConfig body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotImplemented(RiskCapability.NotImplementedReason));

    public override Task<IActionResult> SetHoldingPeriod(Dto.Body2 body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotImplemented(ExitsCapability.NotImplementedReason));

    public override Task<IActionResult> SetExecutionMode(Dto.Body3 body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotImplemented(PositionsCapability.NotImplementedReason));

    public override Task<IActionResult> SetApprovalThreshold(Dto.Body4 body, CancellationToken cancellationToken = default) =>
        Task.FromResult<IActionResult>(ApiResults.NotImplemented(ApprovalCapability.NotImplementedReason));
}

[Authorize(Policy = ApiPolicies.Admin)]
public sealed class ReplayController : Dto.ReplayControllerBase
{
    public override Task<ActionResult<Dto.ReplayResponse>> ReplayEvent(Dto.ReplayRequest body, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<Dto.ReplayResponse>>(ApiResults.NotYetBuilt("M5", "ANA-001"));
}
