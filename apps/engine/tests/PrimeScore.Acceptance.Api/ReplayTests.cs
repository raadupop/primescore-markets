using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api;

/// <summary>ANA-001: replay uses observation time, a temporary configuration and the sourced Volmageddon anchor.</summary>
public sealed class ReplayTests(VolmageddonEngine fixture) : IClassFixture<VolmageddonEngine>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANA_001_replay_matches_hand_calculation_and_higher_threshold_delays_deploy_without_changing_live_decisions()
    {
        var client = fixture.Engine.Client(Role.Read);
        var from = VolmageddonEngine.EventAt.AddDays(-3);
        var before = await client.ListDecisionsAsync(from, VolmageddonEngine.EventAt, Token);

        var baseline = await ReplayAsync(from, VolmageddonEngine.EventAt);
        var decisions = baseline.GetProperty("decisions").EnumerateArray().ToArray();
        Assert.Equal(2, decisions.Length);
        // Feb 2: (0.8253 - 0.0412667...) * 0.9992, k=0.5, IV=17.31.
        Assert.Equal(0.78340584, decisions[0].GetProperty("composite_score").GetDouble(), 9);
        Assert.Equal(6.7803775452, decisions[0].GetProperty("dislocation_value").GetDouble(), 9);
        Assert.Equal("DEPLOY", decisions[0].GetProperty("outcome").GetString());
        Assert.Equal(0.9992, decisions[1].GetProperty("composite_score").GetDouble(), 9);
        Assert.Equal(18.645072, decisions[1].GetProperty("dislocation_value").GetDouble(), 9);
        Assert.Empty(baseline.GetProperty("positions").EnumerateArray());
        Assert.Empty(baseline.GetProperty("exits").EnumerateArray());

        var changed = await ReplayAsync(from, VolmageddonEngine.EventAt,
            new { contexts = new { equity = new { dislocation_threshold = 10.0 } } });
        var delayed = changed.GetProperty("decisions").EnumerateArray().ToArray();
        Assert.Equal("IDLE", delayed[0].GetProperty("outcome").GetString());
        Assert.Equal("DEPLOY", delayed[1].GetProperty("outcome").GetString());
        Assert.Equal(10.0, delayed[0].GetProperty("conditions_evaluated").EnumerateArray()
            .Single(c => c.GetProperty("condition_name").GetString() == "dislocation").GetProperty("required_value").GetDouble());
        var after = await client.ListDecisionsAsync(from, VolmageddonEngine.EventAt, Token);
        Assert.Equal(before.Select(d => d.Decision_id), after.Select(d => d.Decision_id));
        Assert.Equal(before.Select(d => d.Dislocation_value), after.Select(d => d.Dislocation_value));
        var again = await ReplayAsync(from, VolmageddonEngine.EventAt);
        Assert.Equal("DEPLOY", again.GetProperty("decisions")[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task ANA_001_future_observations_do_not_enter_an_earlier_replay()
    {
        var at = VolmageddonEngine.EventAt.AddDays(-3);
        var replay = await ReplayAsync(at, at);
        var decision = Assert.Single(replay.GetProperty("decisions").EnumerateArray());
        Assert.Equal(at, decision.GetProperty("decided_at").GetDateTimeOffset());
        Assert.Equal(0.78340584, decision.GetProperty("composite_score").GetDouble(), 9);
        Assert.DoesNotContain(fixture.Signals[^1].Signal_id,
            decision.GetProperty("top_contributing_signals").EnumerateArray().Select(id => id.GetGuid()));
        using var http = fixture.Engine.Http(Role.Read);
        var page = await http.GetStringAsync(new Uri(fixture.Engine.ApiBase, "/replay?id=" + replay.GetProperty("replay_id").GetGuid()), Token);
        Assert.Contains("Volmageddon acceptance", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANA_001_temporary_aggregation_and_sensitivity_are_recomputed_from_the_recorded_inputs()
    {
        var replay = await ReplayAsync(VolmageddonEngine.EventAt, VolmageddonEngine.EventAt, new
        {
            weighting = new { aggregation = "WeightedMean" },
            contexts = new { equity = new { sensitivity = new { low_vol = 0.25, normal = 0.25, high_vol = 0.25 } } },
        });
        var decision = Assert.Single(replay.GetProperty("decisions").EnumerateArray());
        // Mean of 0.9992 and 0.8253 * 0.9992 = 0.91191988; 37.32 * mean * 0.25 = 8.5082124804.
        Assert.Equal(0.91191988, decision.GetProperty("composite_score").GetDouble(), 9);
        Assert.Equal(8.5082124804, decision.GetProperty("dislocation_value").GetDouble(), 9);
    }

    [Theory]
    [InlineData("{\"unknown_setting\":1}")]
    [InlineData("{\"bypass_percentile\":2}")]
    [InlineData("{\"contexts\":{\"equity\":{\"sensitivity\":{\"high_vol\":1.5}}}}")]
    [InlineData("{\"contexts\":{\"missing\":{\"dislocation_threshold\":2}}}")]
    [InlineData("{\"weighting\":null}")]
    [InlineData("{\"contexts\":[{\"name\":\"equity\"}]}")]
    [InlineData("{\"deploy_conditions\":[{\"name\":\"composite_score\"}]}")]
    public async Task ANA_001_invalid_overrides_are_rejected(string overrides)
    {
        using var http = fixture.Engine.Http(Role.Admin);
        using var response = await http.PostAsJsonAsync("replay", new
        {
            event_label = "Invalid overrides",
            from = VolmageddonEngine.EventAt,
            to = VolmageddonEngine.EventAt,
            config_overrides = JsonSerializer.Deserialize<JsonElement>(overrides),
        }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<JsonElement> ReplayAsync(DateTimeOffset from, DateTimeOffset to, object? overrides = null)
    {
        using var http = fixture.Engine.Http(Role.Admin);
        using var response = await http.PostAsJsonAsync("replay", new
        {
            event_label = "Volmageddon acceptance",
            from,
            to,
            config_overrides = overrides,
        }, Token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }
}
