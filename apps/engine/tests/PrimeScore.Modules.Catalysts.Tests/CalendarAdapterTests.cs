using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Sources;
using PrimeScore.Modules.Catalysts.Sources.Bea;
using PrimeScore.Modules.Catalysts.Sources.Bls;
using PrimeScore.Modules.Catalysts.Sources.Eia;
using PrimeScore.Modules.Catalysts.Sources.Fed;
using PrimeScore.Modules.Catalysts.Sources.Opec;
using PrimeScore.Modules.Ingestion.Contracts;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>
/// The calendar adapters against a real engine database and a stub HTTP handler (no network):
/// options, retries, item errors, provenance, idempotence, the history rule and the curated OPEC file.
/// </summary>
public sealed class CalendarAdapterTests : CatalystDatabase
{
    private const string FedBase = "https://fed.test/";
    private const string BlsBase = "https://bls.test/";
    private const string BeaBase = "https://bea.test/";
    private const string EiaBase = "https://eia.test/";
    private const string ContactAgent = "PrimeScoreMarkets/0.1 (calendar reader; contact: ops@example.org)";

    private static readonly byte[] CurrentPage = CalendarFixtures.Bytes("fed/fomccalendars-excerpt.htm");

    private readonly StubPages _pages = new();

    [Fact]
    public void Every_adapter_is_registered_and_disabled_by_default_with_the_key_to_set()
    {
        var adapters = Services.GetServices<ISourceAdapter>().ToArray();

        Assert.Equal(["FedCalendar", "BlsCalendar", "BeaCalendar", "EiaCalendar", "ClaimsCalendar", "OpecCalendar"], adapters.Select(adapter => adapter.Name));
        Assert.All(adapters, adapter =>
        {
            Assert.Equal($"disabled by configuration (set Sources:{adapter.Name}:Enabled=true)", adapter.DisabledReason);
            Assert.False(adapter.RunOnStartup);
            Assert.Empty(adapter.Series);
            Assert.Equal("daily at 06:00, Monday to Friday (New York time)", adapter.Schedule.Describe());
        });
    }

