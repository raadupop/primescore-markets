using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Sources;
using PrimeScore.Modules.Ingestion.Sources.Fred;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>Timestamps and derivations worked out by hand; values are synthetic arithmetic inputs, not market data.</summary>
public sealed class FredCandidateTests
{
    private static readonly DateTimeOffset Retrieved = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly FredSeries Vix = new("VIXCLS", "VIX", SourceCategory.MarketData, FredTiming.DailyClose, true, false, "equity_index", "points");
    private static readonly FredSeries Cpi = new("CPIAUCSL", "CPI_YOY", SourceCategory.Macroeconomic, FredTiming.InitialRelease, true, true, "INFLATION", "percent_yoy");
    private static readonly FredSeries Claims = new("ICSA", "INITIAL_CLAIMS", SourceCategory.Macroeconomic, FredTiming.InitialRelease, true, false, "EMPLOYMENT", "claims");
    private static readonly FredSeries Sp500 = new("SP500", "SP500", SourceCategory.CrossAssetFlow, FredTiming.BasketObservation, true, false, "SP500", "index");

    [Fact]
    public void Winter_close_on_2018_02_05_is_stamped_16_15_EST_which_is_21_15_UTC()
    {
        var candidate = Single(Vix, new FredObservation(new DateOnly(2018, 2, 5), new DateOnly(2026, 9, 25), 37.32));

        Assert.Equal(new DateTimeOffset(2018, 2, 5, 21, 15, 0, TimeSpan.Zero), candidate.ObservedAt);
        Assert.Equal(37.32, candidate.Value);
        Assert.Equal("fred:VIXCLS", candidate.SourceIdentifier);
        Assert.Null(candidate.Provenance.FirstReleased);
    }

    [Fact]
    public void Summer_close_on_2026_09_22_is_stamped_16_15_EDT_which_is_20_15_UTC()
    {
        var candidate = Single(Vix, new FredObservation(new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 23), 14.21));

