using PrimeScore.Api.Contracts;
using PrimeScore.Modules.Analytics.Contracts;
using Module = PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Engine.Host.Api;

/// <summary>
/// Catalyst calendar views and ratio readings to the contract (brief §5 rule 4). The ratio keeps
/// only the six contract fields; its diagnostics (placebo days, halo exclusions, sessions,
/// unscreened families) stay in the module contract and on the page. No index level is mapped.
/// </summary>
internal static class CatalystDtos
{
    public static Catalyst From(Module.CatalystView view, CatalystRatioReading? reading) => new()
    {
        Catalyst_id = view.CatalystId,
        Family = From(view.Family),
        Title = view.Title,
        Reference_period = view.ReferencePeriod,
        Scheduled_at = view.ScheduledAt,
        Scheduled_date = view.ScheduledDate,
        Time_announced = view.TimeAnnounced,
        Status = From(view.Status),
        Sep = view.Sep,
        Tentative = view.Tentative,
        Vintage = view.Vintage,
        Never_rescheduled = view.NeverRescheduled,
        Backfilled = view.Backfilled,
        Derivation = view.Derivation,
        First_announced_at = view.FirstAnnouncedAt,
        Actual_at = view.ActualAt,
        Source = From(view.Source),
        Ledger_sequence = view.LedgerSequence,
        Ledger_hash = view.LedgerHash,
        Ratio = reading is null ? null : From(reading),
    };

    public static CatalystDetail From(Module.CatalystDetail detail, CatalystRatioReading? reading) => new()
    {
        Catalyst = From(detail.Catalyst, reading),
        Vintages = detail.Vintages.Select(From).ToList(),
    };

    public static CatalystVintage From(Module.CatalystVintageView view) => new()
    {
        Vintage = view.Vintage,
        Change = view.Change is { } change ? From(change) : null,
        Scheduled_at = view.ScheduledAt,
        Scheduled_date = view.ScheduledDate,
        Time_announced = view.TimeAnnounced,
        Status = From(view.Status),
        Sep = view.Sep,
        Tentative = view.Tentative,
        Derivation = view.Derivation,
        Recorded_at = view.RecordedAt,
        Source = From(view.Source),
        Ledger_sequence = view.LedgerSequence,
        Ledger_hash = view.LedgerHash,
    };

    public static CatalystRatio From(CatalystRatioReading reading) => new()
    {
        As_of = reading.AsOf,
        Value = reading.Value,
        Baseline_percentile = reading.BaselinePercentile,
        Baseline_n = reading.BaselineCount,
        Missing_closes = reading.MissingCloses,
        No_baseline_reason = reading.NoBaselineReason,
    };

    public static Module.CatalystFamily ToModule(CatalystFamily family) => family switch
    {
        CatalystFamily.FOMC => Module.CatalystFamily.Fomc,
        CatalystFamily.CPI => Module.CatalystFamily.Cpi,
        CatalystFamily.NFP => Module.CatalystFamily.Nfp,
        CatalystFamily.CLAIMS => Module.CatalystFamily.Claims,
        CatalystFamily.GDP => Module.CatalystFamily.Gdp,
        CatalystFamily.PCE => Module.CatalystFamily.Pce,
        CatalystFamily.WPSR => Module.CatalystFamily.Wpsr,
        CatalystFamily.OPEC => Module.CatalystFamily.Opec,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown catalyst family."),
    };

    private static CatalystFamily From(Module.CatalystFamily family) => family switch
    {
        Module.CatalystFamily.Fomc => CatalystFamily.FOMC,
        Module.CatalystFamily.Cpi => CatalystFamily.CPI,
        Module.CatalystFamily.Nfp => CatalystFamily.NFP,
        Module.CatalystFamily.Claims => CatalystFamily.CLAIMS,
        Module.CatalystFamily.Gdp => CatalystFamily.GDP,
        Module.CatalystFamily.Pce => CatalystFamily.PCE,
        Module.CatalystFamily.Wpsr => CatalystFamily.WPSR,
        Module.CatalystFamily.Opec => CatalystFamily.OPEC,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown catalyst family."),
    };

    private static CatalystStatus From(Module.CatalystStatus status) => status switch
    {
        Module.CatalystStatus.Scheduled => CatalystStatus.Scheduled,
        Module.CatalystStatus.Cancelled => CatalystStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown catalyst status."),
    };

    private static CatalystChange From(Module.CatalystChange change) => change switch
    {
        Module.CatalystChange.Moved => CatalystChange.Moved,
        Module.CatalystChange.Status => CatalystChange.Status,
        Module.CatalystChange.TimeAnnounced => CatalystChange.Time_announced,
        Module.CatalystChange.Correction => CatalystChange.Correction,
        Module.CatalystChange.Detail => CatalystChange.Detail,
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown catalyst change."),
    };

    private static CatalystSource From(Module.CatalystSourceView source) => new()
    {
        Adapter = source.Adapter,
        Kind = source.Kind switch
        {
            Module.CatalystSourceKind.Listing => CatalystSourceKind.Listing,
            Module.CatalystSourceKind.Archive => CatalystSourceKind.Archive,
            Module.CatalystSourceKind.Curated => CatalystSourceKind.Curated,
            Module.CatalystSourceKind.Rule => CatalystSourceKind.Rule,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source.Kind, "Unknown catalyst source kind."),
        },
        Url = source.Url,
        Retrieved_at = source.RetrievedAt,
        File_sha256 = source.FileSha256,
    };
}
