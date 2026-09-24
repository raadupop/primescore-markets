using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Ledger;

/// <summary>An entry to append. The payload is stored as canonical JSON.</summary>
/// <param name="Summary">One-line human-readable statement, reported by <c>GET /logs</c>.</param>
public sealed record LedgerAppend(
    string Kind,
    Guid EntityId,
    CorrelationId CorrelationId,
    ConfigVersion ConfigVersion,
    string Summary,
    string Payload)
{
    public static LedgerAppend Create<TPayload>(
        string kind,
        Guid entityId,
        CorrelationId correlationId,
        ConfigVersion configVersion,
        string summary,
        TPayload payload) =>
        new(kind, entityId, correlationId, configVersion, summary, CanonicalJson.Serialize(payload));
}

/// <summary>A committed ledger entry, as seen by projections and audit views.</summary>
public sealed record LedgerRecord(
    long Sequence,
    DateTimeOffset RecordedAt,
    string Kind,
    Guid EntityId,
    CorrelationId CorrelationId,
    ConfigVersion ConfigVersion,
    string Summary,
    string Payload,
    string PrevHash,
    string Hash)
{
    public TPayload PayloadAs<TPayload>() => CanonicalJson.Deserialize<TPayload>(Payload);
}

/// <summary>Result of recomputing the hash chain end to end (SRS AUD-002).</summary>
/// <param name="FirstInvalidSequence">First entry whose sequence, link or hash does not verify; null when valid.</param>
public sealed record LedgerVerification(
    bool Ok,
    long EntriesChecked,
    long HeadSequence,
    long? FirstInvalidSequence,
    string? Reason,
    DateTimeOffset VerifiedAt);

public sealed record LedgerStatus(long Entries, long HeadSequence, string HeadHash, LedgerVerification? LastVerification);

/// <summary>A structured log line derived from a ledger entry (SRS OBS-001).</summary>
public sealed record PipelineLogEntry(
    CorrelationId CorrelationId,
    string Stage,
    DateTimeOffset Timestamp,
    string Message,
    string Kind,
    Guid EntityId,
    long Sequence,
    ConfigVersion ConfigVersion);
