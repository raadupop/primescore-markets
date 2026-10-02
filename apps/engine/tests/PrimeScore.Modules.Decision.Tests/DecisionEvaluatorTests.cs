using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Decision.Evaluation;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Decision.Tests;

/// <summary>
/// DEC-001 to DEC-003 as resolved in ADR-0008: the gate is the reference level's distance into
/// either tail of its own history; the state is recorded without a direction. Inputs and
/// expectations are worked out in the test names and comments.
/// </summary>
public sealed class DecisionEvaluatorTests
{
    private static readonly DateTimeOffset Event = new(2018, 2, 5, 21, 15, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<DeployCondition> Defaults =
    [
        new(DeployConditionNames.LevelPercentileTail, "<=", 0.05),
        new(DeployConditionNames.ContributingSources, ">=", 1),
        new(DeployConditionNames.TopSignalCertainty, ">=", 0.5),
        new(DeployConditionNames.NewestObservationAge, "<=", 2),
    ];

    [Fact]
    public void Volmageddon_at_percentile_0_9992_is_in_the_upper_tail_so_DEPLOY_extreme_high_with_four_conditions_and_no_direction()
    {
        // VIX 37.32 is above 1,259 of its 1,260 prior closes: tail = 1 − 0.99921 = 0.00079 ≤ 0.05.
        var eventClose = Assessment("VIX", Event, 0.9992, 1.0);
        var dayBefore = Assessment("VIX", Event.AddDays(-3), 0.8253, 0.9992);

        var result = DecisionEvaluator.Evaluate(Aggregate(0.9992, 0.9992063, "high_vol", [eventClose, dayBefore]), Defaults);

        Assert.Equal((DecisionOutcome.Deploy, VolatilityState.ExtremeHigh), (result.Outcome, result.Scenario));
        Assert.Equal(
            [("contributing_sources", 1, 1, true), ("top_signal_certainty", 0.5, 1.0, true), ("newest_observation_age_trading_days", 2, 0, true)],
            result.Conditions.Skip(1).Select(condition => (condition.Name, condition.Required, condition.Actual, condition.Passed)));
        Assert.Equal(("level_percentile_tail", 0.05, true), (result.Conditions[0].Name, result.Conditions[0].Required, result.Conditions[0].Passed));
        Assert.Equal(0.0007937, result.Conditions[0].Actual, 7);
        Assert.Equal([eventClose.SignalId, dayBefore.SignalId], result.TopContributing.Select(signal => signal.SignalId));
        Assert.Empty(result.Dissenting);
        Assert.StartsWith("DEPLOY, extreme_high state: all 4 conditions held.", result.Explanation, StringComparison.Ordinal);
        Assert.Contains("not where it goes", result.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("vol-expansion", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_composite_rank_of_0_9_with_the_level_at_percentile_0_86_is_not_extreme_so_IDLE_stretched()
    {
        // The 2018-02-02 close (17.31) sat at percentile 0.859 of its prior closes: tail 0.141 > 0.05.
        // Under the refuted gate (|composite| ≥ 0.5) this was a DEPLOY; under ADR-0008 it is not.
        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 0.8594, "high_vol", [Assessment("VIX", Event, 0.9, 1.0)]), Defaults);

        Assert.Equal((DecisionOutcome.Idle, VolatilityState.High, 4), (result.Outcome, result.Scenario, result.Conditions.Count));
        Assert.Equal(["level_percentile_tail"], result.Conditions.Where(condition => !condition.Passed).Select(condition => condition.Name));
        Assert.StartsWith("IDLE: level_percentile_tail 0.1406 not <= 0.05.", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void The_5_October_2017_low_at_percentile_0_003_is_a_DEPLOY_extreme_low_by_the_same_tail()
    {
        var eventClose = Assessment("VIX", Event, -0.8849, 1.0);

        var result = DecisionEvaluator.Evaluate(Aggregate(-0.8849, 0.003, "low_vol", [eventClose]), Defaults);

        Assert.Equal((DecisionOutcome.Deploy, VolatilityState.ExtremeLow), (result.Outcome, result.Scenario));
        Assert.Equal(0.003, result.Conditions.Single(condition => condition.Name == "level_percentile_tail").Actual, 12);
    }

    [Fact]
    public void Without_level_history_the_tail_is_recorded_as_1_and_fails_visibly_with_state_unknown()
    {
        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, null, "normal", [Assessment("VIX", Event, 0.9, 1.0)]), Defaults);

        var tail = result.Conditions.Single(condition => condition.Name == "level_percentile_tail");
        Assert.Equal((DecisionOutcome.Idle, VolatilityState.Unknown, DecisionEvaluator.NoHistoryTail, false), (result.Outcome, result.Scenario, tail.Actual, tail.Passed));
        Assert.Contains("at least 252 are needed", tail.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(251)]
    public void A_new_high_among_fewer_than_252_prior_closes_is_not_placed_so_it_cannot_fire(int history)
    {
        // Percentile 1.0 over a short window would read as the extreme tail on most days of a trending start.
        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 1.0, "high_vol", [Assessment("VIX", Event, 0.9, 1.0)], history: history), Defaults);

        Assert.Equal((DecisionOutcome.Idle, VolatilityState.Unknown), (result.Outcome, result.Scenario));
    }

    [Fact]
    public void With_a_strict_tail_the_boundary_is_neither_a_DEPLOY_nor_labelled_extreme()
    {
        IReadOnlyList<DeployCondition> strict = [new(DeployConditionNames.LevelPercentileTail, "<", 0.05), .. Defaults.Skip(1)];

        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 0.95, "high_vol", [Assessment("VIX", Event, 0.9, 1.0)]), strict);

