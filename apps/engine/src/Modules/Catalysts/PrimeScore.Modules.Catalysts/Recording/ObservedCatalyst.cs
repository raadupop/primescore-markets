using PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Modules.Catalysts.Recording;

/// <summary>One scheduled release or meeting as a source lists it.</summary>
/// <param name="ScheduledAt">The scheduled instant; 00:00 New York of the date when <paramref name="TimeAnnounced"/> is false. Ignored for a placeholder.</param>
/// <param name="SourceKey">What the source identifies the release by (<c>CPI:2026-09</c>); null when the source has no such key (FOMC).</param>
/// <param name="Sep">FOMC only; null when the source does not say.</param>
/// <param name="Derivation"><c>standard-weekday</c>, <c>rule</c> or <c>curated</c>; null for a listed row.</param>
/// <param name="RowSourceUrl">A curated row's own source URL; null to use the snapshot's URL.</param>
/// <param name="RowRetrievedAt">A curated row's own retrieval time; null to use the snapshot's fetch time.</param>
/// <param name="OriginallyScheduledDate">Curated rows only: the New York date the release was first announced for, when it later moved.</param>
/// <param name="Placeholder">The raw cell when the source lists the key without a date (<c>TBD</c>, blank); the row then carries no schedule.</param>
/// <param name="VerifiedBy">Curated rows only: who checked the row against its source.</param>
internal sealed record ObservedCatalyst(
    CatalystFamily Family,
    DateTimeOffset ScheduledAt,
    bool TimeAnnounced,
    string Title,
    string? ReferencePeriod,
    string? SourceKey,
    bool? Sep,
    bool Tentative,
    CatalystStatus Status,
    string? Derivation,
    string? RowSourceUrl = null,
    DateTimeOffset? RowRetrievedAt = null,
    DateOnly? OriginallyScheduledDate = null,
    string? Placeholder = null,
    string? VerifiedBy = null);

/// <summary>Everything one source file (or rule run) lists, with its provenance.</summary>
/// <param name="Adapter">The calendar adapter's name, or <c>import</c>.</param>
/// <param name="SourceUrl">The fetched page, <c>file:&lt;name&gt;</c> for a CSV, or a <c>rule:</c> identifier.</param>
/// <param name="FileSha256">SHA-256 of the fetched or imported bytes, lower-case hex; null for a rule.</param>
/// <param name="Families">The families this source lists; recorded catalysts of other families are never judged by it.</param>
/// <param name="CoverageFrom">
/// First New York date the source claims to list completely; with <paramref name="CoverageTo"/>, recorded
/// catalysts of this adapter inside the range that the source no longer lists are flagged. Null: no such claim.
/// </param>
/// <param name="ProximityDays">FOMC: a row may match a recorded meeting of this adapter up to this many days away that the source no longer lists at its date; 0 elsewhere.</param>
/// <param name="RequestedBy">Who asked for an import; named in the ledger summaries.</param>
internal sealed record CalendarSnapshot(
    string Adapter,
    CatalystSourceKind Kind,
    string SourceUrl,
    DateTimeOffset RetrievedAt,
    string? FileSha256,
    IReadOnlyCollection<CatalystFamily> Families,
    DateOnly? CoverageFrom,
    DateOnly? CoverageTo,
    int ProximityDays,
    IReadOnlyList<ObservedCatalyst> Rows,
    string? RequestedBy = null);

/// <param name="Scheduled">New catalysts (one <c>CatalystScheduled</c> each).</param>
/// <param name="Rescheduled">New vintages (one <c>CatalystRescheduled</c> each).</param>
/// <param name="Unchanged">Rows that matched a recorded schedule and wrote nothing, including rows kept out by precedence.</param>
/// <param name="Flags">Disagreements the operator should see: precedence kept a schedule, a row vanished, a date was withdrawn.</param>
internal sealed record RecordTally(int Scheduled, int Rescheduled, int Unchanged, IReadOnlyList<string> Flags);
