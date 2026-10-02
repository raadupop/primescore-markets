using System.Security.Cryptography;
using System.Text.Json;
using PrimeScore.Acceptance.Api.Harness;

namespace PrimeScore.Acceptance.Api;

/// <summary>
/// Source adapters end to end (v3 design §7 step 1): the engine pulls from a local stub server on
/// start-up, and the result is observed through the read-only CLI verbs only.
/// </summary>
public sealed class SourceAdapterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// FRED's answer for the last five VIX dates of the fixture (invented, FRED's JSON shape): equal
    /// to the Cboe fixture except 23 September, 18.80 against 17.80.
    /// </summary>
    private const string FredVixcls = """
        {"observations":[
          {"realtime_start":"2026-09-25","realtime_end":"2026-09-25","date":"2026-09-18","value":"18.20"},
          {"realtime_start":"2026-09-25","realtime_end":"2026-09-25","date":"2026-09-21","value":"17.30"},
          {"realtime_start":"2026-09-25","realtime_end":"2026-09-25","date":"2026-09-22","value":"16.40"},
          {"realtime_start":"2026-09-25","realtime_end":"2026-09-25","date":"2026-09-23","value":"18.80"},
          {"realtime_start":"2026-09-25","realtime_end":"2026-09-25","date":"2026-09-24","value":"17.42"}]}
        """;

    [Fact]
    public async Task Cboe_pull_records_the_VIX_close_for_D_with_cboe_provenance_and_FRED_records_nothing()
    {
        var vix = await File.ReadAllBytesAsync(Fixture("VIX_History.csv"), Token);
        var vix9d = await File.ReadAllBytesAsync(Fixture("VIX9D_History.csv"), Token);
        await using var stub = await StubHttpServer.StartAsync();
        stub.Redirect("/cboe/VIX_History.csv", new Uri(stub.BaseAddress, "cboe-cdn/VIX_History.csv").ToString());
        stub.Serve("/cboe-cdn/VIX_History.csv", vix);
        stub.Serve("/cboe/VIX9D_History.csv", vix9d);
        stub.Serve("/fred/fred/series/observations", System.Text.Encoding.UTF8.GetBytes(FredVixcls), "application/json");
        await using var engine = await RunningEngine.StartAsync(new Dictionary<string, string>
        {
            ["Sources__Cboe__Enabled"] = "true",
            ["Sources__Cboe__BaseUrl"] = new Uri(stub.BaseAddress, "cboe/").ToString(),
            ["Sources__Cboe__RunOnStartup"] = "true",
            ["Sources__Cboe__RetryDelaySeconds"] = "0",
            ["Sources__Cboe__Symbols__0"] = "VIX",
            ["Sources__Cboe__Symbols__1"] = "VIX9D",
            ["Sources__Fred__Enabled"] = "true",
            ["Sources__Fred__ApiKey"] = "acceptance-stub",
            ["Sources__Fred__BaseUrl"] = new Uri(stub.BaseAddress, "fred/").ToString(),
            ["Sources__Fred__RunOnStartup"] = "true",
            ["Sources__Fred__RetryDelaySeconds"] = "0",
        });

        var cboe = await SourceRuns.WaitForSuccessAsync(engine, "Cboe");
        var fred = await SourceRuns.WaitForSuccessAsync(engine, "FRED");
        await engine.StopEngineAsync();
        var (fredExit, fredOutput, fredSignals) = await engine.RunEngineCommandAsync("signals", "--provider", "FRED", "--take", "1");
        var vixRows = await SignalsAsync(engine, "VIX");
        var vix9dRows = await SignalsAsync(engine, "VIX9D");

        Assert.Equal(JsonValueKind.Null, cboe.GetProperty("last_error").ValueKind);
        // D = 24 September 2026; 16:15 EDT (UTC-4) = 20:15Z. The fixture's close for D is 17.42 (invented).
        var close = Assert.Single(vixRows, row => row.GetProperty("observed_at").GetDateTimeOffset() == new DateTimeOffset(2026, 9, 24, 20, 15, 0, TimeSpan.Zero));
        Assert.Equal(17.42, close.GetProperty("value").GetDouble());
        Assert.Equal("cboe:VIX", close.GetProperty("source_identifier").GetString());
        Assert.Equal("IMPLIED_VOLATILITY", close.GetProperty("variant").GetString());
        var provenance = close.GetProperty("provenance");
        Assert.Equal("Cboe", provenance.GetProperty("provider").GetString());
        Assert.EndsWith("/cboe/VIX_History.csv", provenance.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(vix)), provenance.GetProperty("file_sha256").GetString());
        Assert.False(provenance.GetProperty("reconstructed").GetBoolean());
        Assert.Equal(DataRows(vix), vixRows.Count);

        // VIX9D went live on 2013-10-01; Cboe back-calculated the rows before it.
        Assert.True(Row(vix9dRows, new DateOnly(2013, 9, 30)).GetProperty("provenance").GetProperty("reconstructed").GetBoolean());
        Assert.False(Row(vix9dRows, new DateOnly(2013, 10, 1)).GetProperty("provenance").GetProperty("reconstructed").GetBoolean());
        Assert.Equal("IMPLIED_VOLATILITY:9D", vix9dRows[0].GetProperty("variant").GetString());

        // Configured symbols replaced the default list, and Cboe's 307 to its CDN host was followed.
        Assert.Equal(
            ["/cboe/VIX9D_History.csv", "/cboe/VIX_History.csv"],
            stub.Requests.Where(path => path.StartsWith("/cboe/", StringComparison.Ordinal)).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains("/cboe-cdn/VIX_History.csv", stub.Requests);

        // FRED is a read-only cross-check: it was asked (non-vacuous), recorded nothing, and its
        // finding names the index and the date but not FRED's value (FRED terms, ADR-0009).
        Assert.Contains(stub.Requests, path => path.StartsWith("/fred/", StringComparison.Ordinal) && path.Contains("series_id=VIXCLS", StringComparison.Ordinal));
        Assert.True(fredExit == 0, fredOutput);
        Assert.Equal(0, SourceRuns.Parse(fredSignals).GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, fred.GetProperty("last_error").ValueKind);
        var flag = Assert.Single(fred.GetProperty("flags").EnumerateArray()).GetString()!;
        Assert.Equal("VIX 2026-09-23: FRED VIXCLS disagrees with cboe:VIX beyond tolerance", flag);
        Assert.DoesNotContain("18.8", flag, StringComparison.Ordinal);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "cboe", name);

    private static int DataRows(byte[] csv) =>
        System.Text.Encoding.UTF8.GetString(csv).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length - 1;

    /// <summary>16:15 New York on <paramref name="date"/>, the stamp of every Cboe volatility-index close (EDT in the dates used here: 20:15Z).</summary>
    private static JsonElement Row(IReadOnlyList<JsonElement> rows, DateOnly date) =>
        Assert.Single(rows, row => row.GetProperty("observed_at").GetDateTimeOffset() == new DateTimeOffset(date.ToDateTime(new TimeOnly(20, 15)), TimeSpan.Zero));

    private static async Task<IReadOnlyList<JsonElement>> SignalsAsync(RunningEngine engine, string instrument)
    {
        var (exitCode, output, standardOutput) = await engine.RunEngineCommandAsync(
            "signals", "--instrument", instrument, "--source-prefix", "cboe:", "--take", "1000");
        Assert.True(exitCode == 0, output);
        var page = SourceRuns.Parse(standardOutput);
        var rows = page.GetProperty("signals").EnumerateArray().ToArray();
        Assert.Equal(page.GetProperty("total").GetInt32(), rows.Length);
        Assert.All(rows, row => Assert.Equal(instrument, row.GetProperty("instrument").GetString()));
        return rows;
    }
}
