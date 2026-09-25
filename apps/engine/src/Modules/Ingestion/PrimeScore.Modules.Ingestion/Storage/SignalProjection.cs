using System.Text.Json;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Ingestion.Storage;

/// <summary>Ledger payload of <see cref="LedgerKinds.SignalIngested"/>.</summary>
internal sealed record SignalIngestedPayload(
    Guid SignalId,
    SourceCategory Category,
    string SourceIdentifier,
    string Instrument,
    string Variant,
    DateTimeOffset ObservedAt,
    string PayloadType,
    double? Value,
    JsonElement Payload,
    SignalProvenance Provenance);

/// <summary>Ledger payload of <see cref="LedgerKinds.SignalRejected"/>: the structured error record (SRS SIG-002).</summary>
/// <param name="Raw">The submission as received, kept as text so that no content can prevent the record.</param>
internal sealed record SignalRejectedPayload(
    Guid RejectionId,
    string? SourceIdentifier,
    IReadOnlyList<string> Errors,
    string Raw);

/// <summary>Maintains <c>ing_signals</c> and <c>ing_rejections</c> inside each append's transaction.</summary>
internal sealed class SignalProjection : ILedgerProjection
{
    public bool Handles(string kind) => kind is LedgerKinds.SignalIngested or LedgerKinds.SignalRejected;

    public Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(scope);
        var context = scope.Context<IngestionDbContext>(IngestionDbContext.HistoryTable, IngestionDbContext.Create);
        if (record.Kind == LedgerKinds.SignalIngested)
        {
            var signal = record.PayloadAs<SignalIngestedPayload>();
            context.Signals.Add(new SignalRow
            {
                SignalId = signal.SignalId.ToString("D"),
                Sequence = record.Sequence,
                CorrelationId = record.CorrelationId.ToString(),
                Category = signal.Category.ToString(),
                SourceIdentifier = signal.SourceIdentifier,
                Instrument = signal.Instrument,
                Variant = signal.Variant,
                Provider = signal.Provenance.Provider,
                ObservedAtMs = signal.ObservedAt.ToUnixTimeMilliseconds(),
                RecordedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
                PayloadType = signal.PayloadType,
                Value = signal.Value,
                Payload = CanonicalJson.Canonicalize(signal.Payload.GetRawText()),
                Provenance = CanonicalJson.Serialize(signal.Provenance),
            });
        }
        else
        {
            var rejection = record.PayloadAs<SignalRejectedPayload>();
            context.Rejections.Add(new RejectionRow
            {
                RejectionId = rejection.RejectionId.ToString("D"),
                Sequence = record.Sequence,
                RecordedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
                SourceIdentifier = rejection.SourceIdentifier,
                Errors = CanonicalJson.Serialize(rejection.Errors),
                Raw = rejection.Raw,
            });
        }

        return Task.CompletedTask;
    }
}
