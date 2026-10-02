using System.Security.Cryptography;
using System.Text;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api;

/// <summary>
/// The catalyst calendar end to end (v3 design §7 step 2): the engine reads Cboe VIX and VIX9D files
/// and the Federal Reserve's FOMC calendar from a local stub server on start-up, and the calendar,
/// the 9-day/30-day ratio, a reschedule and a curated import are observed through the API, the
/// Catalysts page and the CLI only.
/// </summary>
public sealed class CatalystTests
{
    private const string FedPage = "/fed/monetarypolicy/fomccalendars.htm";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Hand derivation of the ratio (fixture values in Data/catalysts/README.md). D = 2026-10-28
    /// (Wednesday). a = 2026-09-24 (Thursday), the last date with both closes: 18.9 / 20 − 1 = −0.055;
    /// c = D − a = 34 calendar days. Past FOMC Wednesdays on or before a: 01-28, 03-18, 04-29, 06-17,
    /// 07-29, 09-16, giving 18 placebo days p = D' − 7, 14 or 21 days (all Wednesday sessions), each
    /// read at q = p − 34 days, the Thursday five weeks earlier: 2025-12-04, 12-11, 12-18; 2026-01-22,
    /// 01-29, 02-05; 03-05, 03-12, 03-19; 04-23, 04-30, 05-07; 06-04, 06-11, 06-18; 07-23, 07-30, 08-06.
    /// 01-29, 03-19, 04-30, 06-18 and 07-30 are the day after an FOMC decision, inside its one-trading-day
    /// halo: 5 excluded (they carry +0.20, so a missing halo would change n and the rank). The three
    /// December 2025 dates precede the fixture: missing_closes 3. Baseline n = 18 − 5 − 3 = 10:
    /// −0.15, −0.13, −0.11, −0.09, −0.07, −0.05, −0.03, −0.01, +0.01, +0.03. Five are at or below
    /// −0.055 (−0.15 … −0.07), so the percentile is 5 / 10 = 0.5.
    /// </summary>
    [Fact]
    public async Task Catalysts_list_FOMC_2026_10_28_with_its_ratio_from_ledger_data_and_a_reschedule_adds_a_vintage()
    {
        var fedPage = await File.ReadAllBytesAsync(Fixture("fomccalendars.htm"), Token);
        await using var stub = await StubHttpServer.StartAsync();
        stub.Serve("/cboe/VIX_History.csv", await File.ReadAllBytesAsync(Fixture("VIX_History.csv"), Token));
        stub.Serve("/cboe/VIX9D_History.csv", await File.ReadAllBytesAsync(Fixture("VIX9D_History.csv"), Token));
        stub.Serve(FedPage, fedPage, "text/html");
        await using var engine = await RunningEngine.StartAsync(new Dictionary<string, string>
        {
            ["Sources__Cboe__Enabled"] = "true",
            ["Sources__Cboe__BaseUrl"] = new Uri(stub.BaseAddress, "cboe/").ToString(),
            ["Sources__Cboe__RunOnStartup"] = "true",
            ["Sources__Cboe__RetryDelaySeconds"] = "0",
            ["Sources__Cboe__Symbols__0"] = "VIX",
            ["Sources__Cboe__Symbols__1"] = "VIX9D",
            ["Sources__FedCalendar__Enabled"] = "true",
            ["Sources__FedCalendar__BaseUrl"] = new Uri(stub.BaseAddress, "fed/").ToString(),
            ["Sources__FedCalendar__RunOnStartup"] = "true",
            ["Sources__FedCalendar__HistoryFromYear"] = "0",
            ["Sources__FedCalendar__RetryDelaySeconds"] = "0",
        });

        var cboe = await SourceRuns.WaitForSuccessAsync(engine, "Cboe");
        var fed = await SourceRuns.WaitForSuccessAsync(engine, "FedCalendar");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cboe.GetProperty("last_error").ValueKind);
        // The 2026 panel lists 8 meetings, all new.
        Assert.Equal(8, fed.GetProperty("counts").GetProperty("accepted").GetInt32());

