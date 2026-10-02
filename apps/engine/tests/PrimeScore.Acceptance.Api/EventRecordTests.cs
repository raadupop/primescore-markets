using System.Net;
using System.Text.RegularExpressions;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api;

/// <summary>
/// The event record (SRS CAT-004, ADR-0013) end to end: the engine reads stub Cboe VIX, VIX9D and SPX
/// files and the Federal Reserve's 2026 FOMC calendar on start-up, and the record is observed through
/// the API and the Event record page only. Fixture values are invented (Data/catalysts/README.md).
/// </summary>
public sealed class EventRecordTests
{
    private const string FedPage = "/fed/monetarypolicy/fomccalendars.htm";
    private static readonly double Scale = Math.Sqrt(9 / 365.0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Hand calculation. Six 2026 meetings are past (the fixture closes end 2026-09-24). Each is read on the
    /// Tuesday before its Wednesday decision (VIX9D 30 on odd days of the month, 24 on even days), closes
    /// on the decision day (14:00 is before the 16:00 close) and its window ends on the Thursday nine days
    /// after the read date. SPX is 5000 except on event days and window ends:
    /// <code>
    /// meeting  read   VIX9D  priced   event day  window end  actual   inside  below 0.798 × priced  VIX9D change
    /// 01-28    01-27  30     4.711%   +1.0%      02-05       +2.0%    yes     yes (3.759%)          −6
    /// 03-18    03-17  30     4.711%   −2.0%      03-26       −5.0%    no      no                    −6
    /// 04-29    04-28  24     3.769%   +0.5%      05-07       +3.5%    yes     no (3.007%)           +6
    /// 06-17    06-16  24     3.769%   −0.5%      06-25       −1.0%    yes     yes                   +6
    /// 07-29    07-28  24     3.769%    0.0%      08-06       +4.0%    no      no                    +6
    /// 09-16    09-15  30     4.711%   +0.2%      09-24       −2.0%    yes     yes                   −6
    /// </code>
    /// Inside 4 of 6; below the straddle estimate 3 of 6; VIX9D fell 3 of 6. Medians: priced 0.27 × √(9/365)
    /// = 4.24%, |actual| (2.0 + 3.5) / 2 = 2.75%, |event day| 0.50%, VIX9D change 0.
    /// </summary>
    [Fact]
    public async Task The_FOMC_record_matches_the_hand_calculation_through_the_API_and_the_page()
    {
        await using var stub = await StubHttpServer.StartAsync();
        foreach (var symbol in new[] { "VIX", "VIX9D", "SPX" })
        {
            stub.Serve($"/cboe/{symbol}_History.csv", await File.ReadAllBytesAsync(Fixture($"{symbol}_History.csv"), Token));
        }

        stub.Serve(FedPage, await File.ReadAllBytesAsync(Fixture("fomccalendars.htm"), Token), "text/html");
        await using var engine = await RunningEngine.StartAsync(new Dictionary<string, string>
        {
            ["Sources__Cboe__Enabled"] = "true",
            ["Sources__Cboe__BaseUrl"] = new Uri(stub.BaseAddress, "cboe/").ToString(),
            ["Sources__Cboe__RunOnStartup"] = "true",
            ["Sources__Cboe__RetryDelaySeconds"] = "0",
            ["Sources__Cboe__Symbols__0"] = "VIX",
            ["Sources__Cboe__Symbols__1"] = "VIX9D",
            ["Sources__Cboe__Symbols__2"] = "SPX",
            ["Sources__FedCalendar__Enabled"] = "true",
            ["Sources__FedCalendar__BaseUrl"] = new Uri(stub.BaseAddress, "fed/").ToString(),
            ["Sources__FedCalendar__RunOnStartup"] = "true",
            ["Sources__FedCalendar__HistoryFromYear"] = "0",
            ["Sources__FedCalendar__RetryDelaySeconds"] = "0",
        });
        await SourceRuns.WaitForSuccessAsync(engine, "Cboe");
        await SourceRuns.WaitForSuccessAsync(engine, "FedCalendar");

        var record = await engine.Client(Role.Read).GetCatalystOutcomesAsync(CatalystFamily.FOMC, Token);

        Assert.Equal(CatalystFamily.FOMC, record.Family);
        Assert.Equal(["VIX9D", "VIX", "SPX"], record.Reference_instruments);
        Assert.Equal(["FOMC-2026-09-16", "FOMC-2026-07-29", "FOMC-2026-06-17", "FOMC-2026-04-29", "FOMC-2026-03-18", "FOMC-2026-01-28"],
            record.Events.Select(row => row.Catalyst_id));
        Assert.Equal((0, 0, 0), (record.Window_open, record.Before_live_start, record.Missing_closes));
        var september = record.Events.First();
        Assert.Equal((new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 24)),
            (september.Read_date, september.Event_close_date, september.Window_end_date));
        Assert.Equal(0.30 * Scale, september.Priced_move!.Value, 9);
        Assert.Equal(-0.02, september.Actual_move!.Value, 9);
        Assert.Equal(0.002, september.Event_day_move!.Value, 9);
        Assert.Equal(-6, september.Volatility_change, 9);
        Assert.Equal(0.2, september.Ratio_before!.Value, 9);
        Assert.Equal((true, true, true), (september.Inside_priced_range, september.Below_straddle_estimate, september.Previously_examined));
        Assert.Equal([true, false, true, true, false, true], record.Events.Select(row => row.Inside_priced_range!.Value));
        Assert.Equal([true, false, true, false, false, true], record.Events.Select(row => row.Below_straddle_estimate!.Value));
        Assert.Equal((6, 4, 3, 3), (record.All.Count, record.All.Inside_priced_range, record.All.Below_straddle_estimate, record.All.Volatility_fell));
        Assert.Equal(0.27 * Scale, record.All.Median_priced_move!.Value, 9);
        Assert.Equal(0.0275, record.All.Median_abs_actual_move!.Value, 9);
        Assert.Equal(0.005, record.All.Median_abs_event_day_move!.Value, 9);
        Assert.Equal(6, record.Latest_12.Count);

        // A family outside the contract's enum is refused.
        using var raw = engine.Http(Role.Read);
        using var invalid = await raw.GetAsync(new Uri(engine.ApiBase, "/api/analytics/catalyst-outcomes?family=EARNINGS"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        // The page shows the same record (READ bearer, as a signed-in reader sees it).
        var text = PageText(await raw.GetStringAsync(new Uri(engine.ApiBase, "/event-record?family=FOMC"), Token));
        foreach (var expected in new[]
        {
            "6 complete events", "±4.24%", "2.75%", "4 of 6 (67%)", "3 of 6 (50%)", "0.50%",
            "FOMC-2026-09-16", "±4.71%", "-2.00%", "±3.77%", "+3.50%", "previously examined",
            "Estimated from volatility indices, not option prices", "overstates buyer losses",
        })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        // The calendar links each family to its record.
        var calendar = await raw.GetStringAsync(new Uri(engine.ApiBase, "/catalysts?from=2026-10-01"), Token);
        Assert.Contains("href=\"event-record?family=FOMC\"", calendar, StringComparison.Ordinal);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "catalysts", name);

    private static string PageText(string html) => Regex.Replace(
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.CultureInvariant)),
        @"\s+", " ", RegexOptions.CultureInvariant);
}
