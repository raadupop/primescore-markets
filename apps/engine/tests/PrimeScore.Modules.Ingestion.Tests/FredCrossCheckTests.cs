using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Sources.Cboe;
using PrimeScore.Modules.Ingestion.Sources.Fred;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>
/// FRED as a read-only cross-check (ADR-0009) against a real engine database and a stub FRED: it
/// records nothing, stores no FRED value anywhere, and flags only what differs beyond tolerance.
/// Every value below is invented.
/// </summary>
public sealed class FredCrossCheckTests : IAsyncLifetime
{
    private const string ApiKey = "test-key-never-stored";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-fred-tests", Guid.NewGuid().ToString("N"));
    private readonly StubFred _fred = new();
    private ServiceProvider _services = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _services = Build(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
            ["Sources:Fred:Enabled"] = "true",
            ["Sources:Fred:ApiKey"] = ApiKey,
            ["Sources:Fred:BaseUrl"] = "https://fred.test/",
            ["Sources:Fred:RequestsPerMinute"] = "6000",
            ["Sources:Fred:RetryDelaySeconds"] = "0",
            ["Sources:Cboe:Symbols:0"] = "VIX",
            ["Sources:Cboe:Symbols:1"] = "SPX",
        });
        await EngineDatabaseInitializer.InitializeAsync(_services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        new EngineDatabase(Path.Combine(_directory, "engine.db")).ClearPool();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Disagreements_and_dates_Cboe_lacks_are_flagged_without_FREDs_value_and_nothing_is_recorded()
    {
        // Six recorded VIX dates; CompareDays = 5 keeps 15, 16, 17, 21 and 22 September (14 September drops out).
        await SeedAsync("VIX", (14, 19.10), (15, 20.00), (16, 20.00), (17, 20.80), (21, 21.20), (22, 21.30));
        await SeedAsync("SPX", (21, 5000.00), (22, 5000.00));
        _fred.Series["VIXCLS"] =
        [
            ("2026-09-14", "99.00"), // outside the compared range: ignored even though FRED returned it
            ("2026-09-15", "20.01"), // |20.01 − 20.00| = 0.01 > max(0.005, 0.0001 × 20.00 = 0.002) → flag
            ("2026-09-16", "20.004"), // |20.004 − 20.00| = 0.004 ≤ max(0.005, 0.002) → no flag
            ("2026-09-17", "."), // FRED's missing value: not compared, not flagged
            ("2026-09-18", "20.99"), // FRED has a close cboe:VIX lacks → flag
            ("2026-09-21", "21.20"),
            ("2026-09-22", "21.30"),
        ];
        _fred.Series["SP500"] =
        [
            ("2026-09-21", "5000.60"), // |5000.60 − 5000.00| = 0.60 > max(0.005, 0.0001 × 5000.00 = 0.50) → flag
            ("2026-09-22", "5000.40"), // |5000.40 − 5000.00| = 0.40 ≤ 0.50 → no flag
        ];
        var before = await SignalCountAsync();

        var result = await Adapter().PullAsync(Token);

        Assert.Equal(
        [
            "VIX 2026-09-15: FRED VIXCLS disagrees with cboe:VIX beyond tolerance",
            "VIX 2026-09-18: FRED VIXCLS reports a close that cboe:VIX lacks",
            "SPX 2026-09-21: FRED SP500 disagrees with cboe:SPX beyond tolerance",
        ], result.Flags);

        // Compared: VIX 15, 16, 21, 22 (17 is FRED "."; 18 has no Cboe close) + SPX 21, 22 = 6 closes in 2 series.
        Assert.Equal("Compared 6 closes across 2 series; 3 finding(s).", result.Note);
        Assert.All(result.Flags, flag =>
        {
            foreach (var value in new[] { "20.01", "20.004", "20.99", "5000.6", "5000.4", "99" })
            {
                Assert.DoesNotContain(value, flag, StringComparison.Ordinal);
            }
        });
        Assert.Equal(new SourceRunCounts(0, 0, 0, 0, 0), result.Counts);
        Assert.Empty(result.RecordedSignals);
        Assert.Empty(result.ItemErrors);
        Assert.Equal(before, await SignalCountAsync());
        Assert.False(Directory.Exists(Path.Combine(_directory, "cache")));
        Assert.Contains(_fred.Requests, request => request.Contains("series_id=VIXCLS", StringComparison.Ordinal)
            && request.Contains("observation_start=2026-09-15&observation_end=2026-09-22", StringComparison.Ordinal));
        Assert.Contains(_fred.Requests, request => request.Contains("series_id=SP500", StringComparison.Ordinal)
            && request.Contains("observation_start=2026-09-21&observation_end=2026-09-22", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_failing_series_is_an_item_error_without_the_key_or_FREDs_body_after_retries(HttpStatusCode status)
    {
        await SeedAsync("VIX", (21, 21.20));
        await SeedAsync("SPX", (21, 5000.00));
        _fred.Failures["VIXCLS"] = (status, $"<html>busy; 20.01; api_key={ApiKey}</html>");
        _fred.Failures["SP500"] = (HttpStatusCode.BadRequest, $$"""{"error_code":400,"error_message":"Bad Request. The series does not exist. api_key={{ApiKey}}"}""");

        var result = await Adapter().PullAsync(Token);

        Assert.True(result.NothingPulled);
        Assert.Equal(
        [
            $"FRED VIXCLS: HTTP {(int)status}",
            "FRED SP500: HTTP 400 Bad Request. The series does not exist. api_key=***",
        ], result.ItemErrors);
        Assert.Equal(4, _fred.Requests.Count(request => request.Contains("series_id=VIXCLS", StringComparison.Ordinal)));
        Assert.Equal(1, _fred.Requests.Count(request => request.Contains("series_id=SP500", StringComparison.Ordinal)));
        Assert.Empty(result.Flags);
    }

    [Fact]
    public void Error_text_the_scheduler_logs_and_stores_has_the_key_replaced()
    {
        // Called through the contract, as the scheduler does: the interface default returns text unchanged.
        ISourceAdapter adapter = Adapter();

        var redacted = adapter.Redact($"GET https://fred.test/fred/series/observations?api_key={ApiKey} failed");

        Assert.Equal("GET https://fred.test/fred/series/observations?api_key=*** failed", redacted);
    }

    [Fact]
    public async Task A_timed_out_series_is_retried_then_an_item_error_and_the_others_are_still_compared()
    {
        await SeedAsync("VIX", (21, 21.20));
        await SeedAsync("SPX", (21, 5000.00));
        _fred.Timeouts.Add("VIXCLS");
        _fred.Series["SP500"] = [("2026-09-21", "5000.00")];

        var result = await Adapter().PullAsync(Token);

        Assert.False(result.NothingPulled);
        Assert.Equal([$"FRED VIXCLS: {StubFred.TimeoutMessage}"], result.ItemErrors);
        Assert.Equal(4, _fred.Requests.Count(request => request.Contains("series_id=VIXCLS", StringComparison.Ordinal)));
        Assert.Equal("Compared 1 closes across 1 series; 0 finding(s).", result.Note);
    }

    [Fact]
    public async Task Rows_other_providers_recorded_under_cboe_identifiers_are_not_compared()
    {
        // An API row under cboe:VIX (possible before the prefix was reserved) on 18 September,
        // stamped 21:15Z instead of the adapter's 20:15Z (16:15 EDT): it is not a Cboe close.
        await _services.GetRequiredService<SignalRecorder>().RecordAsync(
        [
            new SignalCandidate(SourceCategory.MarketData, "cboe:VIX", "VIX", "IMPLIED_VOLATILITY", new DateTimeOffset(2026, 9, 18, 21, 15, 0, TimeSpan.Zero),
                "STRUCTURED", 25.00, System.Text.Json.JsonDocument.Parse("""{"instrument":"VIX","value":25.0}""").RootElement.Clone(), new SignalProvenance("api")),
        ], Token);
        await SeedAsync("VIX", (21, 21.20), (22, 21.30));
        _fred.Series["VIXCLS"] = [("2026-09-18", "20.99"), ("2026-09-21", "21.20"), ("2026-09-22", "21.30")];

        var result = await Adapter().PullAsync(Token);

        // Only 21 and 22 September are Cboe's, so FRED is asked for that range and 18 September is never
        // compared (SPX has no closes, so it is not asked at all).
        Assert.Empty(result.Flags);
        Assert.Equal("Compared 2 closes across 1 series; 0 finding(s).", result.Note);
        Assert.Contains(_fred.Requests, request => request.Contains("observation_start=2026-09-21&observation_end=2026-09-22", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_recorded_Cboe_closes_nothing_is_requested()
    {
        var result = await Adapter().PullAsync(Token);

        Assert.Empty(_fred.Requests);
        Assert.False(result.NothingPulled);
        Assert.Equal("Nothing to compare: no cboe: closes recorded yet.", result.Note);
    }

    [Fact]
    public void Pairs_come_from_the_registrys_FRED_mappings_plus_VIX3M_and_SPX_minus_excluded_symbols()
    {
        var registry = _services.GetRequiredService<IndicatorRegistry>();

        var pairs = FredSeriesCatalog.CrossCheckPairs(registry, ["VIX", "VIX9D", "VIX3M", "VVIX", "OVX", "SPX"], ["ovx"]);

        // VIX9D has no FRED copy; OVX is excluded (any letter case).
        Assert.Equal(
        [
            new FredCrossCheckPair("VIX", "VIXCLS"),
            new FredCrossCheckPair("VIX3M", "VXVCLS"),
            new FredCrossCheckPair("VVIX", "VVIXCLS"),
            new FredCrossCheckPair("SPX", "SP500"),
        ], pairs);
    }

    [Fact]
    public void The_status_lists_the_series_recorded_until_the_demotion_and_the_run_is_daily_at_08_15_New_York()
    {
        var adapter = Adapter();

        Assert.Contains(adapter.Series, series => series.SourceIdentifier == "fred:VIXCLS");
        Assert.Contains(adapter.Series, series => series.SourceIdentifier == "fred:SP500" && series.Category == SourceCategory.CrossAssetFlow);
        Assert.All(adapter.Series, series => Assert.Contains("no longer updated", series.Timing, StringComparison.Ordinal));

        // 08:15 EDT (UTC−4) on 29 September 2026 = 12:15Z.
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 12, 15, 0, TimeSpan.Zero), adapter.Schedule.NextAfter(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task The_cross_check_is_disabled_by_default_without_a_key_and_by_invalid_options_and_names_a_legacy_key()
    {
        Assert.Equal("disabled by configuration (set Sources:Fred:Enabled=true)", new FredOptions().DisabledReason());
        Assert.StartsWith("no FRED API key configured", new FredOptions { Enabled = true }.DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("CompareDays", Enabled(options => options.CompareDays = 0).DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("AbsoluteTolerance", Enabled(options => options.AbsoluteTolerance = -0.1).DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("RelativeTolerance", Enabled(options => options.RelativeTolerance = double.NaN).DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("BaseUrl", Enabled(options => options.BaseUrl = "not a url").DisabledReason(), StringComparison.Ordinal);
        Assert.Null(Enabled(_ => { }).DisabledReason());

        await using var legacy = Build(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "legacy.db"),
            ["Fred:ApiKey"] = ApiKey,
            ["Sources:Fred:Enabled"] = "true",
        });
        var reason = legacy.GetRequiredService<IOptions<FredOptions>>().Value.DisabledReason();
        Assert.Contains("move it to Sources:Fred:ApiKey", reason, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configured_excluded_symbols_are_the_whole_list()
    {
        await using var provider = Build(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "excluded.db"),
            ["Sources:Fred:ExcludedSymbols:0"] = "VVIX",
        });

        Assert.Equal(["VVIX"], provider.GetRequiredService<IOptions<FredOptions>>().Value.ExcludedSymbols);
        Assert.Empty(new FredOptions().ExcludedSymbols);
    }

    private ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddSingleton<IClock>(new TestClock(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration);
        services.AddHttpClient(FredOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _fred);
        return services.BuildServiceProvider();
    }

    private static FredOptions Enabled(Action<FredOptions> configure)
    {
        var options = new FredOptions { Enabled = true, ApiKey = ApiKey };
        configure(options);
        return options;
    }

    private FredCrossCheck Adapter() => ActivatorUtilities.CreateInstance<FredCrossCheck>(_services);

    /// <summary>Records closes the way the Cboe adapter does (provider Cboe, stamped at the index close), on September 2026 days.</summary>
    private async Task SeedAsync(string symbol, params (int Day, double Close)[] closes)
    {
        var index = CboeIndexCatalog.Find(symbol)!;
        var url = new Uri($"https://cboe.test/{symbol}_History.csv");
        var candidates = closes
            .Select(close => (object)CboeIndexAdapter.Candidate(
                index, new CboeClose(new DateOnly(2026, 9, close.Day), close.Close), url, new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), "0"))
            .ToArray();
        await _services.GetRequiredService<SignalRecorder>().RecordAsync(candidates, Token);
    }

    private async Task<long> SignalCountAsync()
    {
        using var scope = _services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetSignals, SignalPage>>()
            .HandleAsync(new GetSignals(new SignalFilter(Take: 1)), Token);
        return page.Total;
    }

    /// <summary>Answers <c>series/observations</c> in FRED's JSON shape from memory; records each request URL.</summary>
    private sealed class StubFred : HttpMessageHandler
    {
        public Dictionary<string, (string Date, string Value)[]> Series { get; } = [];

        public const string TimeoutMessage = "The request was canceled due to the configured HttpClient.Timeout.";

        public Dictionary<string, (HttpStatusCode Status, string Body)> Failures { get; } = [];

        /// <summary>Series whose request times out the way HttpClient does (no cancellation requested).</summary>
        public HashSet<string> Timeouts { get; } = [];

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            lock (Requests)
            {
                Requests.Add(uri);
            }

            var id = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["series_id"] ?? "";
            if (Timeouts.Contains(id))
            {
                throw new TaskCanceledException(TimeoutMessage, new TimeoutException());
            }

            if (Failures.TryGetValue(id, out var failure))
            {
                return Task.FromResult(new HttpResponseMessage(failure.Status) { Content = new StringContent(failure.Body) });
            }

            var observations = string.Join(",", Series.GetValueOrDefault(id, []).Select(observation => string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"realtime_start":"2026-09-29","realtime_end":"2026-09-29","date":"{{observation.Date}}","value":"{{observation.Value}}"}""")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"observations":[{{observations}}]}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
