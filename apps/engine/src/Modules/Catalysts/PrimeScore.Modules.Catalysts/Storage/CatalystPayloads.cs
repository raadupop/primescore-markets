using PrimeScore.Ledger;
using PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Modules.Catalysts.Storage;

/// <summary>Ledger payload of <see cref="LedgerKinds.CatalystScheduled"/>: the first vintage of a catalyst.</summary>
/// <param name="SourceKey">What the source identifies the release by (<c>CPI:2026-09</c>, <c>WPSR:2026-09-25</c>); matching reschedules rely on it.</param>
/// <param name="Tentative">True when the source marks the date tentative; null means false.</param>
/// <param name="SourceUrl">The page, file row URL or rule identifier the schedule was read from.</param>
/// <param name="RetrievedAt">When the source was fetched (a curated row: when its URL was read).</param>
/// <param name="Backfilled">The scheduled instant was not after the recording time: never claimed as known in advance.</param>
/// <param name="VerifiedBy">Curated rows only: who checked the row against its source.</param>
internal sealed record CatalystScheduledPayload(
    string CatalystId,
    CatalystFamily Family,
    string Title,
    string? ReferencePeriod,
    string? SourceKey,
    DateTimeOffset ScheduledAt,
    bool TimeAnnounced,
    CatalystStatus Status,
    bool? Sep,
    bool? Tentative,
    string Adapter,
    CatalystSourceKind SourceKind,
    string SourceUrl,
    DateTimeOffset RetrievedAt,
    string? FileSha256,
    string? Derivation,
    bool Backfilled,
    string? VerifiedBy = null);

/// <summary>
/// Ledger payload of <see cref="LedgerKinds.CatalystRescheduled"/>: a new vintage of a recorded
/// catalyst. Rescheduling is a new entry, never an update; family, title, reference period and
/// source key carry over from the first vintage.
/// </summary>
/// <param name="Vintage">Two or more; one more than the vintage it replaces.</param>
/// <param name="Forced">True when only an operator's forced import let a lower source kind replace the schedule; null means false.</param>
internal sealed record CatalystRescheduledPayload(
    string CatalystId,
    int Vintage,
    CatalystChange Change,
    DateTimeOffset PreviousScheduledAt,
    DateTimeOffset ScheduledAt,
    bool TimeAnnounced,
    CatalystStatus Status,
    bool? Sep,
    bool? Tentative,
    string Adapter,
    CatalystSourceKind SourceKind,
    string SourceUrl,
    DateTimeOffset RetrievedAt,
    string? FileSha256,
    string? Derivation,
    bool? Forced,
    string? VerifiedBy = null);
