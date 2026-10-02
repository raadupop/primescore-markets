using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>
/// The Cboe adapter against a real engine database and a stub HTTP handler: what it records, what
/// it skips, and that a failure anywhere leaves the next poll to download and try again.
/// </summary>
public sealed class CboeAdapterTests : IAsyncLifetime
{
    private const string BaseUrl = "https://cboe.test/files/";

    private static readonly CboeIndex Vix = CboeIndexCatalog.Find("VIX")!;

    /// <summary>Twelve business days from 1 September 2026 (Labor Day, 7 September, has no row).</summary>
    private static readonly DateOnly[] Dates =
        [.. new[] { 1, 2, 3, 4, 8, 9, 10, 11, 14, 15, 16, 17 }.Select(day => new DateOnly(2026, 9, day))];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-cboe-tests", Guid.NewGuid().ToString("N"));
    private readonly StubCboe _cboe = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
    private ServiceProvider _services = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IClock>(_clock)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration);
        services.AddHttpClient(CboeOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _cboe);
        _services = services.BuildServiceProvider();
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
    public async Task The_first_pull_records_the_whole_file_with_the_hash_of_the_bytes_served()
    {
        _cboe.Files["VIX"] = Csv(Dates);

        var result = await Adapter().PullAsync(Token);

        Assert.Equal(new SourceRunCounts(12, 0, 0, 0, 0), result.Counts);
        Assert.Equal(12, result.RecordedSignals.Count);
        Assert.Empty(result.ItemErrors);
        var page = await Signals();
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(_cboe.Files["VIX"]));
        Assert.All(page.Signals, signal =>
        {
            Assert.Equal("cboe:VIX", signal.SourceIdentifier);
            Assert.Equal(expectedHash, signal.Provenance.FileSha256);
            Assert.Equal(BaseUrl + "VIX_History.csv", signal.Provenance.Url);
            Assert.False(signal.Provenance.Reconstructed);
        });
    }

    [Fact]
    public async Task An_unchanged_file_records_nothing_even_after_a_restart()
    {
        _cboe.Files["VIX"] = Csv(Dates);
        var adapter = Adapter();
        await adapter.PullAsync(Token);
        var head = await HeadAsync();

        var again = await adapter.PullAsync(Token);
        var afterRestart = await Adapter().PullAsync(Token);

        Assert.Equal(new SourceRunCounts(0, 0, 0, 0, 0), again.Counts);
        Assert.Equal(new SourceRunCounts(0, 0, 0, 0, 0), afterRestart.Counts);
        Assert.Equal(head, await HeadAsync());
    }

    [Fact]
    public async Task A_new_date_is_recorded_and_a_revised_recent_close_is_counted_but_not_overwritten()
    {
        var adapter = Adapter();
        _cboe.Files["VIX"] = Csv(Dates);
        await adapter.PullAsync(Token);
        var next = Dates.Append(new DateOnly(2026, 9, 18)).ToArray();

        // 16 September is two rows before the new last row, inside OverlapRows = 3 (15, 16, 17 September are re-sent).
        _cboe.Files["VIX"] = Csv(next, revise: (new DateOnly(2026, 9, 16), 99.0));
        var result = await adapter.PullAsync(Token);

        Assert.Equal(new SourceRunCounts(Accepted: 1, Duplicates: 2, Revised: 1, Missing: 0, Rejected: 0), result.Counts);
        var series = await Query<GetObservationSeries, ObservationSeries>(
            new GetObservationSeries("VIX", SourceCategory.MarketData, "IMPLIED_VOLATILITY", Vix.ObservedAt(new DateOnly(2026, 9, 17)), 1));
        Assert.Equal(Close(new DateOnly(2026, 9, 16)), Assert.Single(series.Points).Value);
    }

    [Fact]
    public async Task A_revised_past_close_without_a_new_date_is_not_seen()
    {
        var adapter = Adapter();
        _cboe.Files["VIX"] = Csv(Dates);
        await adapter.PullAsync(Token);

        _cboe.Files["VIX"] = Csv(Dates, revise: (new DateOnly(2026, 9, 16), 99.0));
        var result = await adapter.PullAsync(Token);

        Assert.Equal(new SourceRunCounts(0, 0, 0, 0, 0), result.Counts);
    }

    [Fact]
    public async Task A_date_Cboe_fills_in_later_is_recorded()
    {
        var adapter = Adapter();
        var gap = new DateOnly(2026, 9, 2);
        _cboe.Files["VIX"] = Csv([.. Dates.Where(date => date != gap)]);
        await adapter.PullAsync(Token);

        _cboe.Files["VIX"] = Csv(Dates);
        var result = await adapter.PullAsync(Token);

        Assert.Equal(1, result.Counts.Accepted);
        Assert.Contains((await Signals()).Signals, signal => MarketTime.NewYorkDate(signal.ObservedAt) == gap);
    }

    [Fact]
    public async Task An_out_of_order_file_records_the_same_closes_in_date_order()
    {
        _cboe.Files["VIX"] = Csv([.. Dates.Reverse()]);

        var result = await Adapter().PullAsync(Token);

        Assert.Equal(12, result.Counts.Accepted);
        var page = await Signals();
        Assert.Equal(Dates.Reverse().Select(Close), page.Signals.Select(signal => signal.Value!.Value));
        Assert.Equal(page.Signals.Select(signal => signal.LedgerSequence).OrderDescending(), page.Signals.Select(signal => signal.LedgerSequence));
    }

    [Fact]
    public async Task A_close_whose_stamp_is_after_the_fetch_waits_for_a_later_poll()
    {
        var adapter = Adapter();
        var today = new DateOnly(2026, 9, 24);
        _cboe.Files["VIX"] = Csv([new DateOnly(2026, 9, 23), today]);

        // 20:05Z is 16:05 EDT, before the 16:15 stamp of 24 September.
        _clock.UtcNow = new DateTimeOffset(2026, 9, 24, 20, 5, 0, TimeSpan.Zero);
        var early = await adapter.PullAsync(Token);
        _clock.UtcNow = new DateTimeOffset(2026, 9, 24, 20, 30, 0, TimeSpan.Zero);
        var later = await adapter.PullAsync(Token);

        Assert.Equal(1, early.Counts.Accepted);
        Assert.Contains("1 closes dated after the fetch", early.Note, StringComparison.Ordinal);
        Assert.Equal(1, later.Counts.Accepted);
        Assert.Contains((await Signals()).Signals, signal => MarketTime.NewYorkDate(signal.ObservedAt) == today);
    }

    [Fact]
    public async Task A_second_poll_is_conditional_and_a_304_records_nothing()
    {
        var adapter = Adapter();
        _cboe.Files["VIX"] = Csv(Dates);
        _cboe.ETag = "W/\"abc123\"";
        _cboe.LastModified = new DateTimeOffset(2026, 9, 25, 0, 30, 54, TimeSpan.Zero);
        await adapter.PullAsync(Token);
        var head = await HeadAsync();

        var result = await adapter.PullAsync(Token);

        var conditional = _cboe.Requests[^1];
        Assert.Equal("W/\"abc123\"", conditional.IfNoneMatch);
        Assert.Equal(_cboe.LastModified, conditional.IfModifiedSince);
        Assert.Equal(1, _cboe.NotModifiedAnswers);
        Assert.Equal(new SourceRunCounts(0, 0, 0, 0, 0), result.Counts);
        Assert.Equal(head, await HeadAsync());
    }

    [Fact]
    public async Task When_recording_fails_the_next_poll_downloads_in_full_and_records()
    {
        var adapter = Adapter();
        _cboe.Files["VIX"] = Csv(Dates);
        _cboe.ETag = "W/\"abc123\"";
        await ExecuteAsync("CREATE TRIGGER refuse_append BEFORE INSERT ON ledger BEGIN SELECT RAISE(ABORT, 'append refused by test'); END;");

        var failed = await adapter.PullAsync(Token);
        await ExecuteAsync("DROP TRIGGER refuse_append;");
        var retried = await adapter.PullAsync(Token);

        Assert.True(failed.NothingPulled);
        Assert.StartsWith("VIX: ", Assert.Single(failed.ItemErrors), StringComparison.Ordinal);
        Assert.Null(_cboe.Requests[^1].IfNoneMatch);
        Assert.Equal(12, retried.Counts.Accepted);
    }

    [Fact]
    public async Task One_missing_or_malformed_file_is_an_item_error_and_the_others_are_recorded()
    {
        _cboe.Files["VIX"] = Csv(Dates);
        _cboe.Files["VVIX"] = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Checking your browser</body></html>");

        var result = await Adapter(options => options.Symbols = ["VIX", "VIX6M", "VVIX"]).PullAsync(Token);

        Assert.False(result.NothingPulled);
        Assert.Equal(12, result.Counts.Accepted);
        Assert.Equal(["VIX6M: HTTP 404", "VVIX: VVIX_History.csv: unexpected header; expected DATE then CLOSE or VVIX"], result.ItemErrors);
        Assert.Equal(1, _cboe.Requests.Count(request => request.Path.EndsWith("VIX6M_History.csv", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Server_errors_are_retried_before_the_file_is_reported()
    {
        _cboe.Files["VIX"] = Csv(Dates);
        _cboe.Failures = 2;

        var result = await Adapter().PullAsync(Token);

        Assert.Equal(12, result.Counts.Accepted);
        Assert.Equal(3, _cboe.Requests.Count);
    }

    [Fact]
    public async Task A_file_the_CDN_refuses_is_retried_like_a_rate_limit()
    {
        _cboe.Files["VIX"] = Csv(Dates);
        _cboe.Failures = 2;
        _cboe.FailureStatus = HttpStatusCode.Forbidden;

        var result = await Adapter().PullAsync(Token);

        Assert.Equal(12, result.Counts.Accepted);
        Assert.Empty(result.ItemErrors);
        Assert.Equal(3, _cboe.Requests.Count);
    }

    [Fact]
    public async Task A_timed_out_file_is_retried_then_an_item_error_and_the_others_are_recorded()
    {
        _cboe.Files["VIX"] = Csv(Dates);
        _cboe.Files["VVIX"] = Csv(Dates);
        _cboe.Timeouts.Add("VVIX");

        var result = await Adapter(options => options.Symbols = ["VIX", "VVIX"]).PullAsync(Token);

        Assert.False(result.NothingPulled);
        Assert.Equal(12, result.Counts.Accepted);
        Assert.Equal([$"VVIX: {StubCboe.TimeoutMessage}"], result.ItemErrors);
        Assert.Equal(4, _cboe.Requests.Count(request => request.Path.EndsWith("VVIX_History.csv", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_missing_close_stays_missing_and_is_counted_once_by_the_run_that_records_a_later_close()
    {
        var adapter = Adapter();
        var (gap, last) = (new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 17));
        var eighteenth = new DateOnly(2026, 9, 18);
        _cboe.Files["VIX"] = Csv(Dates, missing: [gap, last]);

        // First run: 12 rows − 2 missing = 10 closes; 9 September is counted, 17 September (the last row) waits for a later close.
        var first = await adapter.PullAsync(Token);

        // 18 September arrives: 17 September is counted now; 14, 15 and 16 September ride along (OverlapRows = 3).
        _cboe.Files["VIX"] = Csv([.. Dates, eighteenth], missing: [gap, last]);
        var second = await adapter.PullAsync(Token);

        // 21 September arrives: neither gap is counted again; 15, 16 and 18 September ride along.
        _cboe.Files["VIX"] = Csv([.. Dates, eighteenth, new DateOnly(2026, 9, 21)], missing: [gap, last]);
        var third = await adapter.PullAsync(Token);

        Assert.Equal(new SourceRunCounts(Accepted: 10, Duplicates: 0, Revised: 0, Missing: 1, Rejected: 0), first.Counts);
        Assert.Equal(new SourceRunCounts(Accepted: 1, Duplicates: 3, Revised: 0, Missing: 1, Rejected: 0), second.Counts);
        Assert.Equal(new SourceRunCounts(Accepted: 1, Duplicates: 3, Revised: 0, Missing: 0, Rejected: 0), third.Counts);
        Assert.DoesNotContain((await Signals()).Signals, signal => MarketTime.NewYorkDate(signal.ObservedAt) is var date && (date == gap || date == last));
    }

    [Fact]
    public async Task Rows_other_providers_recorded_under_cboe_identifiers_do_not_count_as_recorded()
    {
        // An API row under cboe:VIX (possible before the prefix was reserved) on 2 September, stamped
        // 21:15Z instead of the adapter's 20:15Z (16:15 EDT) and outside the overlap re-send.
        var date = new DateOnly(2026, 9, 2);
        await _services.GetRequiredService<SignalRecorder>().RecordAsync(
        [
            new SignalCandidate(SourceCategory.MarketData, "cboe:VIX", "VIX", "IMPLIED_VOLATILITY", new DateTimeOffset(2026, 9, 2, 21, 15, 0, TimeSpan.Zero), "STRUCTURED", 42.0,
                JsonDocument.Parse("""{"instrument":"VIX","value":42.0}""").RootElement.Clone(), new SignalProvenance("api")),
        ], Token);
        _cboe.Files["VIX"] = Csv(Dates);

        var result = await Adapter().PullAsync(Token);

        Assert.Equal(12, result.Counts.Accepted);
        Assert.Contains((await Signals()).Signals, signal => signal.Provenance.Provider == "Cboe" && signal.ObservedAt == Vix.ObservedAt(date));
    }

    [Fact]
    public async Task A_BaseUrl_without_a_scheme_leaves_the_status_readable_with_the_reason()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
            ["Sources:Cboe:Enabled"] = "true",
            ["Sources:Cboe:BaseUrl"] = "cdn.cboe.com/api/global/us_indices/daily_prices/",
            ["Sources:Cboe:Symbols:0"] = "VIX",
        }).Build();
        await using var provider = new ServiceCollection()
            .AddSingleton<IClock>(_clock)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        var status = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetSourceStatus, IReadOnlyList<SourceStatus>>>()
            .HandleAsync(new GetSourceStatus(), Token);

        var cboe = Assert.Single(status, source => source.Source == "Cboe");
        Assert.Equal("Sources:Cboe:BaseUrl must be an absolute http(s) URL", cboe.DisabledReason);
        Assert.Null(Assert.Single(cboe.Series).Url);
    }

    [Theory]
    [InlineData("Enabled", "yes")]
    [InlineData("PollInterval", "15m")]
    public async Task A_value_that_does_not_bind_disables_only_its_adapter_and_every_status_stays_readable(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
            ["Sources:Cboe:Enabled"] = "true",
            [$"Sources:Cboe:{key}"] = value,
        }).Build();
        await using var provider = new ServiceCollection()
            .AddSingleton<IClock>(_clock)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        var status = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetSourceStatus, IReadOnlyList<SourceStatus>>>()
            .HandleAsync(new GetSourceStatus(), Token);
        var pull = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RequestSourcePull, SourcePullAck>>()
            .HandleAsync(new RequestSourcePull("Cboe"), Token);

        var cboe = Assert.Single(status, source => source.Source == "Cboe");
        Assert.Equal($"invalid configuration: Sources:Cboe:{key} has a value that is not of its type", cboe.DisabledReason);
        Assert.Empty(cboe.Series);
        Assert.Equal("on request only", cboe.Schedule);
        Assert.Single(status, source => source.Source == "FRED");
        Assert.Equal(new SourcePullAck(false, cboe.DisabledReason), pull);
    }

    [Fact]
    public async Task An_earlier_recorded_FRED_close_stays_the_series_of_record_for_its_date()
    {
        var date = new DateOnly(2026, 9, 16);
        var at = Vix.ObservedAt(date);
        await _services.GetRequiredService<SignalRecorder>().RecordAsync(
        [
            new SignalCandidate(SourceCategory.MarketData, "fred:VIXCLS", "VIX", "IMPLIED_VOLATILITY", at, "STRUCTURED", 42.0,
                JsonDocument.Parse("""{"instrument":"VIX","value":42.0}""").RootElement.Clone(), new SignalProvenance("FRED", SeriesId: "VIXCLS")),
        ], Token);
        _cboe.Files["VIX"] = Csv(Dates);

        var result = await Adapter().PullAsync(Token);

        Assert.Equal(12, result.Counts.Accepted);
        var series = await Query<GetObservationSeries, ObservationSeries>(
            new GetObservationSeries("VIX", SourceCategory.MarketData, "IMPLIED_VOLATILITY", at.AddMinutes(1), 1));
        Assert.Equal(42.0, Assert.Single(series.Points).Value);
    }

    [Fact]
    public void The_adapter_is_disabled_by_default_and_by_invalid_options()
    {
        Assert.Contains("Sources:Cboe:Enabled=true", new CboeOptions().DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("PollInterval", Enabled(options => options.PollInterval = TimeSpan.Zero).DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("PollInterval", Enabled(options => options.PollInterval = TimeSpan.FromHours(15)).DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("unknown Cboe symbol 'EVZ'", Enabled(options => options.Symbols = ["VIX", "EVZ"]).DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("BaseUrl", Enabled(options => options.BaseUrl = "ftp://cboe.test/").DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("BaseUrl", Enabled(options => options.BaseUrl = "cdn.cboe.com/files/").DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("BaseUrl", Enabled(options => options.BaseUrl = "").DisabledReason(), StringComparison.Ordinal);
        Assert.Contains("OverlapRows", Enabled(options => options.OverlapRows = -1).DisabledReason(), StringComparison.Ordinal);
        Assert.Null(Enabled(_ => { }).DisabledReason());
    }

    [Fact]
    public void Configured_symbols_replace_the_default_list()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sources:Cboe:Enabled"] = "true",
            ["Sources:Cboe:Symbols:0"] = "vix",
            ["Sources:Cboe:Symbols:1"] = "VIX9D",
        }).Build();
        using var provider = new ServiceCollection().Configure<CboeOptions>(configuration.GetSection(CboeOptions.Section)).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CboeOptions>>().Value;

        Assert.Equal(["VIX", "VIX9D"], options.SymbolList);
        Assert.Equal(10, new CboeOptions().SymbolList.Count);
    }

    [Fact]
    public void The_schedule_polls_every_day_from_18_00_to_08_00_New_York()
    {
        var adapter = Adapter();

        Assert.Equal("every 15 min from 18:00 to 08:00 next day, every day (New York time)", adapter.Schedule.Describe());

        // 18:00 EDT on 28 September 2026 = 22:00Z.
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero), adapter.Schedule.NextAfter(new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero)));
        Assert.Equal(["cboe:VIX"], adapter.Series.Select(series => series.SourceIdentifier));
    }

    private static CboeOptions Enabled(Action<CboeOptions> configure)
    {
        var options = new CboeOptions { Enabled = true };
        configure(options);
        return options;
    }

    private CboeIndexAdapter Adapter(Action<CboeOptions>? configure = null)
    {
        var options = Enabled(options =>
        {
            options.BaseUrl = BaseUrl;
            options.Symbols = ["VIX"];
            options.OverlapRows = 3;
            options.RetryDelaySeconds = 0;
            configure?.Invoke(options);
        });
        return ActivatorUtilities.CreateInstance<CboeIndexAdapter>(_services, Options.Create(options));
    }

    /// <summary>Invented closes (not Cboe data): 15 plus the day of the month over 100.</summary>
    private static double Close(DateOnly date) => 15 + (date.Day / 100.0);

    /// <param name="missing">Dates whose row is well formed but has an empty close.</param>
    private static byte[] Csv(IEnumerable<DateOnly> dates, (DateOnly Date, double Close)? revise = null, DateOnly[]? missing = null)
    {
        var builder = new StringBuilder("DATE,OPEN,HIGH,LOW,CLOSE\n");
        foreach (var date in dates)
        {
            var close = revise is { } change && change.Date == date ? change.Close : Close(date);
            var text = missing?.Contains(date) == true ? "" : close.ToString("F6", CultureInfo.InvariantCulture);
            builder.Append(CultureInfo.InvariantCulture, $"{date:MM/dd/yyyy},15.000000,16.000000,14.000000,{text}\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private async Task<SignalPage> Signals() =>
        await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter(SourcePrefix: "cboe:", Take: 1000)));

    private async Task<long> HeadAsync() => (await _services.GetRequiredService<ILedgerStatusQuery>().GetAsync(Token)).HeadSequence;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "engine.db")}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task<TResult> Query<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync(query, Token);
    }

    private sealed record Request(string Path, string? IfNoneMatch, DateTimeOffset? IfModifiedSince);

    /// <summary>Serves <c>&lt;SYMBOL&gt;_History.csv</c> from memory; 404 for unknown files; honours If-None-Match.</summary>
    private sealed class StubCboe : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public string? ETag { get; set; }

        public DateTimeOffset? LastModified { get; set; }

        public const string TimeoutMessage = "The request was canceled due to the configured HttpClient.Timeout.";

        /// <summary>Symbols whose request times out the way HttpClient does (no cancellation requested).</summary>
        public HashSet<string> Timeouts { get; } = [];

        /// <summary>Requests to answer with <see cref="FailureStatus"/> before serving.</summary>
        public int Failures { get; set; }

        public HttpStatusCode FailureStatus { get; set; } = HttpStatusCode.ServiceUnavailable;

        public int NotModifiedAnswers { get; private set; }

        public List<Request> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ifNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values) ? string.Join(",", values) : null;
            Requests.Add(new Request(request.RequestUri!.AbsolutePath, ifNoneMatch, request.Headers.IfModifiedSince));
            if (Failures > 0)
            {
                Failures--;
                return Task.FromResult(new HttpResponseMessage(FailureStatus) { Content = new StringContent("<html>busy</html>") });
            }

            var symbol = request.RequestUri.Segments[^1].Replace("_History.csv", "", StringComparison.Ordinal);
            if (Timeouts.Contains(symbol))
            {
                throw new TaskCanceledException(TimeoutMessage, new TimeoutException());
            }

            if (!Files.TryGetValue(symbol, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>not found</html>") });
            }

            if (ETag is not null && ifNoneMatch == ETag)
            {
                NotModifiedAnswers++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            if (ETag is not null)
            {
                response.Headers.TryAddWithoutValidation("ETag", ETag);
            }

            response.Content.Headers.LastModified = LastModified;
            return Task.FromResult(response);
        }
    }
}
