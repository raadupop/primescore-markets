using System.Text.Json;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Ingestion.Contracts;

/// <summary>
/// Validate and record submitted signals (SRS SIG-001, SIG-002). Each document is judged on
/// its own; valid signals in a batch are recorded even when others are rejected.
/// </summary>
/// <param name="Documents">Each signal exactly as received, in the contract's <c>SignalInput</c> wire format.</param>
/// <param name="SubmittedBy">Name of the authenticated caller, recorded as provenance.</param>
public sealed record IngestSignals(IReadOnlyList<JsonElement> Documents, string SubmittedBy) : ICommand<IngestSignalsAck>;

public sealed record IngestSignalsAck(IReadOnlyList<SignalIngestResult> Results) : ICommandAck
{
    public int AcceptedCount => Results.Count(result => result.Accepted);

    public int RejectedCount => Results.Count(result => !result.Accepted);
}

/// <param name="SignalId">The recorded signal, the existing signal for a duplicate, or the rejection record.</param>
/// <param name="Duplicate">True when an identical key was already recorded; nothing new was written.</param>
public sealed record SignalIngestResult(Guid SignalId, bool Accepted, bool Duplicate, IReadOnlyList<string> Errors) : ICommandAck;

/// <summary>Signals were recorded; downstream stages process pending signals in observation order.</summary>
public sealed record SignalBatchAccepted(CorrelationId CorrelationId, IReadOnlyList<Guid> SignalIds) : IIntegrationEvent;

/// <summary>Signals matching a filter, newest observation first.</summary>
public sealed record GetSignals(SignalFilter Filter) : IQuery<SignalPage>;

/// <param name="AsOf">Point-in-time cut (SRS SIG-004): only signals observed at or before it.</param>
/// <param name="Instrument">Matched without regard to letter case.</param>
/// <param name="Variant">Metric type, indicator and reference period, or event type (exact).</param>
public sealed record SignalFilter(
    DateTimeOffset? AsOf = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    SourceCategory? Category = null,
    string? Instrument = null,
    int Skip = 0,
    int Take = 100,
    string? Variant = null);

public sealed record SignalPage(IReadOnlyList<SignalView> Signals, int Total);

public sealed record SignalView(
    Guid SignalId,
    long LedgerSequence,
    CorrelationId CorrelationId,
    SourceCategory Category,
    string SourceIdentifier,
    string Instrument,
    string Variant,
    DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt,
    string PayloadType,
    double? Value,
    string Payload,
    SignalProvenance Provenance);

/// <param name="Provider">Where the value came from, e.g. <c>FRED</c>, <c>api</c>, <c>HUMAN_CURATED</c>.</param>
/// <param name="FirstReleased">First publication date for series with release vintages (point-in-time basis).</param>
/// <param name="MappingVerified">False when the registry marks the provider series id as unconfirmed.</param>
public sealed record SignalProvenance(
    string Provider,
    string? SeriesId = null,
    string? Url = null,
    DateTimeOffset? RetrievedAt = null,
    DateOnly? FirstReleased = null,
    string? Derivation = null,
    bool? MappingVerified = null,
    string? SubmittedBy = null,
    string? Note = null);

/// <summary>One recorded signal.</summary>
public sealed record GetSignal(Guid SignalId) : IQuery<SignalView?>;

/// <summary>Signals rejected by validation, newest first.</summary>
public sealed record GetRejections(int Take = 50) : IQuery<IReadOnlyList<RejectionView>>;

public sealed record RejectionView(Guid RejectionId, long LedgerSequence, DateTimeOffset RecordedAt, string? SourceIdentifier, IReadOnlyList<string> Errors, string Raw);
