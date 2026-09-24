using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Modules.Exits.Contracts;
using PrimeScore.Modules.Positions.Contracts;
using PrimeScore.Modules.Risk.Contracts;
using Dto = PrimeScore.Api.Contracts;

namespace PrimeScore.Engine.Host.Api.Controllers;

/// <summary>Milestone B slots: the contract stays whole; each call explains why it is 501 (brief §3).</summary>
[Authorize(Policy = ApiPolicies.Read)]
public sealed class PositionsController : Dto.PositionsControllerBase
{
    public override Task<ActionResult<ICollection<Dto.PositionRecord>>> ListPositions(
        Dto.PositionStatus? status,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<ICollection<Dto.PositionRecord>>>(ApiResults.NotImplemented(PositionsCapability.NotImplementedReason));

    public override Task<ActionResult<Dto.PositionRecord>> GetPosition(Guid position_id, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<Dto.PositionRecord>>(ApiResults.NotImplemented(PositionsCapability.NotImplementedReason));
}

[Authorize(Policy = ApiPolicies.Read)]
public sealed class ExitsController : Dto.ExitsControllerBase
{
    public override Task<ActionResult<ICollection<Dto.ExitRecord>>> ListExits(Guid? position_id, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<ICollection<Dto.ExitRecord>>>(ApiResults.NotImplemented(ExitsCapability.NotImplementedReason));
}

[Authorize(Policy = ApiPolicies.Read)]
public sealed class RiskController : Dto.RiskControllerBase
{
    public override Task<ActionResult<Dto.RiskStatus>> GetRiskStatus(CancellationToken cancellationToken = default) =>
        Task.FromResult<ActionResult<Dto.RiskStatus>>(ApiResults.NotImplemented(RiskCapability.NotImplementedReason));
}
