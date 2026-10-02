using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>Idempotent recording and point-in-time reads against a real engine database.</summary>
public sealed class RecordingTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-ingestion-tests", Guid.NewGuid().ToString("N"));
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
            .AddSingleton<IClock>(new TestClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration);
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
    public async Task A_source_prefix_narrows_a_series_before_the_earliest_recorded_row_of_a_date_is_kept()
    {
        // fred:VIX is recorded first for 1 September, cboe:VIX second for the same New York date.
        var recorder = _services.GetRequiredService<SignalRecorder>();
        await recorder.RecordAsync([Candidate(18.0, day: 1, source: "fred:VIXCLS", provider: "FRED")], Token);
        await recorder.RecordAsync([Candidate(18.5, day: 1, source: "cboe:VIX", provider: "Cboe")], Token);

        var every = await Query<GetObservationSeries, ObservationSeries>(Series(before: At(day: 3)));
        var cboe = await Query<GetObservationSeries, ObservationSeries>(Series(before: At(day: 3)) with { SourcePrefix = "cboe:" });

        Assert.Equal([18.0], every.Points.Select(point => point.Value));
        Assert.Equal([18.5], cboe.Points.Select(point => point.Value));
    }

    [Fact]
    public async Task The_tape_filter_matches_instruments_in_any_letter_case()
    {
        await _services.GetRequiredService<SignalRecorder>().RecordAsync([Candidate(18.0)], Token);

        var page = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter(Instrument: "vix")));

        Assert.Equal("VIX", Assert.Single(page.Signals).Instrument);
    }

    [Fact]
    public async Task The_tape_filters_by_source_prefix_and_by_provider_in_any_letter_case()
    {
        await _services.GetRequiredService<SignalRecorder>().RecordAsync(
            [Candidate(18.0, day: 1), Candidate(19.0, day: 2, source: "cboe:VIX", provider: "Cboe")], Token);

        var cboe = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter(SourcePrefix: "cboe:")));
        var byProvider = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter(Provider: "CBOE")));
        var api = await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter(Provider: "api")));

        Assert.Equal("cboe:VIX", Assert.Single(cboe.Signals).SourceIdentifier);
        Assert.Equal(19.0, Assert.Single(byProvider.Signals).Value);
        Assert.Equal("cboe_vix", Assert.Single(api.Signals).SourceIdentifier);
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
        DateTimeOffset? at = null,
        string provider = "api") => new(
        category, source, "VIX", variant, at ?? At(day), "STRUCTURED", value,
        JsonDocument.Parse($$"""{"instrument":"VIX","value":{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""").RootElement.Clone(),
        new SignalProvenance(provider));
}
