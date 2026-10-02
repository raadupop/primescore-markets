using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Claims;

/// <summary>
/// Weekly claims releases derived by <see cref="ClaimsRule"/> for the standard Thursdays from
/// <c>LookbackWeeks</c> before today through <c>HorizonDays</c> after it (New York dates, from the
/// injected clock), recorded as source kind Rule: an originator's or operator's date for the same
/// week always wins over it. No page is fetched, so the snapshot carries the rule identifier as
/// its URL and no file hash. Coverage is the span of the derived releases, which moves with today.
/// </summary>
internal sealed class ClaimsCalendarAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<ClaimsCalendarOptions> options,
    CatalystRecorder recorder,
    CatalystsReadStore reads,
    IClock clock,
    ILogger<ClaimsCalendarAdapter> logger) : CalendarAdapter(httpClientFactory, recorder, reads, clock, logger)
{
    private static readonly CatalystFamily[] Families = [CatalystFamily.Claims];

    public override string Name => ClaimsCalendarOptions.SourceName;

    public override string Description =>
        "Weekly jobless claims derived by rule from DOL's published practice: Thursday 08:30 New York, the Wednesday before when Thursday is a federal holiday.";

    protected override CalendarOptions? ReadSettings(out string? invalid) =>
        CalendarOptions.TryRead(options, ClaimsCalendarOptions.Section, out invalid);

    protected override string ReadSummary(CalendarRun run) => "derived by rule";

    protected override Task ReadPagesAsync(CalendarRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var settings = options.Value;
        var now = Clock.UtcNow;
        var today = MarketTime.NewYorkDate(now);
        var rows = ClaimsRule.Releases(today.AddDays(-7 * settings.LookbackWeeks), today.AddDays(settings.HorizonDays));
        run.RowsExamined += rows.Count;
        if (rows.Count > 0)
        {
            var dates = rows.Select(row => MarketTime.NewYorkDate(row.ScheduledAt)).ToArray();
            run.Snapshots.Add(new CalendarSnapshot(
                Name, CatalystSourceKind.Rule, ClaimsRule.SourceUrl, now, FileSha256: null, Families, dates.Min(), dates.Max(), ProximityDays: 0, rows));
        }

        return Task.CompletedTask;
    }
}
