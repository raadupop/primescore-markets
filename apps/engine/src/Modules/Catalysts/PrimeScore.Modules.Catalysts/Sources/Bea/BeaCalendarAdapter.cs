using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Bea;

/// <summary>
/// GDP and PCE release dates from BEA's release calendar feed (public domain) as one listing. The
/// feed rather than the schedule web page: it spans the previous and the current year and states
/// UTC instants, so no time zone is inferred (ADR-0011).
/// </summary>
internal sealed class BeaCalendarAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<BeaCalendarOptions> options,
    CatalystRecorder recorder,
    CatalystsReadStore reads,
    IClock clock,
    ILogger<BeaCalendarAdapter> logger) : CalendarAdapter(httpClientFactory, recorder, reads, clock, logger)
{
    public const string CalendarFile = "news/schedule/ics/online-calendar-subscription.ics";

    private static readonly CatalystFamily[] Families = [CatalystFamily.Gdp, CatalystFamily.Pce];

    public override string Name => BeaCalendarOptions.SourceName;

    public override string Description =>
        "BEA release calendar (iCalendar feed): GDP estimates and Personal Income and Outlays (PCE), at the instant BEA states; public domain.";

    protected override CalendarOptions? ReadSettings(out string? invalid) =>
        CalendarOptions.TryRead(options, BeaCalendarOptions.Section, out invalid);

    protected override async Task ReadPagesAsync(CalendarRun run, CancellationToken cancellationToken)
    {
        if (await ReadPageAsync(run, options.Value, CalendarFile, BeaSchedule.Parse, cancellationToken).ConfigureAwait(false) is { } read)
        {
            run.Snapshots.Add(Snapshot(Name, CatalystSourceKind.Listing, read, Families, proximityDays: 0));
        }
    }
}