        Assert.Equal((DecisionOutcome.Idle, VolatilityState.High), (result.Outcome, result.Scenario));
    }

    [Fact]
    public void The_dislocation_and_the_cooldown_placeholder_are_no_longer_evaluated_even_when_the_dislocation_is_huge()
    {
        var result = DecisionEvaluator.Evaluate(Aggregate(0.9992, 0.5, "normal", [Assessment("VIX", Event, 0.9992, 1.0)], dislocation: 40), Defaults);

        Assert.DoesNotContain(result.Conditions, condition => condition.Name is DeployConditionNames.Dislocation or DeployConditionNames.Cooldown);
        Assert.Equal((DecisionOutcome.Idle, VolatilityState.Normal), (result.Outcome, result.Scenario));
    }

    [Fact]
    public void An_optional_composite_score_condition_is_still_evaluated_when_configured()
    {
        IReadOnlyList<DeployCondition> withComposite = [.. Defaults, new(DeployConditionNames.CompositeScore, ">=", 0.95)];

        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 0.999, "high_vol", [Assessment("VIX", Event, 0.9, 1.0)]), withComposite);

        Assert.Equal(DecisionOutcome.Idle, result.Outcome);
        Assert.Equal(["composite_score"], result.Conditions.Where(condition => !condition.Passed).Select(condition => condition.Name));
    }

    [Fact]
    public void The_close_before_Volmageddon_names_2018_02_01_as_dissenting_and_2018_02_02_as_top_with_certainty_0_9992()
    {
        var february2 = Assessment("VIX", Event.AddDays(-3), 0.8253, 0.9992);
        var february1 = Assessment("VIX", Event.AddDays(-4), -0.0413, 0.9984);

        var result = DecisionEvaluator.Evaluate(Aggregate(0.78340584, 0.8594, "high_vol", [february1, february2], at: Event.AddDays(-3)), Defaults);

        Assert.Equal([february2.SignalId], result.TopContributing.Select(signal => signal.SignalId));
        Assert.Equal([february1.SignalId], result.Dissenting.Select(signal => signal.SignalId));
        Assert.Equal(0.9992, result.Conditions.Single(condition => condition.Name == "top_signal_certainty").Actual);
        Assert.Contains("Dissenting: VIX 2018-02-01 -0.0413 × 1.00.", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_zero_composite_has_no_top_or_dissenting_signal_and_fails_contributing_certainty_and_age()
    {
        var result = DecisionEvaluator.Evaluate(Aggregate(0, 0.5, "normal", []), Defaults);

        Assert.Equal((DecisionOutcome.Idle, VolatilityState.Normal), (result.Outcome, result.Scenario));
        Assert.Empty(result.TopContributing);
        Assert.Empty(result.Dissenting);
        Assert.Equal(
            ["level_percentile_tail", "contributing_sources", "top_signal_certainty", "newest_observation_age_trading_days"],
            result.Conditions.Where(condition => !condition.Passed).Select(condition => condition.Name));
        Assert.Equal(DecisionEvaluator.NoObservationAge, result.Conditions.Single(condition => condition.Name == "newest_observation_age_trading_days").Actual);
    }

    [Theory]
    [InlineData("2026-04-02T20:15:00Z", "2026-04-06T20:15:00Z", 1, true)]
    [InlineData("2026-03-31T20:15:00Z", "2026-04-06T20:15:00Z", 3, false)]
    public void Observation_age_counts_NYSE_trading_days_so_Thursday_to_Monday_over_Good_Friday_is_1(string newest, string at, int days, bool passed)
    {
        var observed = DateTimeOffset.Parse(newest, System.Globalization.CultureInfo.InvariantCulture);
        var asOf = DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture);

        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 0.99, "high_vol", [Assessment("VIX", observed, 0.9, 1.0)], at: asOf), Defaults);

        var age = result.Conditions.Single(condition => condition.Name == "newest_observation_age_trading_days");
        Assert.Equal((days, passed), ((int)age.Actual, age.Passed));
    }

    [Fact]
    public void At_most_three_top_signals_are_named_strongest_first()
    {
        var assessments = new[] { 0.3, 0.9, 0.5, 0.7 }.Select(score => Assessment("VIX", Event, score, 1.0)).ToArray();

        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 0.99, "high_vol", assessments), Defaults);

        Assert.Equal([0.9, 0.7, 0.5], result.TopContributing.Select(signal => signal.Score));
    }

    [Theory]
    [InlineData(0.5, ">=", 0.5, true)]
    [InlineData(0.5, ">", 0.5, false)]
    [InlineData(2.0, "<=", 2.0, true)]
    [InlineData(2.0, "<", 2.0, false)]
    [InlineData(0.0, "==", 0.0, true)]
    public void Operators_compare_actual_against_the_threshold(double actual, string @operator, double threshold, bool holds) =>
        Assert.Equal(holds, DecisionEvaluator.Holds(actual, @operator, threshold));

    [Theory]
    [InlineData(0.9992, "high_vol", 0.05, "extreme_high")]
    [InlineData(0.95, "high_vol", 0.05, "extreme_high")]
    [InlineData(0.94, "high_vol", 0.05, "high")]
    [InlineData(0.5, "normal", 0.05, "normal")]
    [InlineData(0.2, "low_vol", 0.05, "low")]
    [InlineData(0.05, "low_vol", 0.05, "extreme_low")]
    [InlineData(0.15, "low_vol", 0.20, "extreme_low")]
    public void States_label_the_percentile_by_tail_then_by_regime(double percentile, string regime, double tail, string state) =>
        Assert.Equal(state, VolatilityState.Label(percentile, 1260, regime, "<=", tail));

    private static ConfirmedAssessment Assessment(string instrument, DateTimeOffset observedAt, double score, double certainty) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, SourceCategory.MarketData, instrument, observedAt, score, certainty, IsFallback: false);

    private static AggregateRecord Aggregate(double composite, double? percentile, string regime, IReadOnlyList<ConfirmedAssessment> confirmed,
        DateTimeOffset? at = null, double dislocation = 5.0, int history = 1260)
    {
        var asOf = at ?? Event;
        var correlation = CorrelationId.New();
        var contributing = confirmed.Count == 0
            ? Array.Empty<CategoryContribution>()
            : [new CategoryContribution(SourceCategory.MarketData, 0.3, 1, 0, composite, composite, null, null, confirmed.Select(assessment => assessment.AssessmentId).ToArray(), [])];
        var compositeView = new CompositeView(Guid.NewGuid(), 10, correlation, new ConfigVersion(1), "equity", composite, "srs_default", "MaxConfirmedWeighted",
            contributing, [], Guid.NewGuid(), Guid.NewGuid(), asOf, asOf);
        var dislocationView = new DislocationView(Guid.NewGuid(), 11, correlation, new ConfigVersion(1), "equity", compositeView.CompositeId, composite,
            "VIX", 37.32, asOf, Guid.NewGuid(), regime, percentile, percentile is null ? 0 : history, 0.5, 37.32 + dislocation, dislocation, 1.5, Math.Abs(dislocation) >= 1.5, asOf, asOf);
        return new AggregateRecord(compositeView, dislocationView, confirmed);
    }
}