    [Fact]
    public void A_value_that_does_not_bind_disables_the_adapter_naming_the_key_but_not_the_value()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sources:FedCalendar:Enabled"] = "sesame",
        }).Build();
        using var provider = new ServiceCollection()
            .Configure<FedCalendarOptions>(configuration.GetSection(FedCalendarOptions.Section))
            .BuildServiceProvider();
        var adapter = ActivatorUtilities.CreateInstance<FedCalendarAdapter>(Services, provider.GetRequiredService<IOptions<FedCalendarOptions>>());

        Assert.Equal("invalid configuration: Sources:FedCalendar:Enabled has a value that is not of its type", adapter.DisabledReason);
        Assert.DoesNotContain("sesame", adapter.DisabledReason, StringComparison.Ordinal);
        Assert.Equal(SourceSchedule.OnRequest, adapter.Schedule);
        Assert.False(adapter.RunOnStartup);
    }

    [Fact]
    public async Task BlsCalendar_stays_disabled_without_a_contact_and_sends_the_configured_user_agent()
    {
        var anonymous = Bls(options => options.UserAgent = "PrimeScoreMarkets/0.1");
        ServeBlsListings();

        var result = await Bls(options => options.HistoryFromYear = 0).PullAsync(Token);

        Assert.Equal("Sources:BlsCalendar:UserAgent must name a contact (an e-mail address or a URL); BLS may block robots without one", anonymous.DisabledReason);
        Assert.Null(Bls().DisabledReason);
        Assert.Empty(result.ItemErrors);
        Assert.Equal(2, _pages.Requests.Count);
        Assert.All(_pages.Requests, request => Assert.Equal(ContactAgent, request.UserAgent));
    }

    [Fact]
    public async Task A_missing_page_is_an_item_error_and_records_nothing()
    {
        var result = await Fed().PullAsync(Token);

        Assert.Equal(["monetarypolicy/fomccalendars.htm: HTTP 404"], result.ItemErrors);
        Assert.Equal(1, result.ItemsAttempted);
        Assert.True(result.NothingPulled);
        Assert.Equal(new SourceRunCounts(0, 0, 0, 0, 0), result.Counts);
        Assert.Equal(0, await HeadAsync());
        Assert.Single(_pages.Requests); // 404 is not retried
    }

    [Fact]
    public async Task A_503_then_a_200_records_the_page()
    {
        _pages.Serve("/monetarypolicy/fomccalendars.htm", CurrentPage, failures: 1);

        var result = await Fed().PullAsync(Token);

        Assert.Empty(result.ItemErrors);
        Assert.Equal(2, _pages.Requests.Count);

        // 2026, 2025 and 2027 panels: 8 meetings each; the 2025 notation vote is skipped.
        Assert.Equal(new SourceRunCounts(24, 0, 0, 0, 0), result.Counts);
        Assert.Equal("1 of 1 pages read; 24 new, 0 rescheduled, 0 unchanged; skipped: 1 notation vote.", result.Note);
        Assert.Equal(1 + 25, result.ItemsAttempted);
        Assert.Empty(result.RecordedSignals);
    }

    [Fact]
    public async Task Three_503s_are_an_item_error_naming_the_status_not_the_body()
    {
        _pages.Serve("/monetarypolicy/fomccalendars.htm", CurrentPage, failures: 3);

        var result = await Fed().PullAsync(Token);

        Assert.Equal(["monetarypolicy/fomccalendars.htm: HTTP 503 after 3 attempts"], result.ItemErrors);
        Assert.DoesNotContain(result.ItemErrors, error => error.Contains("busy", StringComparison.Ordinal));
        Assert.Equal(3, _pages.Requests.Count);
        Assert.Equal(0, await HeadAsync());
    }

    [Fact]
    public async Task Timeouts_are_retried_and_then_an_item_error()
    {
        _pages.Timeouts.Add("/monetarypolicy/fomccalendars.htm");

        var result = await Fed().PullAsync(Token);

        Assert.Equal(["monetarypolicy/fomccalendars.htm: timed out after 3 attempts"], result.ItemErrors);
        Assert.Equal(3, _pages.Requests.Count);
    }

    [Fact]
    public async Task A_page_whose_layout_is_not_recognised_records_nothing()
    {
        _pages.Serve("/monetarypolicy/fomccalendars.htm", Encoding.UTF8.GetBytes("<html><body>Just a moment...</body></html>"));

        var result = await Fed().PullAsync(Token);

        Assert.Equal(["monetarypolicy/fomccalendars.htm: layout not recognised: no '<year> FOMC Meetings' panel"], result.ItemErrors);
        Assert.Equal(0, await HeadAsync());
    }

    [Fact]
    public async Task A_recorded_schedule_carries_the_page_URL_fetch_time_and_hash_of_the_bytes_served()
    {
        _pages.Serve("/monetarypolicy/fomccalendars.htm", CurrentPage);

        await Fed().PullAsync(Token);

        var detail = await DetailAsync("FOMC-2026-10-28");
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero), detail.Catalyst.ScheduledAt);
        Assert.Equal(
            new CatalystSourceView("FedCalendar", CatalystSourceKind.Listing, FedBase + "monetarypolicy/fomccalendars.htm", Now,
                Convert.ToHexStringLower(SHA256.HashData(CurrentPage))),
            detail.Catalyst.Source);
        Assert.False(detail.Catalyst.Sep);
        Assert.True((await DetailAsync("FOMC-2027-10-27")).Catalyst.Tentative);
    }

    [Fact]
    public async Task A_second_pull_of_the_same_page_writes_nothing_and_a_moved_meeting_is_one_new_vintage()
    {
        _pages.Serve("/monetarypolicy/fomccalendars.htm", CurrentPage);
        var adapter = Fed();
        await adapter.PullAsync(Token);
        var head = await HeadAsync();

        var again = await adapter.PullAsync(Token);

        Assert.Equal(new SourceRunCounts(0, 24, 0, 0, 0), again.Counts);
        Assert.Equal(head, await HeadAsync());

        // Only the October 2026 block moves, to November 3-4.
        var moved = CalendarFixtures.ReplaceAfter(Encoding.UTF8.GetString(CurrentPage), "<strong>October</strong>", "27-28", "3-4");
        moved = CalendarFixtures.ReplaceAfter(moved, "<strong>October</strong>", "<strong>October</strong>", "<strong>November</strong>");
        _pages.Serve("/monetarypolicy/fomccalendars.htm", Encoding.UTF8.GetBytes(moved));

        var result = await adapter.PullAsync(Token);

        // Accepted counts every new ledger entry; Revised stays 0 and the note carries the split.
        Assert.Equal(new SourceRunCounts(1, 23, 0, 0, 0), result.Counts);
        Assert.Equal("1 of 1 pages read; 0 new, 1 rescheduled, 23 unchanged; skipped: 1 notation vote.", result.Note);
        Assert.Equal(head + 1, await HeadAsync());
        var detail = await DetailAsync("FOMC-2026-10-28");
        Assert.Equal(CatalystChange.Moved, detail.Vintages[^1].Change);

        // 4 November 2026 is after the change to standard time on 1 November: 14:00 + 5 h.
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 19, 0, 0, TimeSpan.Zero), detail.Catalyst.ScheduledAt);
    }

    [Fact]
    public async Task A_failed_history_page_leaves_the_run_partial_and_only_that_year_is_requested_again()
    {
        // The current page's first panel is 2025, so HistoryFromYear 2022 reads 2022, 2023 and 2024.
        _pages.Serve("/monetarypolicy/fomccalendars.htm", CurrentPage);
        _pages.Serve("/monetarypolicy/fomchistorical2022.htm", History(2022));
        _pages.Serve("/monetarypolicy/fomchistorical2023.htm", History(2023), failures: 3);
        _pages.Serve("/monetarypolicy/fomchistorical2024.htm", History(2024));
        var adapter = Fed(options => options.HistoryFromYear = 2022);

        var first = await adapter.PullAsync(Token);

        Assert.Equal(["monetarypolicy/fomchistorical2023.htm: HTTP 503 after 3 attempts"], first.ItemErrors);
        Assert.False(first.NothingPulled);

        // 24 current meetings and 8 per history page read (the unscheduled October meeting is skipped).
        Assert.Equal(24 + 8 + 8, first.Counts.Accepted);
        var archived = await DetailAsync("FOMC-2022-09-18");
        Assert.Equal(CatalystSourceKind.Archive, archived.Catalyst.Source.Kind);
        Assert.True(archived.Catalyst.Backfilled);
        Assert.Null(archived.Catalyst.NeverRescheduled);

        _pages.Requests.Clear();
        var second = await adapter.PullAsync(Token);

        Assert.Equal(["/monetarypolicy/fomccalendars.htm", "/monetarypolicy/fomchistorical2023.htm"], _pages.Requests.Select(request => request.Path));
        Assert.Empty(second.ItemErrors);
        Assert.Equal(new SourceRunCounts(8, 24, 0, 0, 0), second.Counts);

        _pages.Requests.Clear();
        await adapter.PullAsync(Token);

        Assert.Equal(["/monetarypolicy/fomccalendars.htm"], _pages.Requests.Select(request => request.Path));
    }

    [Fact]
    public async Task The_BLS_overlap_year_is_read_after_a_503_and_then_never_again()
    {
        // The per-release pages start in December 2025, so HistoryFromYear 2025 reads the 2025 archive only.
        ServeBlsListings();
        _pages.Serve("/schedule/2025/home.htm", CalendarFixtures.Bytes("bls/bls2025-excerpt.htm"), failures: 3);
        var adapter = Bls(options => options.HistoryFromYear = 2025);

        var first = await adapter.PullAsync(Token);

        Assert.Equal(["schedule/2025/home.htm: HTTP 503 after 3 attempts"], first.ItemErrors);

        // 13 CPI and 13 Employment Situation reference months, November 2025 to November 2026.
        Assert.Equal(new SourceRunCounts(26, 0, 0, 0, 0), first.Counts);

        _pages.Requests.Clear();
        var second = await adapter.PullAsync(Token);

        // The archive's 22 rows: its two December releases (reference month November 2025) are already listed.
        Assert.Equal(["/schedule/news_release/cpi.htm", "/schedule/news_release/empsit.htm", "/schedule/2025/home.htm"],
            _pages.Requests.Select(request => request.Path));
        Assert.Equal(new SourceRunCounts(20, 26 + 2, 0, 0, 0), second.Counts);
        var shutdown = await DetailAsync("NFP-2025-11-20");
        Assert.Equal(CatalystSourceKind.Archive, shutdown.Catalyst.Source.Kind);
        Assert.Equal(BlsBase + "schedule/2025/home.htm", shutdown.Catalyst.Source.Url);
        Assert.Equal(CatalystSourceKind.Listing, (await DetailAsync("CPI-2025-12-18")).Catalyst.Source.Kind);

        _pages.Requests.Clear();
        await adapter.PullAsync(Token);

        Assert.Equal(["/schedule/news_release/cpi.htm", "/schedule/news_release/empsit.htm"], _pages.Requests.Select(request => request.Path));
    }

    [Fact]
    public async Task The_BEA_feed_is_recorded_with_its_URL_fetch_time_and_hash()
    {
        var feed = CalendarFixtures.Bytes("bea/schedule-excerpt.ics");
        _pages.Serve("/news/schedule/ics/online-calendar-subscription.ics", feed);

        var result = await Adapter<BeaCalendarAdapter, BeaCalendarOptions>(new BeaCalendarOptions { BaseUrl = BeaBase }).PullAsync(Token);

        // 6 GDP estimates and 7 PCE releases in the excerpt.
        Assert.Equal("1 of 1 pages read; 13 new, 0 rescheduled, 0 unchanged.", result.Note);
        Assert.Empty(result.ItemErrors);
        var gdp = await DetailAsync("GDP-2026-10-29");
        Assert.Equal(
            new CatalystSourceView("BeaCalendar", CatalystSourceKind.Listing, BeaBase + "news/schedule/ics/online-calendar-subscription.ics", Now,
                Convert.ToHexStringLower(SHA256.HashData(feed))),
            gdp.Catalyst.Source);
        Assert.Equal("2026 Q3 advance estimate", gdp.Catalyst.ReferencePeriod);
        Assert.Equal(CatalystFamily.Pce, (await DetailAsync("PCE-2026-10-29")).Catalyst.Family);
    }

    [Fact]
    public async Task The_EIA_schedule_is_recorded_through_its_last_listed_week()
    {
        _pages.Serve("/petroleum/supply/weekly/schedule.php", CalendarFixtures.Bytes("eia/wpsr-schedule-excerpt.htm"));
        var adapter = Adapter<EiaCalendarAdapter, EiaCalendarOptions>(new EiaCalendarOptions { BaseUrl = EiaBase });

        var result = await adapter.PullAsync(Token);

        // 98 data weeks, 2024-12-27 to 2026-11-06 (see WpsrScheduleTests).
        Assert.Equal(new SourceRunCounts(98, 0, 0, 0, 0), result.Counts);
        var next = await DetailAsync("WPSR-2026-09-30");
        Assert.Equal("standard-weekday", next.Catalyst.Derivation);
        Assert.Equal(EiaBase + "petroleum/supply/weekly/schedule.php", next.Catalyst.Source.Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 17, 0, 0, TimeSpan.Zero), (await DetailAsync("WPSR-2026-11-12")).Catalyst.ScheduledAt);
        Assert.Empty(await ListAsync(new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.MaxValue, CatalystFamily.Wpsr));

        var again = await adapter.PullAsync(Token);

        Assert.Equal(new SourceRunCounts(0, 98, 0, 0, 0), again.Counts);
    }

    [Fact]
    public async Task The_OPEC_adapter_records_the_curated_rows_with_their_own_source()
    {
        // Invented row (not an announced meeting): 3 December 2026 is in standard time, 10:00 + 5 h = 15:00Z.
        using var csv = new TempCsv(
            string.Join(",", CatalystCsv.Header) + "\n"
            + "OPEC,2026-12-03,10:00,,OPEC and non-OPEC Ministerial Meeting,,ONOMM-TEST,,https://opec.example/pr-detail/test.html,2026-09-01T12:00:00Z,unit test,invented\n");
        var adapter = Opec(csv.Path);

        var result = await adapter.PullAsync(Token);

        Assert.Equal("file read; 1 new, 0 rescheduled, 0 unchanged.", result.Note);
        Assert.Equal(1 + 1, result.ItemsAttempted);
        var detail = await DetailAsync("OPEC-2026-12-03");
        Assert.Equal(new DateTimeOffset(2026, 12, 3, 15, 0, 0, TimeSpan.Zero), detail.Catalyst.ScheduledAt);
        Assert.Equal("curated", detail.Catalyst.Derivation);
        Assert.Equal(
            new CatalystSourceView("OpecCalendar", CatalystSourceKind.Curated, "https://opec.example/pr-detail/test.html",
                new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(csv.Path)))),
            detail.Catalyst.Source);
        Assert.Empty(_pages.Requests);

        var head = await HeadAsync();
        Assert.Equal(new SourceRunCounts(0, 1, 0, 0, 0), (await adapter.PullAsync(Token)).Counts);
        Assert.Equal(head, await HeadAsync());
    }

    [Fact]
    public async Task An_OPEC_file_with_an_invalid_row_or_a_row_of_another_family_records_nothing()
    {
        using var csv = new TempCsv(
            string.Join(",", CatalystCsv.Header) + "\n"
            + "OPEC,2026-12-03,,,,,ONOMM-TEST,,https://opec.example/a,2026-09-01T12:00:00Z,unit test,\n"
            + "OPEC,2026-13-01,,,,,,,https://opec.example/b,2026-09-01T12:00:00Z,unit test,\n");

        var invalid = await Opec(csv.Path).PullAsync(Token);

        Assert.Equal(["opec.csv: line 3: scheduled_date must be YYYY-MM-DD"], invalid.ItemErrors);
        Assert.True(invalid.NothingPulled);

        File.WriteAllText(csv.Path, string.Join(",", CatalystCsv.Header) + "\n"
            + "OPEC,2026-12-03,,,,,ONOMM-TEST,,https://opec.example/a,2026-09-01T12:00:00Z,unit test,\n"
            + "CPI,2026-10-14,08:30,,,,CPI:2026-09,,https://bls.example/cpi,2026-09-01T12:00:00Z,unit test,\n");

        var otherFamily = await Opec(csv.Path).PullAsync(Token);

        Assert.Equal(["opec.csv: rows of family CPI belong in import-catalysts, not the OPEC calendar"], otherFamily.ItemErrors);
        Assert.True(otherFamily.NothingPulled);
        Assert.Equal(0, await HeadAsync());
    }

    [Fact]
    public async Task The_OPEC_adapter_is_disabled_without_its_file_and_the_shipped_file_is_a_valid_header()
    {
        Assert.Equal("Sources:OpecCalendar:File must name the curated OPEC CSV", Opec("").DisabledReason);
        Assert.Equal(
            "Sources:OpecCalendar:File names a file that does not exist",
            Opec(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "opec.csv")).DisabledReason);

        var adapter = Opec(ShippedOpecFile());
        Assert.Null(adapter.DisabledReason);

        var result = await adapter.PullAsync(Token);

        Assert.Empty(result.ItemErrors);
        Assert.Equal("file read; 0 new, 0 rescheduled, 0 unchanged.", result.Note);
        Assert.Equal(0, await HeadAsync());
    }

    private protected override void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient(CalendarAdapter.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _pages);

    /// <summary>The 2013 historical page with its headings moved to <paramref name="year"/> (a stand-in for that year's page).</summary>
    private static byte[] History(int year) =>
        Encoding.UTF8.GetBytes(CalendarFixtures.Text("fed/fomchistorical2013-excerpt.htm").Replace(" - 2013</h5>", $" - {year}</h5>", StringComparison.Ordinal));

    private void ServeBlsListings()
    {
        _pages.Serve("/schedule/news_release/cpi.htm", CalendarFixtures.Bytes("bls/cpi-excerpt.htm"));
        _pages.Serve("/schedule/news_release/empsit.htm", CalendarFixtures.Bytes("bls/empsit-excerpt.htm"));
    }

    private FedCalendarAdapter Fed(Action<FedCalendarOptions>? configure = null)
    {
        var options = new FedCalendarOptions { Enabled = true, BaseUrl = FedBase, RetryDelaySeconds = 0, HistoryFromYear = 0 };
        configure?.Invoke(options);
        return ActivatorUtilities.CreateInstance<FedCalendarAdapter>(Services, Options.Create(options));
    }

    private BlsCalendarAdapter Bls(Action<BlsCalendarOptions>? configure = null)
    {
        var options = new BlsCalendarOptions { Enabled = true, BaseUrl = BlsBase, RetryDelaySeconds = 0, UserAgent = ContactAgent, HistoryFromYear = 0 };
        configure?.Invoke(options);
        return ActivatorUtilities.CreateInstance<BlsCalendarAdapter>(Services, Options.Create(options));
    }

    private TAdapter Adapter<TAdapter, TOptions>(TOptions options)
        where TOptions : CalendarOptions
    {
        options.Enabled = true;
        options.RetryDelaySeconds = 0;
        return ActivatorUtilities.CreateInstance<TAdapter>(Services, Options.Create(options));
    }

    private OpecCalendarAdapter Opec(string file) =>
        ActivatorUtilities.CreateInstance<OpecCalendarAdapter>(Services, Options.Create(new OpecCalendarOptions { Enabled = true, File = file }));

    /// <summary><c>apps/engine/data/catalysts/opec.csv</c>, found above the test output.</summary>
    private static string ShippedOpecFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PrimeScore.Engine.sln")))
            {
                return Path.Combine(directory.FullName, "data", "catalysts", "opec.csv");
            }
        }

        throw new InvalidOperationException("PrimeScore.Engine.sln not found above the test output.");
    }

    private sealed record Request(string Path, string? UserAgent);

    /// <summary>A CSV named <c>opec.csv</c> in a directory of its own, deleted afterwards.</summary>
    private sealed class TempCsv : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "primescore-catalysts-tests", Guid.NewGuid().ToString("N"));

        public TempCsv(string text)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path, text);
        }

        public string Path => System.IO.Path.Combine(_directory, "opec.csv");

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Serves pages from memory by path; 404 for unknown paths; 503 for a page's configured failures.</summary>
    private sealed class StubPages : HttpMessageHandler
    {
        private readonly Dictionary<string, (byte[] Body, int Failures)> _pages = new(StringComparer.Ordinal);

        public HashSet<string> Timeouts { get; } = [];

        public List<Request> Requests { get; } = [];

        public void Serve(string path, byte[] body, int failures = 0) => _pages[path] = (body, failures);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new Request(path, request.Headers.TryGetValues("User-Agent", out var agents) ? string.Join(" ", agents) : null));
            if (Timeouts.Contains(path))
            {
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
            }

            if (!_pages.TryGetValue(path, out var page))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>not found</html>") });
            }

            if (page.Failures > 0)
            {
                _pages[path] = (page.Body, page.Failures - 1);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("<html>busy</html>") });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(page.Body) });
        }
    }
}
