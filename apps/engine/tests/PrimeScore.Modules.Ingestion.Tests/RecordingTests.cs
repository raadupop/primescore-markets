using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Sources.Fred;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>Idempotent recording and point-in-time reads against a real engine database.</summary>
public sealed class RecordingTests : IAsyncLifetime
{
    private const string ApiKey = "test-key-never-stored";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-ingestion-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeFred _fred = new();
    private ServiceProvider _services = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
            ["Fred:ApiKey"] = ApiKey,
            ["Fred:CacheHours"] = "0",
            ["Fred:RequestsPerMinute"] = "6000",
            ["Fred:BackfillStart"] = "2026-09-01",
            ["Fred:RetryDelaySeconds"] = "0",
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IClock>(new TestClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration);
        services.Configure<FredOptions>(options => options.BasketSeries = []);
        services.AddHttpClient(FredOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _fred);
        _services = services.BuildServiceProvider();
        await EngineDatabaseInitializer.InitializeAsync(_services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Recording_the_same_key_twice_keeps_one_signal_and_reports_the_duplicate()
    {
        var recorder = _services.GetRequiredService<SignalRecorder>();
        var candidate = Candidate(17.31);

        var first = await recorder.RecordAsync([candidate], Token);
        var second = await recorder.RecordAsync([candidate], Token);
        var revised = await recorder.RecordAsync([Candidate(18.0)], Token);

        Assert.Equal(RecordStatus.Recorded, first[0].Status);
        Assert.Equal(RecordStatus.Duplicate, second[0].Status);
        Assert.Equal(first[0].Id, second[0].Id);
        Assert.Equal(RecordStatus.Revised, revised[0].Status);
        Assert.Contains("conflicts with recorded signal", Assert.Single(revised[0].Errors), StringComparison.Ordinal);
        var page = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter()));
        Assert.Equal(17.31, Assert.Single(page.Signals).Value);
    }

    [Fact]
    public async Task As_of_excludes_signals_observed_later_even_when_they_were_recorded_first()
    {
        var recorder = _services.GetRequiredService<SignalRecorder>();
        await recorder.RecordAsync([Candidate(20.0, day: 3), Candidate(18.0, day: 1), Candidate(19.0, day: 2)], Token);

        var asOfDay2 = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter(AsOf: At(day: 2))));
        var window = await Query<GetObservationSeries, ObservationSeries>(Series(before: At(day: 3)));

        Assert.Equal([19.0, 18.0], asOfDay2.Signals.Select(signal => signal.Value!.Value));
        Assert.Equal([18.0, 19.0], window.Points.Select(point => point.Value));
        Assert.Equal(At(day: 2), window.LastObservedAt);
    }

    [Fact]
    public async Task A_series_holds_one_metric_of_one_category_and_one_point_per_New_York_date()
    {
        var recorder = _services.GetRequiredService<SignalRecorder>();
        await recorder.RecordAsync([Candidate(18.0, day: 1)], Token);
        await recorder.RecordAsync(
        [
            Candidate(99.0, day: 1, variant: "PRICE"),
            Candidate(50.0, day: 1, source: "other_feed", at: At(day: 1).AddMinutes(-15)),
            Candidate(51.0, day: 2, category: SourceCategory.CrossAssetFlow),
        ], Token);

        var series = await Query<GetObservationSeries, ObservationSeries>(Series(before: At(day: 3)));

        // PRICE and the cross-asset row are other series; the second close of 1 September loses to the first recorded.
        Assert.Equal([18.0], series.Points.Select(point => point.Value));
    }

    [Fact]
    public async Task The_tape_filter_matches_instruments_in_any_letter_case()
    {
        await _services.GetRequiredService<SignalRecorder>().RecordAsync([Candidate(18.0)], Token);

        var page = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter(Instrument: "vix")));

        Assert.Equal("VIX", Assert.Single(page.Signals).Instrument);
    }

    [Fact]
    public async Task An_unstorable_signal_is_rejected_alone_and_the_rest_of_its_batch_is_recorded()
    {
        var valid = """{"source_category":"MARKET_DATA","source_identifier":"cboe_vix","timestamp":"2018-02-05T21:15:00Z","payload_type":"STRUCTURED","structured_payload":{"asset_class":"equity_index","instrument":"VIX","metric_type":"IMPLIED_VOLATILITY","value":37.32,"unit":"points","observed_at":"2018-02-05T21:15:00Z"}}""";
        var overflowing = valid.Replace("37.32", "1e999", StringComparison.Ordinal).Replace("2018-02-05", "2018-02-06", StringComparison.Ordinal);

        var ack = await Ingest(valid, overflowing);

        Assert.Equal([true, false], ack.Results.Select(result => result.Accepted));
        Assert.Contains(ack.Results[1].Errors, error => error.StartsWith("structured_payload.value: must be a finite number", StringComparison.Ordinal));
        var rejections = await Query<GetRejections, IReadOnlyList<RejectionView>>(new GetRejections());
        Assert.Contains("1e999", Assert.Single(rejections).Raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resubmitting_a_signal_is_accepted_as_a_duplicate_but_a_different_payload_for_its_key_is_refused()
    {
        var original = """{"source_category":"MARKET_DATA","source_identifier":"cboe_vix","timestamp":"2018-02-05T21:15:00Z","payload_type":"STRUCTURED","structured_payload":{"asset_class":"equity_index","instrument":"VIX","metric_type":"IMPLIED_VOLATILITY","value":37.32,"unit":"points","observed_at":"2018-02-05T21:15:00Z"}}""";
        var first = await Ingest(original);

        var again = await Ingest(original);
        var changed = await Ingest(original.Replace("37.32", "38.00", StringComparison.Ordinal));

        Assert.True(again.Results[0].Accepted);
        Assert.True(again.Results[0].Duplicate);
        Assert.Equal(first.Results[0].SignalId, again.Results[0].SignalId);
        Assert.False(changed.Results[0].Accepted);
        Assert.Contains("conflicts with recorded signal", Assert.Single(changed.Results[0].Errors), StringComparison.Ordinal);
        var page = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter()));
        Assert.Equal(37.32, Assert.Single(page.Signals).Value);
    }

    [Fact]
    public async Task A_FRED_pull_records_observations_skips_missing_values_and_is_idempotent_on_repeat()
    {
        _fred.Series["VIXCLS"] = """
            {"observations":[
              {"realtime_start":"2026-09-23","realtime_end":"2026-09-23","date":"2026-09-18","value":"14.81"},
              {"realtime_start":"2026-09-23","realtime_end":"2026-09-23","date":"2026-09-21","value":"."},
              {"realtime_start":"2026-09-23","realtime_end":"2026-09-23","date":"2026-09-22","value":"14.21"}]}
            """;
        var puller = _services.CreateScope().ServiceProvider.GetRequiredService<FredPuller>();

        var first = await puller.PullAsync(Token);
        var second = await puller.PullAsync(Token);

        Assert.Equal(2, first.Counts.Accepted);
        Assert.Equal(1, first.Counts.Missing);
        Assert.Equal(0, second.Counts.Accepted);
        Assert.Equal(2, second.Counts.Duplicates);
        Assert.All(_fred.Requests, request => Assert.Contains("api_key=" + ApiKey, request, StringComparison.Ordinal));
        var page = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter(Instrument: "VIX")));
        Assert.All(page.Signals, signal =>
        {
            Assert.DoesNotContain(ApiKey, signal.Payload, StringComparison.Ordinal);
            Assert.Equal("FRED", signal.Provenance.Provider);
            Assert.Equal("https://fred.stlouisfed.org/series/VIXCLS", signal.Provenance.Url);
        });
    }

    [Fact]
    public async Task Rows_from_other_providers_never_move_the_adapters_resume_point()
    {
        // Recorded directly, bypassing the API's reservation of the prefix, to prove the second guard.
        await _services.GetRequiredService<SignalRecorder>().RecordAsync([Candidate(15.0, day: 20, source: "fred:VIXCLS")], Token);
        var puller = _services.CreateScope().ServiceProvider.GetRequiredService<FredPuller>();

        await puller.PullAsync(Token);

        Assert.Contains(_fred.Requests, request => request.Contains("series_id=VIXCLS", StringComparison.Ordinal)
            && request.Contains("observation_start=2026-09-01", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failing_series_is_reported_with_the_key_redacted_and_the_others_are_still_pulled()
    {
        _fred.Failures["OVXCLS"] = (HttpStatusCode.BadRequest, $$"""{"error_code":400,"error_message":"Bad request for api_key={{ApiKey}}"}""");
        _fred.Timeouts.Add("VIXCLS");
        _fred.Series["CPIAUCSL"] = """{"observations":[{"realtime_start":"2026-09-11","realtime_end":"9999-12-31","date":"2026-08-01","value":"322.1"}]}""";
        var puller = _services.CreateScope().ServiceProvider.GetRequiredService<FredPuller>();

        var result = await puller.PullAsync(Token);

        Assert.False(result.NothingPulled);
        Assert.Contains(result.SeriesErrors, error => error.Contains("OVXCLS", StringComparison.Ordinal));
        Assert.Contains(result.SeriesErrors, error => error.Contains("VIXCLS", StringComparison.Ordinal));
        Assert.All(result.SeriesErrors, error => Assert.DoesNotContain(ApiKey, error, StringComparison.Ordinal));
        Assert.Equal(4, _fred.Requests.Count(request => request.Contains("series_id=VIXCLS", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_run_in_which_every_series_fails_pulled_nothing()
    {
        _fred.FailEverything = true;
        var puller = _services.CreateScope().ServiceProvider.GetRequiredService<FredPuller>();

        var result = await puller.PullAsync(Token);

        Assert.True(result.NothingPulled);
        Assert.Equal(result.SeriesAttempted, result.SeriesErrors.Count);
    }

    private async Task<IngestSignalsAck> Ingest(params string[] documents)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<IngestSignals, IngestSignalsAck>>()
            .HandleAsync(new IngestSignals(documents.Select(document => JsonDocument.Parse(document).RootElement.Clone()).ToArray(), "tester"), Token);
    }

    private static GetObservationSeries Series(DateTimeOffset before) => new("VIX", SourceCategory.MarketData, "IMPLIED_VOLATILITY", before, 10);

    private async Task<TResult> Query<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync(query, Token);
    }

    private static DateTimeOffset At(int day) => new(2026, 9, day, 20, 15, 0, TimeSpan.Zero);

    private static SignalCandidate Candidate(
        double value,
        int day = 1,
        string source = "cboe_vix",
        string variant = "IMPLIED_VOLATILITY",
        SourceCategory category = SourceCategory.MarketData,
        DateTimeOffset? at = null) => new(
        category, source, "VIX", variant, at ?? At(day), "STRUCTURED", value,
        JsonDocument.Parse($$"""{"instrument":"VIX","value":{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""").RootElement.Clone(),
        new SignalProvenance("api"));

    private sealed class FakeFred : HttpMessageHandler
    {
        public Dictionary<string, string> Series { get; } = [];

        public Dictionary<string, (HttpStatusCode Status, string Body)> Failures { get; } = [];

        /// <summary>Series whose requests time out the way HttpClient reports it: a cancellation nobody requested.</summary>
        public HashSet<string> Timeouts { get; } = [];

        public bool FailEverything { get; set; }

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
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
            }

            var (status, body) = FailEverything
                ? (HttpStatusCode.BadRequest, """{"error_code":400,"error_message":"Bad Request."}""")
                : Failures.TryGetValue(id, out var failure) ? failure : (HttpStatusCode.OK, Series.GetValueOrDefault(id, """{"observations":[]}"""));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
