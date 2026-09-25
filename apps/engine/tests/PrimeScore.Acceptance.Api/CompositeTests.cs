using System.Diagnostics;
using System.Net.Http.Json;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;
using static PrimeScore.Acceptance.Api.Harness.SignalDocuments;

namespace PrimeScore.Acceptance.Api;

/// <summary>
/// SRS CLS-002 and CLS-006 with expected values worked out by hand from the sourced anchor
/// fixtures and the ADR-0004 formulas (default configuration, equity context), plus NFR-001
/// and NFR-003.
/// </summary>
public sealed class CompositeTests(EngineFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CLS_002_and_CLS_006_Volmageddon_composite_is_0_9992_and_its_dislocation_18_645072_breaches_1_5()
    {
        // The 2018-02-05 close (37.32) ranks 1259/1260 against its window: severity +0.9992,
        // certainty 1.0 (full window, previous close one business day earlier).
        // The 2018-02-02 close (17.31) is in the two-trading-day window with severity +0.8253: they
        // confirm each other and s_MD = max⁺ − max⁻ = 0.9992 − 0.
        // Only MARKET_DATA is present: composite = 0.3 × 1 × 0.9992 / 0.3 = 0.9992.
        // 1259 of the 1260 prior VIX levels are ≤ 37.32: percentile 0.9992 > 0.70, high regime, k = 0.5.
        // SignalImpliedIV = 37.32 × (1 + 0.9992 × 0.5) = 37.32 × 1.4996 = 55.965072; dislocation 18.645072 ≥ 1.5.
        await using var engine = await RunningEngine.StartAsync();
        var eventAt = new DateTimeOffset(2018, 2, 5, 21, 15, 0, TimeSpan.Zero);
        await AnchorHistory.LoadAsync(engine, "market_data_vix_volmageddon_2018_02_05.json", new DateOnly(2018, 2, 2), 37.32, eventAt, Token);
        var client = engine.Client(Role.Read);

        var composite = await client.GetCompositeScoreAsync(null, "equity", Token);
        var dislocation = await client.GetDislocationAsync(null, "equity", Token);
        var dayBefore = await client.GetCompositeScoreAsync(eventAt.AddDays(-3), "equity", Token);

        Assert.Equal(0.9992, composite.Score, 12);
        Assert.Equal(eventAt, composite.As_of);
        Assert.Equal("srs_default", composite.Weighting_scheme_id);
        Assert.Equal([SourceCategory.MARKET_DATA], composite.Contributing_sources);
        Assert.Equal([SourceCategory.MACROECONOMIC, SourceCategory.CROSS_ASSET_FLOW], composite.Absent_sources.Order());
        Assert.Equal(0.9992, Assert.Single(composite.Component_scores).Weighted_contribution, 12);
        Assert.Equal(("VIX", 37.32, (double?)0.5), (dislocation.Reference_instrument, dislocation.Market_observed_iv, dislocation.Sensitivity_factor));
        Assert.Equal(55.965072, dislocation.Signal_implied_iv, 9);
        Assert.Equal(18.645072, dislocation.Dislocation_value, 9);
        Assert.Equal(1.5, dislocation.Dislocation_threshold);
        Assert.True(dislocation.Threshold_breached);

        // SIG-004: as of the previous close the event is not part of anything. 2018-02-02 (17.31) ranks
        // 1039/1259 of its window: +0.8253 at certainty 1259/1260 = 0.9992 (+0.82463976); 2018-02-01 (13.47)
        // ranks 52/1258: −0.0413 at 0.9984 (−0.04123392). s_MD = 0.82463976 − 0.04123392 = 0.78340584.
        // 1082 of the 1259 levels before 17.31 are ≤ it: percentile 0.859 > 0.70, k = 0.5;
        // dislocation 17.31 × 0.78340584 × 0.5 = 6.7803775452.
        var dislocationBefore = await client.GetDislocationAsync(eventAt.AddDays(-3), "equity", Token);
        Assert.Equal(eventAt.AddDays(-3), dayBefore.As_of);
        Assert.Equal(0.78340584, dayBefore.Score, 9);
        Assert.Equal((17.31, (double?)0.5), (dislocationBefore.Market_observed_iv, dislocationBefore.Sensitivity_factor));
        Assert.Equal(6.7803775452, dislocationBefore.Dislocation_value, 9);
    }

    [Fact]
    public async Task CLS_002_and_CLS_006_low_vol_composite_is_minus_0_8849_and_its_dislocation_minus_8_132231_breaches_by_magnitude()
    {
        // The 2017-10-05 close (9.19) is below the window median (13.89) and ranks 1115/1260:
        // severity −0.8849, certainty 1.0. The 2017-10-04 close (9.63) ranks −0.8610 and confirms it:
        // s_MD = 0 − 0.8849 = −0.8849; composite = −0.8849.
        // No prior level is ≤ 9.19: percentile 0 < 0.30, low regime, k = 1.0.
        // SignalImpliedIV = 9.19 × (1 − 0.8849) = 9.19 × 0.1151 = 1.057769; dislocation −8.132231,
        // whose magnitude breaches 1.5 (ADR-0004 §6: a vol-compression dislocation can breach).
        await using var engine = await RunningEngine.StartAsync();
        var eventAt = new DateTimeOffset(2017, 10, 5, 20, 15, 0, TimeSpan.Zero);
        await AnchorHistory.LoadAsync(engine, "market_data_vix_low_vol_regime_2017_10_05.json", new DateOnly(2017, 10, 4), 9.19, eventAt, Token);
        var client = engine.Client(Role.Read);

        var composite = await client.GetCompositeScoreAsync(null, null, Token);
        var dislocation = await client.GetDislocationAsync(null, null, Token);

        Assert.Equal(-0.8849, composite.Score, 12);
        Assert.Equal(eventAt, composite.As_of);
        Assert.Equal(("VIX", 9.19, (double?)1.0), (dislocation.Reference_instrument, dislocation.Market_observed_iv, dislocation.Sensitivity_factor));
        Assert.Equal(1.057769, dislocation.Signal_implied_iv, 9);
        Assert.Equal(-8.132231, dislocation.Dislocation_value, 9);
        Assert.True(dislocation.Threshold_breached);
    }

    [Fact]
    public async Task NFR_003_a_new_weighting_scheme_and_threshold_apply_from_the_next_composite_without_a_restart()
    {
        // Volmageddon as above: 0.9992 and a dislocation of 18.645072 that breaches 1.5. Then WEIGHTED_MEAN and a
        // threshold of 30, and the next close, 2018-02-06 (VIXCLS 29.98). Its window is the 1260 levels before it
        // (the fixture's last 1259 and 37.32): median 13.6, |29.98 − 13.6| ranks 1255/1260, severity +0.996,
        // certainty 1.0. The window holds 2018-02-05 and 2018-02-06, both confirmed:
        // mean (0.9992 + 0.996) / 2 = 0.9976 (net max would give 0.9992).
        // 1255 of those 1260 levels are ≤ 29.98: percentile 0.996, k = 0.5.
        // SignalImpliedIV = 29.98 × (1 + 0.9976 × 0.5) = 44.934024; dislocation 14.954024 < 30.
        await using var engine = await RunningEngine.StartAsync();
        var eventAt = new DateTimeOffset(2018, 2, 5, 21, 15, 0, TimeSpan.Zero);
        await AnchorHistory.LoadAsync(engine, "market_data_vix_volmageddon_2018_02_05.json", new DateOnly(2018, 2, 2), 37.32, eventAt, Token);
        var admin = engine.Client(Role.Admin);

        await admin.SetWeightingSchemeAsync(new WeightingScheme
        {
            Scheme_id = "equal_weight",
            Category_weights = new() { ["MARKET_DATA"] = 1, ["MACROECONOMIC"] = 1, ["GEOPOLITICAL"] = 1, ["CROSS_ASSET_FLOW"] = 1 },
            Aggregation = WeightingSchemeAggregation.WEIGHTED_MEAN,
        }, Token);
        await admin.SetDislocationThresholdAsync(new Body { Threshold = 30, Reference_instrument = "VIX" }, Token);
        await AnchorHistory.PostAsync(engine, Batch(MarketData("VIX", 29.98, eventAt.AddDays(1), source: "fixture:VIXCLS")), Token);
        var read = engine.Client(Role.Read);

        var before = await read.GetCompositeScoreAsync(eventAt, "equity", Token);
        var dislocationBefore = await read.GetDislocationAsync(eventAt, "equity", Token);
        var after = await read.GetCompositeScoreAsync(null, "equity", Token);
        var dislocationAfter = await read.GetDislocationAsync(null, "equity", Token);

        Assert.Equal(("srs_default", 0.9992), (before.Weighting_scheme_id, Math.Round(before.Score, 12)));
        Assert.Equal((1.5, true), (dislocationBefore.Dislocation_threshold, dislocationBefore.Threshold_breached));
        Assert.Equal(("equal_weight", eventAt.AddDays(1)), (after.Weighting_scheme_id, after.As_of));
        Assert.Equal(0.9976, after.Score, 12);
        Assert.Equal(44.934024, dislocationAfter.Signal_implied_iv, 9);
        Assert.Equal(14.954024, dislocationAfter.Dislocation_value, 9);
        Assert.Equal((30.0, false), (dislocationAfter.Dislocation_threshold, dislocationAfter.Threshold_breached));
    }

    [Theory]
    [InlineData("""{"scheme_id":"desk","category_weights":{"MARKET_DATA":1,"MACROECONOMIC":1,"CROSS_ASSET_FLOW":1}}""", "aggregation")]
    [InlineData("""{"scheme_id":"desk","category_weights":{"MARKET_DATA":1,"MACROECONOMIC":1,"CROSS_ASSET_FLOW":1},"aggregation":7}""", "aggregation")]
    public async Task A_weighting_scheme_without_a_valid_aggregation_is_a_400(string body, string field)
    {
        using var http = fixture.Engine.Http(Role.Admin);

        using var response = await http.PutAsync(new Uri("config/weighting-scheme", UriKind.Relative),
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Token);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ApiJsonOptions.Value, Token);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(error!.Details!, detail => detail.Contains(field, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_change_the_formulas_cannot_use_is_a_400_naming_the_problem_and_nothing_changes()
    {
        await using var engine = await RunningEngine.StartAsync();
        var admin = engine.Client(Role.Admin);

        // The SRS example map: 1.5 would let 1 + composite × k go negative (ADR-0004 §6).
        var refused = await Assert.ThrowsAnyAsync<PrimeScoreApiException>(() => admin.SetDislocationThresholdAsync(new Body
        {
            Threshold = 1.5,
            Sensitivity_factor_map = new() { ["low_vol"] = 1.5, ["normal"] = 1.0, ["high_vol"] = 0.6 },
        }, Token));
        var unknownCategory = await Assert.ThrowsAsync<PrimeScoreApiException<ErrorResponse>>(() => admin.SetWeightingSchemeAsync(new WeightingScheme
        {
            Scheme_id = "typo",
            Category_weights = new() { ["MARKET_DATA"] = 1, ["MACROECONOMIC"] = 1, ["CROSS_ASSET_FLOWS"] = 1 },
            Aggregation = WeightingSchemeAggregation.MAX_CONFIRMED_WEIGHTED,
        }, Token));

        Assert.Equal(400, refused.StatusCode);
        Assert.Contains("outside (0, 1]", refused.Response, StringComparison.Ordinal);
        Assert.Equal(400, unknownCategory.StatusCode);
        Assert.Contains(unknownCategory.Result.Details!, detail => detail.Contains("'CROSS_ASSET_FLOWS' is not a source category", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_context_is_a_404()
    {
        var client = fixture.Engine.Client(Role.Read);

        var composite = await Assert.ThrowsAnyAsync<PrimeScoreApiException>(() => client.GetCompositeScoreAsync(null, "gold", Token));
        var dislocation = await Assert.ThrowsAnyAsync<PrimeScoreApiException>(() => client.GetDislocationAsync(null, "gold", Token));

        Assert.Equal((404, 404), (composite.StatusCode, dislocation.StatusCode));
        Assert.Contains("Unknown context 'gold'", composite.Response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NFR_001_the_composite_reflects_a_new_signal_well_within_120_seconds()
    {
        // A time no other test uses; VXN is a member of the equity context.
        var at = new DateTimeOffset(2014, 7, 15, 20, 15, 0, TimeSpan.Zero);
        var watch = Stopwatch.StartNew();

        await AnchorHistory.PostAsync(fixture.Engine, Batch(MarketData("VXN", 16.0, at)), Token);
        var composite = await fixture.Engine.Client(Role.Read).GetCompositeScoreAsync(at, "equity", Token);
        watch.Stop();

        Assert.Equal(at, composite.As_of);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(120));
    }
}
