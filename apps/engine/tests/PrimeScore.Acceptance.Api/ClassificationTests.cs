using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;
using static PrimeScore.Acceptance.Api.Harness.SignalDocuments;

namespace PrimeScore.Acceptance.Api;

/// <summary>SRS SIG-004 (point in time), CLS-001 (via the classifier), CLS-004 (fallback), CLS-009 (unknown indicator).</summary>
public sealed class ClassificationTests(EngineFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SIG_004_assessments_as_of_a_time_exclude_signals_observed_after_it()
    {
        var t1 = new DateTimeOffset(2019, 3, 4, 21, 15, 0, TimeSpan.Zero);
        var response = await PostAsync(fixture.Engine, Batch(
            MarketData("RVX", 15.0, t1),
            MarketData("RVX", 16.0, t1.AddDays(1)),
            MarketData("RVX", 17.0, t1.AddDays(2))));
        var ids = response.Signals.Select(signal => signal.Signal_id).ToArray();

        var asOfSecond = await fixture.Engine.Client(Role.Read).GetAssessmentsAsync(null, t1.AddDays(1), Token);
        var returned = asOfSecond.Select(assessment => assessment.Signal_id).ToHashSet();

        Assert.Contains(ids[0], returned);
        Assert.Contains(ids[1], returned);
        Assert.DoesNotContain(ids[2], returned);
    }

    [Fact]
    public async Task SIG_004_an_as_of_without_an_offset_is_UTC_on_every_host_and_an_unreadable_one_is_a_400()
    {
        var t1 = new DateTimeOffset(2019, 4, 1, 20, 15, 0, TimeSpan.Zero);
        var response = await PostAsync(fixture.Engine, Batch(MarketData("GVZ", 14.0, t1), MarketData("GVZ", 14.5, t1.AddDays(1))));
        var second = response.Signals.Last().Signal_id;
        using var http = fixture.Engine.Http(Role.Read);

        // The second close is observed at 2019-04-02T20:15:00Z: included at that cut however it is
        // written (a '+' sent unescaped arrives as a space), excluded one second earlier.
        string[] atTheCut =
        [
            "2019-04-02T20:15:00", "2019-04-02T20:15:00Z", "2019-04-02T20:15:00%2B00:00", "2019-04-02T20:15:00+00:00",
            "2019-04-03T01:15:00%2B05:00", "2019-04-03T01:15:00+05:00", "2019-04-02T16:15:00-04:00",
        ];
        string[] justBefore = ["2019-04-02T20:14:59", "2019-04-03T01:14:59+05:00", "2019-04-02T16:14:59-04:00"];
        foreach (var cut in atTheCut)
        {
            Assert.True((await AsOfAsync(http, cut)).Any(assessment => assessment.Signal_id == second), $"as_of={cut} should include the close");
        }

        foreach (var cut in justBefore)
        {
            Assert.False((await AsOfAsync(http, cut)).Any(assessment => assessment.Signal_id == second), $"as_of={cut} should exclude the close");
        }

        using var unreadable = await http.GetAsync(new Uri("classification/assessments?as_of=yesterday", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.BadRequest, unreadable.StatusCode);
        var error = await unreadable.Content.ReadFromJsonAsync<ErrorResponse>(Token);
        Assert.Contains(error!.Details!, detail => detail.StartsWith("as_of:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CLS_001_Volmageddon_scores_within_the_anchor_band_when_its_sourced_history_arrives_through_the_API()
    {
        var anchor = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryPaths.ClassifierDirectory, "tests", "acceptance", "fixtures", "market_data_vix_volmageddon_2018_02_05.json"), Token))!;
        var band = anchor["expected_band"]!;
        await using var engine = await RunningEngine.StartAsync();

        var response = await AnchorHistory.LoadAsync(engine, "market_data_vix_volmageddon_2018_02_05.json",
            new DateOnly(2018, 2, 2), 37.32, new DateTimeOffset(2018, 2, 5, 21, 15, 0, TimeSpan.Zero), Token);

        var assessment = Assert.Single(await engine.Client(Role.Read).GetAssessmentsAsync(response.Signals.Last().Signal_id, null, Token));
        var expected = band["expected_score_signed"]!.GetValue<double>();
        var tolerance = band["score_tolerance"]!.GetValue<double>();
        Assert.InRange(assessment.Score, expected - tolerance, expected + tolerance);
        Assert.Equal(1.0, assessment.History_sufficiency);
        Assert.Equal(ClassificationMethod.RULE_BASED, assessment.Classification_method);
        Assert.False(assessment.Is_fallback ?? false);
    }

    [Fact]
    public async Task CLS_009_an_unregistered_instrument_is_assessed_with_zero_score_and_zero_certainty()
    {
        // Control: a registered instrument with no earlier history also scores 0 with certainty 0
        // (insufficient history), but its temporal relevance is 1; the unknown indicator's is 0.
        var at = new DateTimeOffset(1996, 6, 3, 21, 15, 0, TimeSpan.Zero);
        var response = await PostAsync(fixture.Engine, Batch(MarketData("NOT_REGISTERED", 42.0, at), MarketData("OVX", 30.0, at)));
        var client = fixture.Engine.Client(Role.Read);

        var unknown = Assert.Single(await client.GetAssessmentsAsync(response.Signals.First().Signal_id, null, Token));
        var firstObservation = Assert.Single(await client.GetAssessmentsAsync(response.Signals.Last().Signal_id, null, Token));

        Assert.Equal((0.0, 0.0, 0.0), (unknown.Score, unknown.Certainty, unknown.Temporal_relevance));
        Assert.Equal((0.0, 0.0, 1.0), (firstObservation.Score, firstObservation.Certainty, firstObservation.Temporal_relevance));
    }

    [Fact]
    public async Task CLS_004_while_the_classifier_is_down_the_last_real_assessment_is_reused_with_its_staleness()
    {
        await using var engine = await RunningEngine.StartAsync(new Dictionary<string, string> { ["Classifier__TimeoutSeconds"] = "5" });
        var t1 = new DateTimeOffset(2021, 5, 3, 20, 15, 0, TimeSpan.Zero);
        await PostAsync(engine, Batch(MarketData("VXN", 25.0, t1)));
        await engine.StopClassifierAsync();

        var during = await PostAsync(engine, Batch(MarketData("VXN", 26.0, t1.AddDays(1)), MarketData("GVZ", 18.0, t1.AddDays(1))));
        var client = engine.Client(Role.Read);
        var fallback = Assert.Single(await client.GetAssessmentsAsync(during.Signals.First().Signal_id, null, Token));
        var withoutHistory = await client.GetAssessmentsAsync(during.Signals.Last().Signal_id, null, Token);

        Assert.True(fallback.Is_fallback);
        Assert.Equal(86400, fallback.Staleness_seconds);
        Assert.Empty(withoutHistory);
    }

    [Fact]
    public async Task A_macro_print_without_sourced_consensus_is_not_assessed()
    {
        var at = new DateTimeOffset(2022, 7, 13, 12, 30, 0, TimeSpan.Zero);
        var response = await PostAsync(fixture.Engine, Batch(Macroeconomic("bls:CPI_YOY", 9.1, at), MarketData("OVX", 45.0, at)));
        var client = fixture.Engine.Client(Role.Read);

        Assert.Empty(await client.GetAssessmentsAsync(response.Signals.First().Signal_id, null, Token));
        Assert.Single(await client.GetAssessmentsAsync(response.Signals.Last().Signal_id, null, Token));
    }

    [Fact]
    public async Task A_macro_print_with_a_sourced_consensus_row_is_classified()
    {
        await using var engine = await RunningEngine.StartAsync(new Dictionary<string, string>
        {
            ["Consensus__Directory"] = Path.Combine(AppContext.BaseDirectory, "Data", "consensus"),
        });
        var claims = Macroeconomic("dol:INITIAL_CLAIMS", 219000, new DateTimeOffset(2026, 4, 9, 12, 30, 0, TimeSpan.Zero));
        ((JsonObject)claims["structured_payload"]!)["indicator_type"] = "EMPLOYMENT";

        var response = await PostAsync(engine, Batch(claims));
        var assessment = Assert.Single(await engine.Client(Role.Read).GetAssessmentsAsync(response.Signals.Single().Signal_id, null, Token));

        // No earlier surprise has a consensus row, so the classifier reports insufficient history.
        Assert.Equal(0.0, assessment.Certainty);
        Assert.Equal(ClassificationMethod.RULE_BASED, assessment.Classification_method);
    }

    [Fact]
    public async Task A_geopolitical_signal_has_no_assessment_because_that_classifier_route_is_not_in_v1()
    {
        var at = new DateTimeOffset(2022, 2, 24, 3, 0, 0, TimeSpan.Zero);
        var response = await PostAsync(fixture.Engine, Batch(Geopolitical("Europe", 0.9, at), MarketData("OVX", 40.0, at)));
        var client = fixture.Engine.Client(Role.Read);

        Assert.Empty(await client.GetAssessmentsAsync(response.Signals.First().Signal_id, null, Token));
        Assert.Single(await client.GetAssessmentsAsync(response.Signals.Last().Signal_id, null, Token));
    }

    private static async Task<List<SignalAssessment>> AsOfAsync(HttpClient http, string asOf) =>
        (await http.GetFromJsonAsync<List<SignalAssessment>>($"classification/assessments?as_of={asOf}", ApiJsonOptions.Value, Token))!;

    private static async Task<IngestSignalsResponse> PostAsync(RunningEngine engine, JsonObject batch)
    {
        using var http = engine.Http(Role.Admin);
        using var response = await http.PostAsync(new Uri("admin/signals", UriKind.Relative),
            new StringContent(batch.ToJsonString(), Encoding.UTF8, "application/json"), Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<IngestSignalsResponse>(ApiJsonOptions.Value, Token))!;
        Assert.Equal(0, body.Rejected_count);
        return body;
    }
}