        Assert.Equal(new DateTimeOffset(2026, 9, 22, 20, 15, 0, TimeSpan.Zero), candidate.ObservedAt);
    }

    [Fact]
    public void Missing_values_reported_as_dot_are_skipped_and_counted_never_filled()
    {
        var (candidates, missing) = FredPuller.Candidates(Vix,
        [
            new FredObservation(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 23), 14.87),
            new FredObservation(new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 23), null),
        ], Retrieved, DateOnly.MinValue);

        Assert.Single(candidates);
        Assert.Equal(1, missing);
    }

    [Fact]
    public void CPI_YoY_of_109_1_over_base_100_is_9_1000_released_08_30_EDT_on_the_first_release_date()
    {
        var (candidates, missing) = FredPuller.Candidates(Cpi,
        [
            new FredObservation(new DateOnly(2021, 5, 1), new DateOnly(2021, 6, 10), 100.0),
            new FredObservation(new DateOnly(2021, 6, 1), new DateOnly(2021, 7, 13), 100.0),
            new FredObservation(new DateOnly(2022, 5, 1), new DateOnly(2022, 6, 10), 108.6),
            new FredObservation(new DateOnly(2022, 6, 1), new DateOnly(2022, 7, 13), 109.1),
        ], Retrieved, DateOnly.MinValue);

        // 2021 months have no year-earlier base in the response: counted missing, not invented.
        Assert.Equal(2, missing);
        var june = candidates.Cast<SignalCandidate>().Single(candidate => candidate.ObservedAt.Month == 7);
        Assert.Equal(9.1, june.Value!.Value, precision: 10);
        Assert.Equal(new DateTimeOffset(2022, 7, 13, 12, 30, 0, TimeSpan.Zero), june.ObservedAt);
        Assert.Equal(new DateOnly(2022, 7, 13), june.Provenance.FirstReleased);
        Assert.Equal(8.6, june.Payload.GetProperty("prior_value").GetDouble(), precision: 10);
        Assert.Equal("2022-06-01", june.Payload.GetProperty("reference_period").GetString());
        Assert.Contains("Seasonally adjusted", june.Provenance.Derivation, StringComparison.Ordinal);
    }

    [Fact]
    public void Observations_fetched_only_as_year_over_year_bases_are_neither_emitted_nor_counted_missing()
    {
        var (candidates, missing) = FredPuller.Candidates(Cpi,
        [
            new FredObservation(new DateOnly(2021, 5, 1), new DateOnly(2021, 6, 10), 100.0),
            new FredObservation(new DateOnly(2021, 6, 1), new DateOnly(2021, 7, 13), 100.0),
            new FredObservation(new DateOnly(2022, 5, 1), new DateOnly(2022, 6, 10), 108.6),
            new FredObservation(new DateOnly(2022, 6, 1), new DateOnly(2022, 7, 13), 109.1),
        ], Retrieved, emitFrom: new DateOnly(2022, 5, 1));

        Assert.Equal(0, missing);
        Assert.Equal([8.6, 9.1], candidates.Cast<SignalCandidate>().Select(candidate => Math.Round(candidate.Value!.Value, 4)));
    }

    [Fact]
    public void Reference_periods_first_released_on_the_same_date_are_distinct_signals()
    {
        // A series' first vintage publishes its history at once: both weeks share one release date.
        var (candidates, _) = FredPuller.Candidates(Claims,
        [
            new FredObservation(new DateOnly(2009, 5, 2), new DateOnly(2009, 5, 7), 601000),
            new FredObservation(new DateOnly(2009, 5, 9), new DateOnly(2009, 5, 7), 637000),
        ], Retrieved, DateOnly.MinValue);

        var signals = candidates.Cast<SignalCandidate>().ToArray();
        Assert.Equal(signals[0].ObservedAt, signals[1].ObservedAt);
        Assert.Equal(["EMPLOYMENT@2009-05-02", "EMPLOYMENT@2009-05-09"], signals.Select(signal => signal.Variant));
    }

    [Fact]
    public void Implied_volatility_and_basket_series_have_one_variant_each()
    {
        Assert.Equal("IMPLIED_VOLATILITY", Single(Vix, new FredObservation(new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 23), 14.21)).Variant);
        Assert.Equal("basket_observation", Single(Sp500, new FredObservation(new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 23), 6612.4)).Variant);
    }

    [Fact]
    public void Initial_claims_use_the_first_released_value_and_the_previous_week_as_prior()
    {
        var (candidates, _) = FredPuller.Candidates(Claims,
        [
            new FredObservation(new DateOnly(2026, 9, 12), new DateOnly(2026, 9, 17), 196000),
            new FredObservation(new DateOnly(2026, 9, 19), new DateOnly(2026, 9, 24), 197000),
        ], Retrieved, DateOnly.MinValue);

        var latest = candidates.Cast<SignalCandidate>().Last();
        Assert.Equal(197000, latest.Value);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), latest.ObservedAt);
        Assert.Equal(196000, latest.Payload.GetProperty("prior_value").GetDouble());
    }

    [Fact]
    public void Basket_prices_are_cross_asset_observations_stamped_16_00_New_York_with_a_publication_caveat()
    {
        var candidate = Single(Sp500, new FredObservation(new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 23), 6612.4));

        Assert.Equal(SourceCategory.CrossAssetFlow, candidate.Category);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 20, 0, 0, TimeSpan.Zero), candidate.ObservedAt);
        Assert.Equal("basket_observation", candidate.Payload.GetProperty("kind").GetString());
        Assert.Contains("publication may be later", candidate.Provenance.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void FRED_missing_markers_parse_to_null_and_an_error_body_is_reported()
    {
        var observations = FredClient.Parse("""
            {"observations":[{"realtime_start":"2026-09-23","realtime_end":"2026-09-23","date":"2026-09-21","value":"14.87"},
                             {"realtime_start":"2026-09-23","realtime_end":"2026-09-23","date":"2026-09-22","value":"."}]}
            """, "VIXCLS");

        Assert.Equal([14.87, null], observations.Select(observation => observation.Value));
        var exception = Assert.Throws<InvalidOperationException>(() => FredClient.Parse("""{"error_code":400,"error_message":"Bad Request.  The series does not exist."}""", "NOPE"));
        Assert.Contains("series does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-09-25T10:00:00Z", "2026-09-25T13:30:00Z")]
    [InlineData("2026-09-25T13:30:00Z", "2026-09-26T13:30:00Z")]
    [InlineData("2026-09-25T20:00:00Z", "2026-09-26T13:30:00Z")]
    public void The_daily_run_at_13_30_UTC_is_today_until_it_has_passed(string now, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            SourceScheduler.NextRun(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), new TimeOnly(13, 30)));

    private static SignalCandidate Single(FredSeries series, FredObservation observation) =>
        Assert.IsType<SignalCandidate>(Assert.Single(FredPuller.Candidates(series, [observation], Retrieved, DateOnly.MinValue).Candidates));
}
