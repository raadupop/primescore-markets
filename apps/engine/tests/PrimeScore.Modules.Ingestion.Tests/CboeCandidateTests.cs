using PrimeScore.Modules.Ingestion.Sources.Cboe;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>How a Cboe close becomes a signal: New York close stamps, variants, typed provenance and the reconstructed marker.</summary>
public sealed class CboeCandidateTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 9, 28, 23, 5, 0, TimeSpan.Zero);

    private static readonly Uri Url = new("https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv");

    [Theory]
    // Volatility indices 16:15 New York: EDT (UTC-4) 16:15 + 4 h = 20:15Z; EST (UTC-5) 16:15 + 5 h = 21:15Z.
    // SPX 16:00 New York: EDT 16:00 + 4 h = 20:00Z; EST 16:00 + 5 h = 21:00Z.
    // 1976-04-20 is before that year's changeover (last Sunday of April, 25 April): EST.
    [InlineData("VIX", 2026, 9, 24, "2026-09-24T20:15:00Z")]
    [InlineData("VIX", 2026, 1, 15, "2026-01-15T21:15:00Z")]
    [InlineData("SPX", 2026, 9, 24, "2026-09-24T20:00:00Z")]
    [InlineData("SPX", 2026, 1, 15, "2026-01-15T21:00:00Z")]
    [InlineData("SPX", 1976, 4, 20, "1976-04-20T21:00:00Z")]
    [InlineData("SPX", 1975, 3, 3, "1975-03-03T20:00:00Z")]
    public void A_close_is_stamped_at_its_index_close_New_York_on_its_data_date_not_at_fetch_time(string symbol, int year, int month, int day, string expected)
    {
        var candidate = Candidate(symbol, new DateOnly(year, month, day));

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), candidate.ObservedAt);
        Assert.Equal(RetrievedAt, candidate.Provenance.RetrievedAt);
        Assert.NotEqual(candidate.Provenance.RetrievedAt, candidate.ObservedAt);
    }

    [Fact]
    public void Short_and_long_tenors_carry_the_tenor_in_the_variant_and_the_payload()
    {
        var candidate = Candidate("VIX9D", new DateOnly(2026, 9, 24));

        Assert.Equal((SourceCategory.MarketData, "cboe:VIX9D", "VIX9D", "IMPLIED_VOLATILITY:9D"),
            (candidate.Category, candidate.SourceIdentifier, candidate.Instrument, candidate.Variant));
        Assert.Equal("9D", candidate.Payload.GetProperty("tenor").GetString());
        Assert.Equal("IMPLIED_VOLATILITY", candidate.Payload.GetProperty("metric_type").GetString());
        Assert.Equal("2026-09-24T20:15:00Z", candidate.Payload.GetProperty("observed_at").GetString());
        Assert.Equal("IMPLIED_VOLATILITY", Candidate("VIX", new DateOnly(2026, 9, 24)).Variant);
        Assert.False(Candidate("VIX", new DateOnly(2026, 9, 24)).Payload.TryGetProperty("tenor", out _));
    }

    [Fact]
    public void SPX_follows_the_cross_asset_basket_convention()
    {
        var candidate = Candidate("SPX", new DateOnly(2026, 9, 24));

        Assert.Equal((SourceCategory.CrossAssetFlow, "basket_observation"), (candidate.Category, candidate.Variant));
        Assert.Equal("basket_observation", candidate.Payload.GetProperty("kind").GetString());
        Assert.Equal("index", candidate.Payload.GetProperty("unit").GetString());
        Assert.Equal("2026-09-24", candidate.Payload.GetProperty("observation_date").GetString());
    }

    [Theory]
    [InlineData("VIX9D", 2013, 9, 30, true)]
    [InlineData("VIX9D", 2013, 10, 1, false)]
    [InlineData("VIX6M", 2013, 11, 26, true)]
    [InlineData("VIX6M", 2013, 11, 27, false)]
    [InlineData("VIX", 2002, 12, 31, true)]
    [InlineData("VIX", 2004, 1, 2, false)]
    [InlineData("VIX3M", 2009, 9, 18, false)]
    [InlineData("SPX", 1975, 1, 2, false)]
    public void Rows_before_a_verified_live_start_are_marked_reconstructed(string symbol, int year, int month, int day, bool expected)
    {
        var candidate = Candidate(symbol, new DateOnly(year, month, day));

        Assert.Equal(expected, candidate.Provenance.Reconstructed);
        Assert.Equal(expected, candidate.Provenance.Note!.Contains("back-calculated", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("VVIX", 2020, 3, 16)]
    [InlineData("VIX", 2003, 6, 2)]
    public void Without_a_verified_live_start_the_marker_is_left_unknown_and_says_so(string symbol, int year, int month, int day)
    {
        var candidate = Candidate(symbol, new DateOnly(year, month, day));

        Assert.Null(candidate.Provenance.Reconstructed);
        Assert.Contains("live start unverified", candidate.Provenance.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Provenance_names_Cboe_the_file_and_its_hash_and_no_derivation()
    {
        var candidate = Candidate("VIX", new DateOnly(2026, 9, 24));

        Assert.Equal("Cboe", candidate.Provenance.Provider);
        Assert.Equal("VIX", candidate.Provenance.SeriesId);
        Assert.Equal(Url.ToString(), candidate.Provenance.Url);
        Assert.Equal(new string('a', 64), candidate.Provenance.FileSha256);
        Assert.Null(candidate.Provenance.Derivation);
        Assert.Equal(21.45, candidate.Value);
    }

    private static Recording.SignalCandidate Candidate(string symbol, DateOnly date) =>
        CboeIndexAdapter.Candidate(CboeIndexCatalog.Find(symbol)!, new CboeClose(date, 21.45), Url, RetrievedAt, new string('a', 64));
}
