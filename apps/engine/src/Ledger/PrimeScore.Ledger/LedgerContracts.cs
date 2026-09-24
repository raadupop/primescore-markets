namespace PrimeScore.Ledger;

/// <summary>
/// The only write path to engine state. An append commits the entries, every matching
/// projection, and the pipeline log in one transaction, or none of them.
/// </summary>
public interface ILedger
{
    Task<IReadOnlyList<LedgerRecord>> AppendAsync(IReadOnlyList<LedgerAppend> entries, CancellationToken cancellationToken);
}

/// <summary>
/// Maintains a module's read model from ledger entries, synchronously, inside the append's
/// transaction. Every row a projection writes carries the <see cref="LedgerRecord.Sequence"/>
/// that produced it (brief §6).
/// </summary>
public interface ILedgerProjection
{
    bool Handles(string kind);

    Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken);
}

/// <summary>Recomputes the chain and records the result (SRS AUD-002).</summary>
public interface ILedgerVerifier
{
    Task<LedgerVerification> VerifyAsync(CancellationToken cancellationToken);
}

public interface ILedgerStatusQuery
{
    Task<LedgerStatus> GetAsync(CancellationToken cancellationToken);
}

public interface IPipelineLogQuery
{
    Task<IReadOnlyList<PipelineLogEntry>> ByCorrelationAsync(Guid correlationId, CancellationToken cancellationToken);
}

/// <summary>Audit views of stored entries: one entry or a contiguous chain segment.</summary>
public interface ILedgerAuditQuery
{
    Task<LedgerRecord?> BySequenceAsync(long sequence, CancellationToken cancellationToken);

    Task<IReadOnlyList<LedgerRecord>> SegmentAsync(long fromSequence, long toSequence, CancellationToken cancellationToken);
}
