using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Catalysts.Contracts;

/// <summary>Catalyst families; the wire names are the upper-case member names (<c>FOMC</c>, <c>CPI</c>, ...).</summary>
public enum CatalystFamily
{
    Fomc,
    Cpi,

    /// <summary>The Employment Situation release (nonfarm payrolls).</summary>
    Nfp,

    /// <summary>Weekly initial unemployment claims.</summary>
    Claims,

    /// <summary>Every GDP estimate release (advance, second, third; the estimate is in the reference period).</summary>
    Gdp,

    /// <summary>Personal Income and Outlays.</summary>
    Pce,

    /// <summary>EIA Weekly Petroleum Status Report.</summary>
    Wpsr,

    /// <summary>OPEC and JMMC meetings; several meetings on one New York date are one catalyst.</summary>
    Opec,
}

public enum CatalystStatus
{
    Scheduled,
    Cancelled,
}

/// <summary>
/// Where a schedule came from, in ascending precedence: a vintage of a lower kind never replaces
/// a vintage of a higher kind, so a daily rule run cannot undo an originator's or operator's date
/// and an archive cannot undo the originator's current listing.
/// </summary>
public enum CatalystSourceKind
{
    /// <summary>Derived by a published rule (weekly claims), not read from a schedule.</summary>
    Rule,

    /// <summary>An operator-curated CSV row with its own source URL and retrieval time.</summary>
    Curated,

    /// <summary>An originator's historical page, which shows the dates releases actually happened.</summary>
    Archive,

    /// <summary>An originator's current release schedule.</summary>
    Listing,
}

/// <summary>Why a vintage after the first was appended. Only <see cref="Moved"/> and <see cref="Status"/> count as reschedules.</summary>
public enum CatalystChange
{
    /// <summary>The New York date changed, or an announced time changed.</summary>
    Moved,

    /// <summary>Scheduled became cancelled or the reverse.</summary>
    Status,

    /// <summary>A time was announced for a catalyst recorded without one, on the same date.</summary>
    TimeAnnounced,

    /// <summary>A rule-derived vintage replaced by a schedule of a higher source kind.</summary>
    Correction,

    /// <summary>Only the SEP marker or the tentative marker changed.</summary>
    Detail,
}

/// <summary>Current schedules with <c>ScheduledAt</c> in <c>[From, To]</c> (inclusive), ordered by instant then id.</summary>
public sealed record GetCatalysts(DateTimeOffset From, DateTimeOffset To, CatalystFamily? Family = null) : IQuery<CatalystList>;

public sealed record CatalystList(IReadOnlyList<CatalystView> Catalysts, DateTimeOffset ComputedAt);

/// <summary>One catalyst with every vintage of its schedule; null when the id is unknown.</summary>
public sealed record GetCatalyst(string CatalystId) : IQuery<CatalystDetail?>;

/// <param name="Vintages">Oldest first; the last one is <paramref name="Catalyst"/>'s current schedule.</param>
public sealed record CatalystDetail(CatalystView Catalyst, IReadOnlyList<CatalystVintageView> Vintages);

/// <summary>A catalyst's current schedule (its latest vintage).</summary>
/// <param name="CatalystId"><c>FAMILY-YYYY-MM-DD</c> from the New York date of the first recorded schedule; fixed for life.</param>
/// <param name="ScheduledDate">New York date of <paramref name="ScheduledAt"/>.</param>
/// <param name="TimeAnnounced">False when the source gives only a date; <paramref name="ScheduledAt"/> is then 00:00 New York.</param>
/// <param name="Sep">FOMC only: the meeting is associated with a Summary of Economic Projections; null for other families.</param>
/// <param name="NeverRescheduled">
/// False when any vintage moved the catalyst or changed its status; null when the first vintage was
/// back-filled (the engine did not see the schedule before the event); otherwise true.
/// </param>
/// <param name="Backfilled">The first vintage's scheduled instant was not after the time the engine recorded it.</param>
/// <param name="Derivation"><c>standard-weekday</c>, <c>rule</c> or <c>curated</c>; null when read from a listed row.</param>
/// <param name="FirstAnnouncedAt">When the engine first recorded the catalyst (the first vintage's ledger time).</param>
/// <param name="ActualAt">When the release actually happened; null until prints are recorded.</param>
public sealed record CatalystView(
    string CatalystId,
    CatalystFamily Family,
    string Title,
    string? ReferencePeriod,
    DateTimeOffset ScheduledAt,
    DateOnly ScheduledDate,
    bool TimeAnnounced,
    CatalystStatus Status,
    bool? Sep,
    bool Tentative,
    int Vintage,
    bool? NeverRescheduled,
    bool Backfilled,
    string? Derivation,
    DateTimeOffset FirstAnnouncedAt,
    DateTimeOffset? ActualAt,
    CatalystSourceView Source,
    long LedgerSequence,
    string LedgerHash);

/// <param name="Adapter">The calendar adapter's name, or <c>import</c> for an operator CSV import.</param>
/// <param name="Url">The page, file or rule identifier the schedule was read from.</param>
/// <param name="FileSha256">SHA-256 of the fetched or imported file; null for rule-derived schedules.</param>
public sealed record CatalystSourceView(string Adapter, CatalystSourceKind Kind, string Url, DateTimeOffset RetrievedAt, string? FileSha256);

/// <param name="Change">Null for the first vintage.</param>
/// <param name="RecordedAt">Ledger time of the vintage.</param>
public sealed record CatalystVintageView(
    int Vintage,
    CatalystChange? Change,
    DateTimeOffset ScheduledAt,
    DateOnly ScheduledDate,
    bool TimeAnnounced,
    CatalystStatus Status,
    bool? Sep,
    bool Tentative,
    string? Derivation,
    DateTimeOffset RecordedAt,
    CatalystSourceView Source,
    long LedgerSequence,
    string LedgerHash);

/// <summary>
/// Records the rows of an operator-curated catalyst CSV (the back-fill path). The whole file is
/// rejected when any row is invalid. <paramref name="Force"/> lets curated rows replace schedules
/// of a higher source kind; each such vintage is marked forced.
/// </summary>
public sealed record ImportCatalysts(string FileName, string Csv, string FileSha256, string ImportedBy, bool Force = false) : ICommand<ImportCatalystsAck>;

/// <param name="Unchanged">Rows that matched a recorded schedule and wrote nothing, including rows kept out by precedence (see <paramref name="Flags"/>).</param>
/// <param name="Errors">Validation errors with line numbers; when present nothing was recorded.</param>
public sealed record ImportCatalystsAck(int Scheduled, int Rescheduled, int Unchanged, IReadOnlyList<string> Flags, IReadOnlyList<string> Errors) : ICommandAck;
