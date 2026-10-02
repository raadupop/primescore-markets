using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Bls;

/// <summary>
/// CPI and Employment Situation (NFP) release dates from BLS (public domain): the per-release
/// schedule pages as listings, then the archived year pages from <c>HistoryFromYear</c> through the
/// year of the earliest per-release row as archives, under the history rule (the overlap year's
/// rows carry its URL once read, so it is read once). Not the BLS ICS feed: it carries no
/// reference month, so a moved release could not be matched to its catalyst (ADR-0011). Every
/// request sends the configured user agent, which must name a contact.
/// </summary>
internal sealed class BlsCalendarAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<BlsCalendarOptions> options,
    CatalystRecorder recorder,
    CatalystsReadStore reads,
    IClock clock,
    ILogger<BlsCalendarAdapter> logger) : CalendarAdapter(httpClientFactory, recorder, reads, clock, logger)
{
    /// <summary>Per-release pages, read first.</summary>
    public static readonly IReadOnlyList<(string Path, CatalystFamily Family)> ReleasePages =
    [
        ("schedule/news_release/cpi.htm", CatalystFamily.Cpi),
        ("schedule/news_release/empsit.htm", CatalystFamily.Nfp),
    ];

    private static readonly CatalystFamily[] ArchiveFamilies = [CatalystFamily.Cpi, CatalystFamily.Nfp];

    public override string Name => BlsCalendarOptions.SourceName;

    public override string Description =>
        "BLS release schedules for CPI and the Employment Situation (current and archived years): release time New York; public domain.";

    public static string ArchivePage(int year) => string.Create(CultureInfo.InvariantCulture, $"schedule/{year}/home.htm");

    protected override CalendarOptions? ReadSettings(out string? invalid) =>
        CalendarOptions.TryRead(options, BlsCalendarOptions.Section, out invalid);

    protected override async Task ReadPagesAsync(CalendarRun run, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        int? firstListedYear = null;
        foreach (var (path, family) in ReleasePages)
        {
            var read = await ReadPageAsync(run, settings, path, html => BlsSchedulePages.ParseRelease(html, family), cancellationToken)
                .ConfigureAwait(false);
            if (read is null)
            {
                continue;
            }

            run.Snapshots.Add(Snapshot(Name, CatalystSourceKind.Listing, read, [family], proximityDays: 0));
            if (read.Page.CoverageFrom is { } from)
            {
                firstListedYear = Math.Min(firstListedYear ?? from.Year, from.Year);
            }
        }

        if (settings.HistoryFromYear == 0 || firstListedYear is not { } lastYear)
        {
            return;
        }

        for (var year = settings.HistoryFromYear; year <= lastYear; year++)
        {
            var path = ArchivePage(year);
            if (await AlreadyRecordedAsync(settings.PageUrl(path), cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (await ReadPageAsync(run, settings, path, BlsSchedulePages.ParseArchive, cancellationToken).ConfigureAwait(false) is { } page)
            {
                run.Snapshots.Add(Snapshot(Name, CatalystSourceKind.Archive, page, ArchiveFamilies, proximityDays: 0));
            }
        }
    }
}
