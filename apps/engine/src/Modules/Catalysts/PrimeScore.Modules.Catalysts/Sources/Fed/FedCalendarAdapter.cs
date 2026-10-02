using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Fed;

/// <summary>
/// FOMC meetings from the Federal Reserve (public domain): the current calendar page as a listing
/// (FOMC may match a recorded meeting up to 21 days away that the page no longer lists at its date,
/// so a moved meeting keeps its id), then each historical page from <c>HistoryFromYear</c> up to the
/// year before the current page's first panel as an archive, under the history rule. History is
/// read only when the current page was, since its first panel year bounds the history.
/// </summary>
internal sealed class FedCalendarAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<FedCalendarOptions> options,
    CatalystRecorder recorder,
    CatalystsReadStore reads,
    IClock clock,
    ILogger<FedCalendarAdapter> logger) : CalendarAdapter(httpClientFactory, recorder, reads, clock, logger)
{
    public const string CurrentPage = "monetarypolicy/fomccalendars.htm";

    public const int ProximityDays = 21;

    private static readonly CatalystFamily[] Families = [CatalystFamily.Fomc];

    public override string Name => FedCalendarOptions.SourceName;

    public override string Description =>
        "Federal Reserve FOMC calendar (current and historical pages): decision at 14:00 New York on the last meeting day; public domain.";

    public static string HistoricalPage(int year) => string.Create(CultureInfo.InvariantCulture, $"monetarypolicy/fomchistorical{year}.htm");

    protected override CalendarOptions? ReadSettings(out string? invalid) =>
        CalendarOptions.TryRead(options, FedCalendarOptions.Section, out invalid);

    protected override async Task ReadPagesAsync(CalendarRun run, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var current = await ReadPageAsync(run, settings, CurrentPage, FomcPages.ParseCurrent, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return;
        }

        run.Snapshots.Add(Snapshot(Name, CatalystSourceKind.Listing, current, Families, ProximityDays));
        if (settings.HistoryFromYear == 0 || current.Page.CoverageFrom is not { } firstListed)
        {
            return;
        }

        for (var year = settings.HistoryFromYear; year < firstListed.Year; year++)
        {
            var path = HistoricalPage(year);
            if (await AlreadyRecordedAsync(settings.PageUrl(path), cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (await ReadPageAsync(run, settings, path, FomcPages.ParseHistorical, cancellationToken).ConfigureAwait(false) is { } page)
            {
                run.Snapshots.Add(Snapshot(Name, CatalystSourceKind.Archive, page, Families, ProximityDays));
            }
        }
    }
}
