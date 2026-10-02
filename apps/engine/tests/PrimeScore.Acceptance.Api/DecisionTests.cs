using System.Text.Json;
using System.Text.RegularExpressions;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;
using static PrimeScore.Acceptance.Api.Harness.SignalDocuments;

namespace PrimeScore.Acceptance.Api;

/// <summary>The Volmageddon history (VIXCLS as the anchor fixture cites it), loaded once for the read-only decision tests.</summary>
public sealed class VolmageddonEngine : IAsyncLifetime
{
    public static readonly DateTimeOffset EventAt = new(2018, 2, 5, 21, 15, 0, TimeSpan.Zero);

    public RunningEngine Engine { get; private set; } = null!;

    /// <summary>History first (2018-02-02 is at index 1259, 2018-02-01 at 1258), the 2018-02-05 event last.</summary>
    public IReadOnlyList<SignalResult> Signals { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        Engine = await RunningEngine.StartAsync();
        Signals = (await AnchorHistory.LoadAsync(Engine, "market_data_vix_volmageddon_2018_02_05.json", new DateOnly(2018, 2, 2), 37.32, EventAt, CancellationToken.None)).Signals;
    }

    public async ValueTask DisposeAsync() => await Engine.DisposeAsync();
}

/// <summary>
/// SRS DEC-001 to DEC-003, AUD-001, AUD-002 and OBS-001 with the default conditions (ADR-0008):
/// the reference level within 5% of either end of its prior closes, contributing categories ≥ 1,
/// top signal certainty ≥ 0.5, newest contributing observation ≤ 2 trading days old. Percentiles are
/// counted by hand from the fixture window; composites are the hand calculation in <see cref="CompositeTests"/>.
/// </summary>
public sealed class DecisionTests(VolmageddonEngine volmageddon) : IClassFixture<VolmageddonEngine>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UI_research_analysis_explains_the_reference_values_and_conditions_without_implying_execution()
    {
        var engine = volmageddon.Engine;
        var decision = Assert.Single(await engine.Client(Role.Read).ListDecisionsAsync(VolmageddonEngine.EventAt, VolmageddonEngine.EventAt, Token));
        using var http = engine.Http(Role.Read);
        var overview = await http.GetStringAsync(new Uri(engine.ApiBase, "/"), Token);
        var analysis = await http.GetStringAsync(new Uri(engine.ApiBase, $"/decisions/{decision.Decision_id}"), Token);

        Assert.Contains("No position opened", PageText(overview), StringComparison.Ordinal);
        Assert.Contains($"href=\"decisions/{decision.Decision_id}\"", overview, StringComparison.Ordinal);
        Assert.Contains("Position: none. Execution: none.", PageText(analysis), StringComparison.Ordinal);
        Assert.Contains("no order was sent", PageText(analysis), StringComparison.Ordinal);
        foreach (var page in new[] { overview, analysis })
        {
            var text = PageText(page);
            Assert.Contains("VIX", text, StringComparison.Ordinal);
            Assert.Contains("US equity volatility", text, StringComparison.Ordinal);
            // 37.32 is at or above 1,259 of its 1,260 prior closes: percentile 0.99921, the extreme upper tail.
            Assert.Contains("37.32", text, StringComparison.Ordinal);
            Assert.Contains("99.9th percentile", text, StringComparison.Ordinal);
            Assert.Contains("Extremely stretched", text, StringComparison.Ordinal);
            Assert.Contains("not a directional forecast", text, StringComparison.Ordinal);
            var workflow = Regex.Match(page, "<nav[^>]*aria-label=\"Research workflow\"[^>]*>(.*?)</nav>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
            Assert.True(workflow.Success, "An analysis must expose the route from data to review.");
            foreach (var route in new[] { "sources", "tape", "./", "decisions" })
            {
                Assert.Contains($"href=\"{route}\"", workflow.Value, StringComparison.Ordinal);
            }
        }

        var rows = Regex.Matches(analysis, "<tr>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(row => PageText(row.Value)).ToArray();
        var tail = Assert.Single(rows, row => row.Contains("Level in the extreme tail", StringComparison.Ordinal));
        // 1 / 1260 = 0.0794%, shown to two decimals so it can be read against the 5.00% requirement.
        Assert.Contains("0.08% from the nearer end", tail, StringComparison.Ordinal);
        Assert.Contains("At most 5.00% from the nearer end", tail, StringComparison.Ordinal);
        Assert.Contains("not a forecast", tail, StringComparison.Ordinal);
        var confidence = Assert.Single(rows, row => row.Contains("Strongest signal confidence", StringComparison.Ordinal));
        Assert.Contains("100%", confidence, StringComparison.Ordinal);
        Assert.Contains("At least 50%", confidence, StringComparison.Ordinal);
        Assert.Contains("not a probability of a profitable trade", confidence, StringComparison.Ordinal);

        var observation = await http.GetStringAsync(new Uri(engine.ApiBase, $"/tape?signal={volmageddon.Signals[^1].Signal_id}"), Token);
        var selected = observation.IndexOf("id=\"selected-signal\"", StringComparison.Ordinal);
        Assert.True(selected >= 0 && selected < observation.IndexOf("aria-label=\"Filters\"", StringComparison.Ordinal),
            "Following evidence must show the selected observation before the searchable list.");
        Assert.Contains("37.3200", PageText(observation), StringComparison.Ordinal);
        Assert.Contains("Comparison history", PageText(observation), StringComparison.Ordinal);
        Assert.Contains("not the probability of a profitable position", PageText(observation), StringComparison.Ordinal);
    }

    /// <summary>The Decision journal (slice 1): newest first, the Volmageddon close leads with every check passed.</summary>
    [Fact]
    public async Task UI_decision_journal_lists_Volmageddon_first_as_all_checks_passed_in_the_extremely_stretched_state()
    {
        var engine = volmageddon.Engine;
        var decisions = await engine.Client(Role.Read).ListDecisionsAsync(null, null, Token);
        var decision = Assert.Single(decisions, candidate => candidate.Decided_at == VolmageddonEngine.EventAt);
        using var http = engine.Http(Role.Read);
        var page = await http.GetStringAsync(new Uri(engine.ApiBase, "/decisions"), Token);
        var text = PageText(page);

        // The journal shows at most the newest 300 records.
        Assert.Contains($"Records shown {Math.Min(decisions.Count, 300).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}", text, StringComparison.Ordinal);
        Assert.Contains("No order was sent", text, StringComparison.Ordinal);
        var newest = Regex.Matches(page, "<tr>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(row => row.Value).First(row => row.Contains("<td", StringComparison.Ordinal));
        Assert.Contains($"href=\"decisions/{decision.Decision_id}\"", newest, StringComparison.Ordinal);
        var row = PageText(newest);
        Assert.Contains("2018-02-05 21:15 UTC", row, StringComparison.Ordinal);
        Assert.Contains("All checks passed", row, StringComparison.Ordinal);
        Assert.Contains("Extremely stretched", row, StringComparison.Ordinal);
        Assert.Contains("4 of 4 passed", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DEC_001_and_DEC_003_Volmageddon_is_a_DEPLOY_with_every_condition_and_its_explanation_recorded()
    {
        var eventSignal = volmageddon.Signals[^1].Signal_id;
        var dayBefore = volmageddon.Signals[^2].Signal_id;

        var decision = Assert.Single(await volmageddon.Engine.Client(Role.Read).ListDecisionsAsync(VolmageddonEngine.EventAt, VolmageddonEngine.EventAt, Token));

        // Composite 0.9992, dislocation 18.645072 (recorded, no longer a gate); the event (0.9992 × 1.0) leads the 2018-02-02
        // close (0.8253 × 0.9992); nothing dissents. Level percentile 1259 / 1260, so tail 1 / 1260 = 0.000794 ≤ 0.05.
        Assert.Equal(DecisionOutcome.DEPLOY, decision.Outcome);
        Assert.Equal("extreme_high", decision.State);
        Assert.Equal(1259.0 / 1260, decision.Level_percentile!.Value, 12);
        Assert.Equal(0.9992, decision.Composite_score, 12);
        Assert.Equal(18.645072, decision.Dislocation_value, 9);
        Assert.Equal([eventSignal, dayBefore], decision.Top_contributing_signals);
        Assert.Empty(decision.Dissenting_signals);
        Assert.Equal(VolmageddonEngine.EventAt, decision.Decided_at);
        AssertConditions(decision,
            ("level_percentile_tail", 0.05, 1.0 / 1260, true),
            ("contributing_sources", 1, 1, true),
            ("top_signal_certainty", 0.5, 1.0, true),
            ("newest_observation_age_trading_days", 2, 0, true));
    }

    [Fact]
    public async Task DEC_003_the_close_before_names_the_2018_02_01_close_as_dissenting()
    {
        // 2018-02-02: +0.82463976 (17.31) against −0.04123392 (13.47): composite 0.78340584, dislocation 6.7803775452.
        var dayBefore = volmageddon.Signals[^2].Signal_id;
        var twoDaysBefore = volmageddon.Signals[^3].Signal_id;
        var at = VolmageddonEngine.EventAt.AddDays(-3);

        var decision = Assert.Single(await volmageddon.Engine.Client(Role.Read).ListDecisionsAsync(at, at, Token));

        // 17.31 is at percentile 0.8594 of its 1,259 prior closes: stretched, not extreme (tail 0.1406 > 0.05).
        // The refuted composite gate called this close a DEPLOY; the state gate does not.
        Assert.Equal((DecisionOutcome.IDLE, "high"), (decision.Outcome, decision.State));
        Assert.Equal(["level_percentile_tail"], decision.Conditions_evaluated.Where(condition => !condition.Passed).Select(condition => condition.Condition_name));
        Assert.Equal(0.78340584, decision.Composite_score, 9);
        Assert.Equal(6.7803775452, decision.Dislocation_value, 9);
        Assert.Equal([dayBefore], decision.Top_contributing_signals);
        Assert.Equal([twoDaysBefore], decision.Dissenting_signals);
        Assert.Equal(0.9992, decision.Conditions_evaluated.Single(condition => condition.Condition_name == "top_signal_certainty").Actual_value, 12);
    }

    [Fact]
    public async Task AUD_001_each_decision_has_an_append_only_audit_entry_with_its_inputs_and_outputs()
    {
        var client = volmageddon.Engine.Client(Role.Read);
        var decision = Assert.Single(await client.ListDecisionsAsync(VolmageddonEngine.EventAt, VolmageddonEngine.EventAt, Token));

        var entry = Assert.Single(await client.ListAuditEntriesAsync(decision.Decision_id, null, null, null, Token));
        var deploys = await client.ListAuditEntriesAsync(null, AuditEventType.DEPLOY_DECISION, null, null, Token);
        var approvals = await client.ListAuditEntriesAsync(null, AuditEventType.APPROVAL_REQUESTED, null, null, Token);

        Assert.Equal((AuditEventType.DEPLOY_DECISION, decision.Decision_id), (entry.Event_type, entry.Entity_id));
        var input = (JsonElement)entry.Input_snapshot;
        var output = (JsonElement)entry.Output_snapshot;
        Assert.Equal(0.9992, input.GetProperty("composite_score").GetDouble(), 12);
        Assert.Equal(18.645072, input.GetProperty("dislocation_value").GetDouble(), 9);
        Assert.Equal("DEPLOY", output.GetProperty("outcome").GetString());
        Assert.Equal(4, output.GetProperty("conditions_evaluated").GetArrayLength());
        Assert.Equal("extreme_high", output.GetProperty("scenario").GetString());
        var idles = await client.ListAuditEntriesAsync(null, AuditEventType.IDLE_DECISION, null, null, Token);
        Assert.NotEqual(default, entry.Recorded_at);
        Assert.Contains(deploys, audit => audit.Entity_id == decision.Decision_id);
        Assert.DoesNotContain(idles, audit => audit.Entity_id == decision.Decision_id);
        Assert.All(deploys, audit => Assert.Equal(AuditEventType.DEPLOY_DECISION, audit.Event_type));
        Assert.True(deploys.Zip(deploys.Skip(1)).All(pair => pair.First.Sequence_number < pair.Second.Sequence_number), "sequence numbers increase");
        Assert.Empty(approvals);
    }

    [Fact]
    public async Task OBS_001_one_correlation_id_traces_the_event_close_from_ingestion_to_its_decision()
    {
        var client = volmageddon.Engine.Client(Role.Read);
        var decision = Assert.Single(await client.ListDecisionsAsync(VolmageddonEngine.EventAt, VolmageddonEngine.EventAt, Token));

        var logs = await client.GetLogsByCorrelationAsync(decision.Correlation_id, Token);

        Assert.Equal(["ingestion", "classification", "composite", "dislocation", "decision"], logs.Select(log => log.Stage).Distinct());
        var ingestion = logs.First(log => log.Stage == "ingestion");
        Assert.Equal(volmageddon.Signals[^1].Signal_id.ToString(), ((JsonElement)ingestion.Details!).GetProperty("entity_id").GetString());
    }

    [Fact]
    public async Task DEC_001_and_DEC_002_one_failing_condition_is_IDLE_with_every_value_recorded_and_processing_continues()
    {
        // Tail at most 0.001, then 2018-02-06 (29.98): 1,255 of its 1,260 prior closes are at or below it, percentile
        // 0.996032, tail 5 / 1260 = 0.003968 > 0.001. Only the tail condition fails.
        await using var engine = await RunningEngine.StartAsync();
        await AnchorHistory.LoadAsync(engine, "market_data_vix_volmageddon_2018_02_05.json", new DateOnly(2018, 2, 2), 37.32, VolmageddonEngine.EventAt, Token);
        await engine.Client(Role.Admin).SetDeployConditionsAsync(new DeployConditionsConfig
        {
            Conditions = [new Conditions { Name = "level_percentile_tail", Operator = ConditionsOperator.Le, Threshold = 0.001 }],
        }, Token);
        var next = VolmageddonEngine.EventAt.AddDays(1);
        await AnchorHistory.PostAsync(engine, Batch(MarketData("VIX", 29.98, next, source: "fixture:VIXCLS")), Token);
        var client = engine.Client(Role.Read);

        var decision = Assert.Single(await client.ListDecisionsAsync(next, next, Token));
        var composite = await client.GetCompositeScoreAsync(next, "equity", Token);

        Assert.Equal(DecisionOutcome.IDLE, decision.Outcome);
        AssertConditions(decision,
            ("level_percentile_tail", 0.001, 5.0 / 1260, false),
            ("contributing_sources", 1, 1, true),
            ("top_signal_certainty", 0.5, 1.0, true),
            ("newest_observation_age_trading_days", 2, 0, true));
        // The state uses the configured tail too: 0.003968 is outside a 0.001 tail, so stretched, not extreme.
        Assert.Equal("high", decision.State);

        // DEC-002: idle means no exposure, and signal processing carried on for that close.
        Assert.Equal(next, composite.As_of);
        var noPositions = await Assert.ThrowsAsync<PrimeScoreApiException<ErrorResponse>>(() => client.ListPositionsAsync(null, Token));
        Assert.Equal(501, noPositions.StatusCode);
    }

    [Fact]
    public async Task AUD_002_a_decision_modified_in_storage_is_detected_by_ledger_verification()
    {
        await using var engine = await RunningEngine.StartAsync();
        var at = new DateTimeOffset(2019, 5, 6, 20, 15, 0, TimeSpan.Zero);
        await AnchorHistory.PostAsync(engine, Batch(MarketData("VIX", 15.0, at), MarketData("VIX", 16.0, at.AddDays(1))), Token);
        var decision = (await engine.Client(Role.Read).ListDecisionsAsync(null, null, Token)).First();
        await engine.StopEngineAsync();
        var intact = await engine.RunEngineCommandAsync("verify-ledger");

        // Flip the recorded outcome of the decision at storage level, bypassing the append-only trigger.
        await RunningEngine.RunPythonAsync(
            "import sqlite3, sys; c = sqlite3.connect(sys.argv[1]); c.execute('DROP TRIGGER IF EXISTS ledger_reject_update'); " +
            "n = c.execute(\"UPDATE ledger SET payload = replace(payload, '\\\"Idle\\\"', '\\\"Deploy\\\"') WHERE entity_id = ? OR payload LIKE ?\", (sys.argv[2], '%' + sys.argv[2] + '%')).rowcount; " +
            "c.commit(); print(n)",
            engine.DatabasePath, decision.Decision_id.ToString());
        var tampered = await engine.RunEngineCommandAsync("verify-ledger");

        Assert.Equal(DecisionOutcome.IDLE, decision.Outcome);
        Assert.Equal(0, intact.ExitCode);
        Assert.True(tampered.ExitCode == 1, $"verify-ledger after tampering exited {tampered.ExitCode}: {tampered.Output}");
    }

    [Fact]
    public async Task Deploy_conditions_change_through_the_API_and_a_null_or_misnamed_condition_is_a_400()
    {
        await using var engine = await RunningEngine.StartAsync();
        var admin = engine.Client(Role.Admin);
        using var http = engine.Http(Role.Admin);

        await admin.SetDeployConditionsAsync(new DeployConditionsConfig { Conditions = [new Conditions { Name = "composite_score", Operator = ConditionsOperator.Gt, Threshold = 0.6 }] }, Token);
        using var withNull = await http.PutAsync(new Uri("config/deploy-conditions", UriKind.Relative),
            new StringContent("""{"conditions":[null]}""", System.Text.Encoding.UTF8, "application/json"), Token);
        var misnamed = await Assert.ThrowsAnyAsync<PrimeScoreApiException>(() => admin.SetDeployConditionsAsync(
            new DeployConditionsConfig { Conditions = [new Conditions { Name = "dislocation", Operator = ConditionsOperator.Ge, Threshold = 2 }] }, Token));

        var at = new DateTimeOffset(2019, 5, 6, 20, 15, 0, TimeSpan.Zero);
        await AnchorHistory.PostAsync(engine, Batch(MarketData("VIX", 15.0, at), MarketData("VIX", 16.0, at.AddDays(1))), Token);
        var decision = (await engine.Client(Role.Read).ListDecisionsAsync(null, null, Token)).First();

        var compositeCondition = decision.Conditions_evaluated.Single(condition => condition.Condition_name == "composite_score");
        Assert.Equal(0.6, compositeCondition.Required_value);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, withNull.StatusCode);
        Assert.Equal(400, misnamed.StatusCode);
        Assert.Contains("PUT /config/dislocation-threshold", misnamed.Response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_decision_is_a_404()
    {
        var missing = await Assert.ThrowsAnyAsync<PrimeScoreApiException>(() => volmageddon.Engine.Client(Role.Read).GetDecisionAsync(Guid.NewGuid(), Token));

        Assert.Equal(404, missing.StatusCode);
    }

    private static void AssertConditions(DecisionRecord decision, params (string Name, double Required, double Actual, bool Passed)[] expected)
    {
        Assert.Equal(expected.Select(condition => condition.Name), decision.Conditions_evaluated.Select(condition => condition.Condition_name));
        foreach (var (name, required, actual, passed) in expected)
        {
            var condition = decision.Conditions_evaluated.Single(evaluation => evaluation.Condition_name == name);
            Assert.Equal(required, condition.Required_value, 9);
            Assert.Equal(actual, condition.Actual_value, 9);
            Assert.True(passed == condition.Passed, $"{name}: expected passed={passed}");
        }
    }

    private static string PageText(string html) => Regex.Replace(
        System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.CultureInvariant)),
        @"\s+", " ", RegexOptions.CultureInvariant);
}