        var client = engine.Client(Role.Read);
        var october = Assert.Single(await client.ListCatalystsAsync(At(2026, 10, 1), At(2026, 11, 30), null, Token));
        Assert.Equal("FOMC-2026-10-28", october.Catalyst_id);
        Assert.Equal(CatalystFamily.FOMC, october.Family);
        // 14:00 New York on the second meeting day; EDT (UTC−4) → 18:00Z.
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero), october.Scheduled_at);
        Assert.Equal(new DateOnly(2026, 10, 28), october.Scheduled_date);
        Assert.True(october.Time_announced);
        Assert.Equal(CatalystStatus.Scheduled, october.Status);
        Assert.False(october.Sep);
        Assert.False(october.Tentative);
        Assert.Equal(1, october.Vintage);
        Assert.Equal("FedCalendar", october.Source.Adapter);
        Assert.Equal(CatalystSourceKind.Listing, october.Source.Kind);
        Assert.EndsWith(FedPage, october.Source.Url, StringComparison.Ordinal);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(fedPage)), october.Source.File_sha256);
        // Back-filled only when the engine first saw the meeting at or after its time; the run's
        // real clock decides, so the test states the rule instead of assuming today's date.
        Assert.Equal(october.Scheduled_at <= october.First_announced_at, october.Backfilled);
        Assert.Equal(october.Backfilled ? null : true, october.Never_rescheduled);
        var ratio = october.Ratio!;
        Assert.Equal(new DateOnly(2026, 9, 24), ratio.As_of);
        Assert.Equal(-0.055, ratio.Value!.Value, 1e-12);
        Assert.Equal(10, ratio.Baseline_n);
        Assert.Equal(0.5, ratio.Baseline_percentile);
        Assert.Equal(3, ratio.Missing_closes);
        Assert.Null(ratio.No_baseline_reason);

        var detail = await client.GetCatalystAsync("FOMC-2026-10-28", Token);
        Assert.Equal("FOMC-2026-10-28", detail.Catalyst.Catalyst_id);
        Assert.Null(Assert.Single(detail.Vintages).Change);
        var unknown = await Assert.ThrowsAnyAsync<PrimeScoreApiException>(() => client.GetCatalystAsync("FOMC-2026-10-29", Token));
        Assert.Equal(404, unknown.StatusCode);
        var reversed = await Assert.ThrowsAnyAsync<PrimeScoreApiException>(() => client.ListCatalystsAsync(At(2026, 11, 1), At(2026, 10, 1), null, Token));
        Assert.Equal(400, reversed.StatusCode);
        Assert.Equal(8, (await client.ListCatalystsAsync(At(2026, 1, 1), At(2027, 1, 1), CatalystFamily.FOMC, Token)).Count);

        // The page reads the same calendar and ratio (READ bearer, as a signed-in reader sees it).
        using var reader = engine.Http(Role.Read);
        var page = await reader.GetStringAsync(new Uri(engine.ApiBase, "/catalysts?from=2026-10-01"), Token);
        Assert.Contains("FOMC-2026-10-28", page, StringComparison.Ordinal);
        Assert.Contains("-0.055", page, StringComparison.Ordinal);
        Assert.Contains("50%", page, StringComparison.Ordinal);
        Assert.Contains("n = 10", page, StringComparison.Ordinal);
        Assert.Contains("not yet captured", page, StringComparison.Ordinal);

        // A curated import through the CLI while the engine is stopped: recorded once, idempotent,
        // and a bad file is refused whole with its line number.
        await engine.StopEngineAsync();
        var import = Fixture("import-2014.csv");
        var (firstExit, firstOutput, firstAck) = await engine.RunEngineCommandAsync("import-catalysts", "--file", import);
        Assert.True(firstExit == 0, firstOutput);
        Assert.Equal(1, SourceRuns.Parse(firstAck).GetProperty("scheduled").GetInt32());
        var (againExit, againOutput, againAck) = await engine.RunEngineCommandAsync("import-catalysts", "--file", import);
        Assert.True(againExit == 0, againOutput);
        Assert.Equal(0, SourceRuns.Parse(againAck).GetProperty("scheduled").GetInt32());
        Assert.Equal(1, SourceRuns.Parse(againAck).GetProperty("unchanged").GetInt32());
        var (invalidExit, invalidOutput, _) = await engine.RunEngineCommandAsync("import-catalysts", "--file", Fixture("import-invalid.csv"));
        Assert.Equal(1, invalidExit);
        Assert.Contains("line 2", invalidOutput, StringComparison.Ordinal);

        // The Fed moves the October meeting to November 3-4; only that block of the page changes.
        stub.Serve(FedPage, MoveOctoberMeeting(fedPage), "text/html");
        await engine.RestartEngineAsync();
        var moved = await SourceRuns.WaitForSuccessAsync(engine, "FedCalendar", after: fed.GetProperty("last_success_at").GetDateTimeOffset());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, moved.GetProperty("last_error").ValueKind);
        // One reschedule, seven meetings unchanged.
        Assert.Equal(1, moved.GetProperty("counts").GetProperty("accepted").GetInt32());
        Assert.Equal(7, moved.GetProperty("counts").GetProperty("duplicates").GetInt32());

        client = engine.Client(Role.Read);
        var rescheduled = await client.GetCatalystAsync("FOMC-2026-10-28", Token);
        Assert.Equal(2, rescheduled.Catalyst.Vintage);
        // 14:00 New York on 2026-11-04; EST (UTC−5) after the 2026-11-01 change → 19:00Z.
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 19, 0, 0, TimeSpan.Zero), rescheduled.Catalyst.Scheduled_at);
        Assert.False(rescheduled.Catalyst.Never_rescheduled);
        Assert.Collection(
            rescheduled.Vintages,
            first =>
            {
                Assert.Equal(1, first.Vintage);
                Assert.Equal(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero), first.Scheduled_at);
                Assert.Null(first.Change);
            },
            second =>
            {
                Assert.Equal(2, second.Vintage);
                Assert.Equal(new DateTimeOffset(2026, 11, 4, 19, 0, 0, TimeSpan.Zero), second.Scheduled_at);
                Assert.Equal(CatalystChange.Moved, second.Change);
            });
        Assert.Empty(await client.ListCatalystsAsync(At(2026, 10, 1), At(2026, 10, 31), null, Token));
        Assert.Single((await client.GetCatalystAsync("FOMC-2026-01-28", Token)).Vintages);
        Assert.Single((await client.GetCatalystAsync("FOMC-2026-12-09", Token)).Vintages);
        Assert.Equal(8, (await client.ListCatalystsAsync(At(2026, 1, 1), At(2027, 1, 1), CatalystFamily.FOMC, Token)).Count);

        // The imported row: back-filled with its own source, reschedules unknown.
        var cpi = (await client.GetCatalystAsync("CPI-2014-01-16", Token)).Catalyst;
        Assert.True(cpi.Backfilled);
        Assert.Null(cpi.Never_rescheduled);
        Assert.Equal("curated", cpi.Derivation);
        Assert.Equal("import", cpi.Source.Adapter);
        Assert.Equal(CatalystSourceKind.Curated, cpi.Source.Kind);
        Assert.Equal("https://www.bls.gov/schedule/2014/home.htm", cpi.Source.Url);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(import, Token))), cpi.Source.File_sha256);
        // 08:30 New York on 2014-01-16; EST (UTC−5) → 13:30Z.
        Assert.Equal(new DateTimeOffset(2014, 1, 16, 13, 30, 0, TimeSpan.Zero), cpi.Scheduled_at);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "catalysts", name);

    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Rewrites only the October meeting row (from its month cell through its date cell) to
    /// November 3-4. "27-28" also appears in the January row, which must stay as it is.
    /// </summary>
    private static byte[] MoveOctoberMeeting(byte[] page)
    {
        var html = Encoding.UTF8.GetString(page);
        var start = html.IndexOf("<strong>October</strong>", StringComparison.Ordinal);
        var end = html.IndexOf("</div>", html.IndexOf("fomc-meeting__date", start, StringComparison.Ordinal), StringComparison.Ordinal);
        var block = html[start..end];
        Assert.Contains(">27-28", block, StringComparison.Ordinal);
        var movedBlock = block.Replace("October", "November", StringComparison.Ordinal).Replace(">27-28", ">3-4", StringComparison.Ordinal);
        var moved = html[..start] + movedBlock + html[end..];
        Assert.Contains("<strong>January</strong>", moved, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(moved, ">27-28<"));
        return Encoding.UTF8.GetBytes(moved);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
