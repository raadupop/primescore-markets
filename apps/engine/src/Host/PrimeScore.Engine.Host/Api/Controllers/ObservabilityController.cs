using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrimeScore.Engine.Host.Health;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Ledger;
using Dto = PrimeScore.Api.Contracts;

namespace PrimeScore.Engine.Host.Api.Controllers;

[Authorize(Policy = ApiPolicies.Read)]
public sealed class ObservabilityController(EngineHealthService health, IPipelineLogQuery logs) : Dto.ObservabilityControllerBase
{
    public override async Task<ActionResult<ICollection<Dto.LogEntry>>> GetLogsByCorrelation(
        Guid correlation_id,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || correlation_id == Guid.Empty)
        {
            return ApiResults.BadRequest("correlation_id is required and must be a UUID.");
        }

        var entries = await logs.ByCorrelationAsync(correlation_id, cancellationToken).ConfigureAwait(false);
        return entries.Select(ToLogEntry).ToList();
    }

    public override async Task<ActionResult<Dto.EngineHealth>> GetHealth(CancellationToken cancellationToken = default)
    {
        var snapshot = await health.GetAsync(cancellationToken).ConfigureAwait(false);
        return new Dto.EngineHealth
        {
            Status = snapshot.Ok ? Dto.EngineHealthStatus.Ok : Dto.EngineHealthStatus.Degraded,
            Engine_version = snapshot.EngineVersion,
            Checked_at = snapshot.CheckedAt,
            Ledger = new Dto.Ledger
            {
                Entries = snapshot.Ledger.Entries,
                Head_sequence = snapshot.Ledger.HeadSequence,
                Last_verified_at = snapshot.Ledger.LastVerification?.VerifiedAt,
                Last_verification_ok = snapshot.Ledger.LastVerification?.Ok,
            },
            Classifier = new Dto.Classifier
            {
                Status = snapshot.Classifier.Reachable ? Dto.ClassifierStatus.Reachable : Dto.ClassifierStatus.Unreachable,
                Ready = snapshot.Classifier.Ready,
                Detail = snapshot.Classifier.Detail,
            },
        };
    }

    private static Dto.LogEntry ToLogEntry(PipelineLogEntry entry) => new()
    {
        Correlation_id = entry.CorrelationId.Value,
        Stage = entry.Stage,
        Timestamp = entry.Timestamp,
        Message = entry.Message,
        Details = new Dictionary<string, object>
        {
            ["kind"] = entry.Kind,
            ["entity_id"] = entry.EntityId,
            ["ledger_sequence"] = entry.Sequence,
            ["config_version"] = entry.ConfigVersion.Value,
        },
    };
}
