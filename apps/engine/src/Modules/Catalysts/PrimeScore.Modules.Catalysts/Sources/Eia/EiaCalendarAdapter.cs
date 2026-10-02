using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Eia;

/// <summary>
/// Weekly Petroleum Status Report release dates from EIA's schedule page (public domain) as one
/// listing: EIA's standard Wednesday 10:30 New York release for every data week from the first to
/// the last week its exception table lists, and the table's date and time where it names one.
/// Nothing is derived past the table's last week (ADR-0011).
/// </summary>
internal sealed class EiaCalendarAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<EiaCalendarOptions> options,
    CatalystRecorder recorder,
    CatalystsReadStore reads,
    IClock clock,
    ILogger<EiaCalendarAdapter> logger) : CalendarAdapter(httpClientFactory, recorder, reads, clock, logger)
{
    public const string SchedulePage = "petroleum/supply/weekly/schedule.php";

    private static readonly CatalystFamily[] Families = [CatalystFamily.Wpsr];

    public override string Name => EiaCalendarOptions.SourceName;

    public override string Description =>
        "EIA Weekly Petroleum Status Report schedule: Wednesday 10:30 New York and EIA's listed holiday exceptions, through the last week EIA lists; public domain.";

    protected override CalendarOptions? ReadSettings(out string? invalid) =>
        CalendarOptions.TryRead(options, EiaCalendarOptions.Section, out invalid);

    protected override async Task ReadPagesAsync(CalendarRun run, CancellationToken cancellationToken)
    {
        if (await ReadPageAsync(run, options.Value, SchedulePage, WpsrSchedulePage.Parse, cancellationToken).ConfigureAwait(false) is { } read)
        {
            run.Snapshots.Add(Snapshot(Name, CatalystSourceKind.Listing, read, Families, proximityDays: 0));
        }
    }
}
