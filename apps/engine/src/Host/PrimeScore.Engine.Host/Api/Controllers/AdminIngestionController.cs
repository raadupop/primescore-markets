using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel.Cqrs;
using Dto = PrimeScore.Api.Contracts;

namespace PrimeScore.Engine.Host.Api.Controllers;

/// <summary>
/// SRS SIG-001, SIG-002: each signal is validated on its own and recorded or rejected with a
/// structured error; 400 only when the request itself is not an object with a signals array.
/// </summary>
[Authorize(Policy = ApiPolicies.Read)]
public sealed class AdminIngestionController(ICommandHandler<IngestSignals, IngestSignalsAck> ingest) : Dto.Admin_IngestionControllerBase
{
    private static readonly JsonElement JsonNull = JsonDocument.Parse("null").RootElement.Clone();

    [Authorize(Policy = ApiPolicies.Admin)]
    public override async Task<ActionResult<Dto.IngestSignalsResponse>> IngestSignals(
        Dto.IngestSignalsRequest body,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || body?.Signals is not { Count: > 0 } signals)
        {
            return ApiResults.BadRequest("The request body must be a JSON object with a non-empty 'signals' array.");
        }

        var documents = signals.Select(signal => signal?.Raw ?? JsonNull).ToArray();
        var ack = await ingest.HandleAsync(new IngestSignals(documents, User.Identity?.Name ?? "unknown"), cancellationToken).ConfigureAwait(false);
        var response = new Dto.IngestSignalsResponse
        {
            Accepted_count = ack.AcceptedCount,
            Rejected_count = ack.RejectedCount,
            Signals = ack.Results.Select(result => new Dto.SignalResult
            {
                Signal_id = result.SignalId,
                Validation_status = result.Accepted ? Dto.ValidationStatus.ACCEPTED : Dto.ValidationStatus.REJECTED,
                Validation_errors = result.Accepted ? null : result.Errors.ToList(),
            }).ToList(),
        };
        return StatusCode(StatusCodes.Status202Accepted, response);
    }
}
