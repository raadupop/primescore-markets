using System.Net;
using System.Text.RegularExpressions;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api;

/// <summary>
/// Event scenarios for an entered position (SRS RSK-004, ADR-0014) end to end: stub Cboe VIX9D, VIX, VIX3M,
/// VIX6M and SPX files (invented, Data/catalysts/README.md) and the Fed's 2026 calendar, observed through the API
/// and the Your trade page. Expected figures come from an independent Black-Scholes-Merton calculation with an
/// exact normal distribution function (Python <c>math.erf</c>), not from the engine.
/// </summary>
public sealed class PositionScenarioTests
{
    private const string FedPage = "/fed/monetarypolicy/fomccalendars.htm";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// As of Thu 2026-09-24: SPX 4900; VIX9D 18.9, VIX 20, VIX3M 22, VIX6M 23. Long SPX 5000 call + put to Fri 2026-10-30,
    /// 36 days: σ = √((0.04 × 30 + (0.0484 × 93 − 1.2) × 6 / 63) / 36) = 0.205102; r 4%, q 1.3%.
    /// Value 26,303.41; theta −331.75 a day; delta −18.95; vega 1,191.42. FOMC-2026-10-28 closes 34 days ahead with 2 days
    /// left, so each past meeting's VIX9D change applies in full: (+1%, −6), (−2%, −6), (+0.5%, +6), (−0.5%, +6), (0, +6),
    /// (+0.2%, −6) give −4,558.68, +9,145.25, −627.72, +2,720.87, +897.40, −1,430.45 against 10,579.60 before the meeting.
    /// </summary>
    [Fact]
    public async Task A_long_straddle_matches_an_independent_valuation_and_the_FOMC_replay_through_the_API_and_the_page()
    {
        await using var stub = await StubHttpServer.StartAsync();
        foreach (var symbol in new[] { "VIX", "VIX9D", "VIX3M", "VIX6M", "SPX" })
        {
            stub.Serve($"/cboe/{symbol}_History.csv", await File.ReadAllBytesAsync(Fixture($"{symbol}_History.csv"), Token));
        }

        stub.Serve(FedPage, await File.ReadAllBytesAsync(Fixture("fomccalendars.htm"), Token), "text/html");
        var settings = new Dictionary<string, string>
        {
            ["Sources__Cboe__Enabled"] = "true",
            ["Sources__Cboe__BaseUrl"] = new Uri(stub.BaseAddress, "cboe/").ToString(),
            ["Sources__Cboe__RunOnStartup"] = "true",
            ["Sources__Cboe__RetryDelaySeconds"] = "0",
            ["Sources__FedCalendar__Enabled"] = "true",
            ["Sources__FedCalendar__BaseUrl"] = new Uri(stub.BaseAddress, "fed/").ToString(),
            ["Sources__FedCalendar__RunOnStartup"] = "true",
            ["Sources__FedCalendar__HistoryFromYear"] = "0",
            ["Sources__FedCalendar__RetryDelaySeconds"] = "0",
        };
        var symbols = new[] { "VIX", "VIX9D", "VIX3M", "VIX6M", "SPX" };
        for (var index = 0; index < symbols.Length; index++)
        {
            settings[$"Sources__Cboe__Symbols__{index}"] = symbols[index];
        }

        await using var engine = await RunningEngine.StartAsync(settings);
        await SourceRuns.WaitForSuccessAsync(engine, "Cboe");
        await SourceRuns.WaitForSuccessAsync(engine, "FedCalendar");
        var client = engine.Client(Role.Read);

        var report = await client.GetPositionScenariosAsync(new PositionScenarioRequest
        {
            Underlying = PositionScenarioRequestUnderlying.SPX,
            Expiry = new DateOnly(2026, 10, 30),
            Legs = [new() { Right = PositionLegInputRight.C, Strike = 5000, Quantity = 1 }, new() { Right = PositionLegInputRight.P, Strike = 5000, Quantity = 1 }],
            Entry_cost = 30_000,
        }, Token);

        Assert.Null(report.Error);
        Assert.Equal((new DateOnly(2026, 9, 24), 4900.0, 36), (report.As_of!.Value, report.Underlying_level!.Value, report.Days_to_expiry!.Value));
        Assert.Equal(0.205102, report.Implied_volatility!.Value, 6);
        Assert.Equal(26_303.4099, report.Value!.Value, 0.01);
        Assert.Equal(-331.7545, report.Theta!.Value, 0.01);
        Assert.Equal(-18.9499, report.Delta!.Value, 0.01);
        Assert.Equal(1_191.4248, report.Vega!.Value, 0.01);
        Assert.Equal((30_000.0, false), (report.Max_loss_at_expiry!.Value, report.Unlimited_loss));
        var fomc = Assert.Single(report.Events);
        Assert.Equal(("FOMC-2026-10-28", CatalystFamily.FOMC, 34, 6), (fomc.Catalyst_id, fomc.Family, fomc.Days_from_as_of, fomc.Scenarios));
        Assert.Equal(-15_723.8121, fomc.Decay_to_event, 0.01);
        Assert.Equal(899.6317, fomc.Mean_move_part!.Value, 0.01);
        Assert.Equal(185.5352, fomc.Mean_volatility_part!.Value, 0.01);
        Assert.Equal(1_024.4468, fomc.Mean_total!.Value, 0.01);
        Assert.Equal(134.8411, fomc.Median_total!.Value, 0.01);
        Assert.Equal(0.5, fomc.Loss_share!.Value, 12);
        Assert.Equal(-4_558.6762, fomc.Worst!.Value, 0.01);
        Assert.Equal(9_145.2531, fomc.Best!.Value, 0.01);

        // A short call is unlimited; an expiry before the latest close is explained, not valued.
        var shortCall = await client.GetPositionScenariosAsync(new PositionScenarioRequest
        {
            Underlying = PositionScenarioRequestUnderlying.SPX,
            Expiry = new DateOnly(2026, 10, 30),
            Legs = [new() { Right = PositionLegInputRight.C, Strike = 5000, Quantity = -1 }],
        }, Token);
        Assert.Equal((true, (double?)null), (shortCall.Unlimited_loss, shortCall.Max_loss_at_expiry));
        var expired = await client.GetPositionScenariosAsync(new PositionScenarioRequest
        {
            Underlying = PositionScenarioRequestUnderlying.SPX,
            Expiry = new DateOnly(2026, 9, 1),
            Legs = [new() { Right = PositionLegInputRight.P, Strike = 5000, Quantity = 1 }],
        }, Token);
        Assert.Contains("after the latest close (2026-09-24)", expired.Error, StringComparison.Ordinal);
        Assert.Null(expired.Value);

        // The page reads the position from its address and shows the same figures.
        using var raw = engine.Http(Role.Read);
        var text = PageText(await raw.GetStringAsync(new Uri(engine.ApiBase, "/your-trade?u=SPX&exp=2026-10-30&legs=1C5000,1P5000&cost=30000"), Token));
        foreach (var expected in new[]
        {
            "Estimated value $26,303", "Worst loss at expiry $30,000", "Vega +$1,191", "Theta -$332",
            "FOMC-2026-10-28", "Lost money", "3 of 6 (50%)", "-$4,559", "+$9,145", "not advice",
        })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        // Without a position the page opens on an example: an at-the-money straddle (4900) to the third Friday of October.
        var example = PageText(await raw.GetStringAsync(new Uri(engine.ApiBase, "/your-trade"), Token));
        Assert.Contains("Example position", example, StringComparison.Ordinal);
        Assert.Contains("22 days to expiry", example, StringComparison.Ordinal);

        // An unreadable leg is explained.
        var unreadable = PageText(await raw.GetStringAsync(new Uri(engine.ApiBase, "/your-trade?u=SPX&exp=2026-10-30&legs=1X5000"), Token));
        Assert.Contains("Enter one to 4 legs", unreadable, StringComparison.Ordinal);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "catalysts", name);

    private static string PageText(string html) => Regex.Replace(
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.CultureInvariant)),
        @"\s+", " ", RegexOptions.CultureInvariant);
}
