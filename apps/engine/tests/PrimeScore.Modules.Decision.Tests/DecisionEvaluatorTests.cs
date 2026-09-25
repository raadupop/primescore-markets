using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Decision.Evaluation;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Decision.Tests;

/// <summary>DEC-001 to DEC-003 with the brief §9.6 defaults; inputs and expectations worked out in the test names and comments.</summary>
public sealed class DecisionEvaluatorTests
{
    private static readonly DateTimeOffset Event = new(2018, 2, 5, 21, 15, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<DeployCondition> Defaults =
    [
        new(DeployConditionNames.CompositeScore, ">=", 0.5),
        new(DeployConditionNames.ContributingSources, ">=", 1),
        new(DeployConditionNames.TopSignalCertainty, ">=", 0.5),
        new(DeployConditionNames.NewestObservationAge, "<=", 2),
    ];

    [Fact]
    public void Volmageddon_0_9992_with_dislocation_18_645072_holds_all_six_conditions_so_DEPLOY_vol_expansion()
    {
        var eventClose = Assessment("VIX", Event, 0.9992, 1.0);
        var dayBefore = Assessment("VIX", Event.AddDays(-3), 0.8253, 0.9992);

        var result = DecisionEvaluator.Evaluate(Aggregate(0.9992, 18.645072, 1.5, [eventClose, dayBefore]), Defaults);

        Assert.Equal((DecisionOutcome.Deploy, "vol-expansion"), (result.Outcome, result.Scenario));
        Assert.Equal(
            [("dislocation", 1.5, 18.645072, true), ("composite_score", 0.5, 0.9992, true), ("contributing_sources", 1, 1, true),
             ("top_signal_certainty", 0.5, 1.0, true), ("newest_observation_age_trading_days", 2, 0, true), ("active_cooldowns", 0, 0, true)],
            result.Conditions.Select(condition => (condition.Name, condition.Required, condition.Actual, condition.Passed)));
        Assert.Equal([eventClose.SignalId, dayBefore.SignalId], result.TopContributing.Select(signal => signal.SignalId));
        Assert.Empty(result.Dissenting);
        Assert.StartsWith("DEPLOY, vol-expansion scenario.", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_composite_of_0_45_fails_only_composite_score_so_IDLE_and_all_six_values_are_still_recorded()
    {
        var result = DecisionEvaluator.Evaluate(Aggregate(0.45, 8.0, 1.5, [Assessment("VIX", Event, 0.45, 1.0)]), Defaults);

        Assert.Equal(DecisionOutcome.Idle, result.Outcome);
        Assert.Equal(6, result.Conditions.Count);
        Assert.Equal(["composite_score"], result.Conditions.Where(condition => !condition.Passed).Select(condition => condition.Name));
        Assert.StartsWith("IDLE: composite_score 0.45 not >= 0.5.", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dislocation_of_1_2_misses_a_1_5_threshold_so_IDLE_with_actual_1_2_recorded()
    {
        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 1.2, 1.5, [Assessment("VIX", Event, 0.9, 1.0)]), Defaults);

        var dislocation = result.Conditions.Single(condition => condition.Name == "dislocation");
        Assert.Equal((DecisionOutcome.Idle, 1.5, 1.2, false), (result.Outcome, dislocation.Required, dislocation.Actual, dislocation.Passed));
    }

    [Fact]
    public void Low_vol_minus_0_8849_with_dislocation_minus_8_132231_is_a_DEPLOY_vol_compression_by_magnitude()
    {
        var eventClose = Assessment("VIX", Event, -0.8849, 1.0);
        var dayBefore = Assessment("VIX", Event.AddDays(-1), -0.861, 1.0);

        var result = DecisionEvaluator.Evaluate(Aggregate(-0.8849, -8.132231, 1.5, [dayBefore, eventClose]), Defaults);

        Assert.Equal((DecisionOutcome.Deploy, "vol-compression"), (result.Outcome, result.Scenario));
        Assert.Equal(8.132231, result.Conditions.Single(condition => condition.Name == "dislocation").Actual, 9);
        Assert.Equal(0.8849, result.Conditions.Single(condition => condition.Name == "composite_score").Actual, 12);
        Assert.Equal([eventClose.SignalId, dayBefore.SignalId], result.TopContributing.Select(signal => signal.SignalId));
    }

    [Fact]
    public void The_close_before_Volmageddon_names_2018_02_01_as_dissenting_and_2018_02_02_as_top_with_certainty_0_9992()
    {
        var february2 = Assessment("VIX", Event.AddDays(-3), 0.8253, 0.9992);
        var february1 = Assessment("VIX", Event.AddDays(-4), -0.0413, 0.9984);

        var result = DecisionEvaluator.Evaluate(Aggregate(0.78340584, 6.7803775452, 1.5, [february1, february2], Event.AddDays(-3)), Defaults);

        Assert.Equal([february2.SignalId], result.TopContributing.Select(signal => signal.SignalId));
        Assert.Equal([february1.SignalId], result.Dissenting.Select(signal => signal.SignalId));
        Assert.Equal(0.9992, result.Conditions.Single(condition => condition.Name == "top_signal_certainty").Actual);
        Assert.Contains("Dissenting: VIX 2018-02-01 -0.0413 × 1.00.", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_zero_composite_has_no_top_or_dissenting_signal_and_fails_contributing_certainty_and_age()
    {
        var result = DecisionEvaluator.Evaluate(Aggregate(0, 0, 1.5, []), Defaults);

        Assert.Equal((DecisionOutcome.Idle, "none"), (result.Outcome, result.Scenario));
        Assert.Empty(result.TopContributing);
        Assert.Empty(result.Dissenting);
        Assert.Equal(
            ["dislocation", "composite_score", "contributing_sources", "top_signal_certainty", "newest_observation_age_trading_days"],
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

        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 12, 1.5, [Assessment("VIX", observed, 0.9, 1.0)], asOf), Defaults);

        var age = result.Conditions.Single(condition => condition.Name == "newest_observation_age_trading_days");
        Assert.Equal((days, passed), ((int)age.Actual, age.Passed));
    }

    [Fact]
    public void At_most_three_top_signals_are_named_strongest_first()
    {
        var assessments = new[] { 0.3, 0.9, 0.5, 0.7 }.Select(score => Assessment("VIX", Event, score, 1.0)).ToArray();

        var result = DecisionEvaluator.Evaluate(Aggregate(0.9, 12, 1.5, assessments), Defaults);

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

    private static ConfirmedAssessment Assessment(string instrument, DateTimeOffset observedAt, double score, double certainty) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, SourceCategory.MarketData, instrument, observedAt, score, certainty, IsFallback: false);

    private static AggregateRecord Aggregate(double composite, double dislocation, double threshold, IReadOnlyList<ConfirmedAssessment> confirmed, DateTimeOffset? at = null)
    {
        var asOf = at ?? Event;
        var correlation = CorrelationId.New();
        var contributing = confirmed.Count == 0
            ? Array.Empty<CategoryContribution>()
            : [new CategoryContribution(SourceCategory.MarketData, 0.3, 1, 0, composite, composite, null, null, confirmed.Select(assessment => assessment.AssessmentId).ToArray(), [])];
        var compositeView = new CompositeView(Guid.NewGuid(), 10, correlation, new ConfigVersion(1), "equity", composite, "srs_default", "MaxConfirmedWeighted",
            contributing, [], Guid.NewGuid(), Guid.NewGuid(), asOf, asOf);
        var dislocationView = new DislocationView(Guid.NewGuid(), 11, correlation, new ConfigVersion(1), "equity", compositeView.CompositeId, composite,
            "VIX", 37.32, asOf, Guid.NewGuid(), "high_vol", 0.99, 1260, 0.5, 37.32 + dislocation, dislocation, threshold, Math.Abs(dislocation) >= threshold, asOf, asOf);
        return new AggregateRecord(compositeView, dislocationView, confirmed);
    }
}
