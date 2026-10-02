using System.Globalization;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Decision.Evaluation;

/// <param name="Scenario">The volatility state label (<see cref="VolatilityState"/>); the field keeps its pre-ADR-0008 name for storage compatibility.</param>
internal sealed record DecisionResult(
    DecisionOutcome Outcome,
    string Scenario,
    IReadOnlyList<ConditionEvaluation> Conditions,
    IReadOnlyList<DecisionSignal> TopContributing,
    IReadOnlyList<DecisionSignal> Dissenting,
    string Explanation);

/// <summary>
/// SRS DEC-001 to DEC-003 as resolved in ADR-0008. DEPLOY only when every configured condition
/// holds; every condition's required and actual value is recorded either way. The gate is the
/// reference level's position in its own history (<c>level_percentile_tail</c>); the dislocation
/// and the cooldown placeholder are no longer evaluated, because the first is the level times a
/// rank of the level and the second was always true. Pure: replay evaluates the same inputs
/// under other settings (ANA-001).
/// </summary>
internal static class DecisionEvaluator
{
    /// <summary>Contributing signals named in an explanation.</summary>
    public const int TopSignals = 3;

    /// <summary>Recorded as the age when no observation contributed, so the age condition fails visibly.</summary>
    public const double NoObservationAge = 9999;

    /// <summary>Recorded as the tail distance when the level has too little history, so the tail condition fails visibly.</summary>
    public const double NoHistoryTail = 1;

    public static DecisionResult Evaluate(AggregateRecord aggregate, IReadOnlyList<DeployCondition> conditions)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(conditions);
        var composite = aggregate.Composite;
        var dislocation = aggregate.Dislocation;
        var sign = Math.Sign(composite.Score);

        // Top: confirmed assessments on the composite's side; dissenting: those against it (DEC-003).
        var top = sign == 0 ? [] : Ranked(aggregate.Confirmed.Where(assessment => Math.Sign(assessment.Conviction) == sign)).Take(TopSignals).ToArray();
        var dissenting = sign == 0 ? [] : Ranked(aggregate.Confirmed.Where(assessment => Math.Sign(assessment.Conviction) == -sign)).ToArray();

        var tailCondition = conditions.FirstOrDefault(condition => condition.Name == DeployConditionNames.LevelPercentileTail);
        var state = VolatilityState.Label(dislocation.RegimePercentile, dislocation.RegimeHistory, dislocation.Regime,
            tailCondition?.Operator ?? "<=", tailCondition?.Threshold ?? VolatilityState.DefaultTail);

        var evaluations = new List<ConditionEvaluation>(conditions.Count);
        foreach (var condition in conditions)
        {
            var (actual, detail) = condition.Name switch
            {
                DeployConditionNames.LevelPercentileTail => VolatilityState.Tail(dislocation.RegimePercentile, dislocation.RegimeHistory) is { } tail
                    ? (tail, Invariant($"{dislocation.ReferenceInstrument} {dislocation.MarketObservedIv:0.00} at percentile {dislocation.RegimePercentile:0.000} of {dislocation.RegimeHistory} prior closes ({dislocation.Regime} regime)"))
                    : (NoHistoryTail, Invariant($"{dislocation.RegimeHistory} prior closes of {dislocation.ReferenceInstrument}; at least {VolatilityState.MinimumHistory} are needed to place the level (recorded as {NoHistoryTail})")),
                DeployConditionNames.CompositeScore => (Math.Abs(composite.Score), Invariant($"|{composite.Score:+0.0000;-0.0000;0}|")),
                DeployConditionNames.ContributingSources => (composite.Contributing.Count,
                    composite.Contributing.Count == 0 ? "no category had a confirmed assessment" : string.Join(", ", composite.Contributing.Select(category => category.Category.ToWireName()))),
                DeployConditionNames.TopSignalCertainty => top.Length == 0
                    ? (0.0, "no contributing signal")
                    : (top[0].Certainty, Invariant($"{top[0].Instrument} {top[0].ObservedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC, severity {top[0].Score:+0.0000;-0.0000;0}")),
                DeployConditionNames.NewestObservationAge => NewestAge(aggregate, dislocation.AsOf),
                _ => throw new InvalidOperationException($"Unknown deploy condition '{condition.Name}' in validated settings."),
            };
            evaluations.Add(new ConditionEvaluation(condition.Name, condition.Operator, condition.Threshold, actual, Holds(actual, condition.Operator, condition.Threshold), detail));
        }

        var outcome = evaluations.All(evaluation => evaluation.Passed) ? DecisionOutcome.Deploy : DecisionOutcome.Idle;
        return new DecisionResult(outcome, state, evaluations, top, dissenting, Explain(outcome, state, aggregate, evaluations, top, dissenting));
    }

    public static bool Holds(double actual, string @operator, double threshold) => @operator switch
    {
        ">=" => actual >= threshold,
        ">" => actual > threshold,
        "<=" => actual <= threshold,
        "<" => actual < threshold,
        "==" => actual == threshold,
        _ => throw new InvalidOperationException($"Unknown operator '{@operator}' in validated settings."),
    };

    private static IEnumerable<DecisionSignal> Ranked(IEnumerable<ConfirmedAssessment> assessments) =>
        assessments
            .OrderByDescending(assessment => Math.Abs(assessment.Conviction))
            .ThenByDescending(assessment => assessment.ObservedAt)
            .ThenBy(assessment => assessment.LedgerSequence)
            .Select(assessment => new DecisionSignal(
                assessment.SignalId, assessment.AssessmentId, assessment.Category, assessment.Instrument,
                assessment.ObservedAt, assessment.Score, assessment.Certainty, assessment.Conviction));

    /// <summary>NYSE trading days from the newest contributing observation to the decision.</summary>
    private static (double Actual, string Detail) NewestAge(AggregateRecord aggregate, DateTimeOffset asOf)
    {
        if (aggregate.Confirmed.Count == 0)
        {
            return (NoObservationAge, Invariant($"no contributing observation (recorded as {NoObservationAge})"));
        }

        var newest = aggregate.Confirmed.Max(assessment => assessment.ObservedAt);
        var days = MarketTime.TradingDaysBetween(MarketTime.NewYorkDate(newest), MarketTime.NewYorkDate(asOf));
        return (days, Invariant($"newest contributing observation {newest.UtcDateTime:yyyy-MM-dd HH:mm} UTC"));
    }

    private static string Explain(
        DecisionOutcome outcome,
        string state,
        AggregateRecord aggregate,
        IReadOnlyList<ConditionEvaluation> evaluations,
        IReadOnlyList<DecisionSignal> top,
        IReadOnlyList<DecisionSignal> dissenting)
    {
        var composite = aggregate.Composite;
        var dislocation = aggregate.Dislocation;
        var head = outcome == DecisionOutcome.Deploy
            ? Invariant($"DEPLOY, {state} state: all {evaluations.Count} conditions held")
            : "IDLE: " + string.Join("; ", evaluations.Where(evaluation => !evaluation.Passed).Select(Failed));
        var level = (dislocation.RegimePercentile, dislocation.RegimeHistory >= VolatilityState.MinimumHistory) switch
        {
            ({ } percentile, true) => Invariant($"{dislocation.ReferenceInstrument} {dislocation.MarketObservedIv:0.00} at percentile {percentile:0.000} of {dislocation.RegimeHistory} prior closes ({dislocation.Regime} regime)"),
            _ => Invariant($"{dislocation.ReferenceInstrument} {dislocation.MarketObservedIv:0.00} with {dislocation.RegimeHistory} prior closes, too few to place the level"),
        };
        var body = Invariant(
            $"Composite {composite.Score:+0.0000;-0.0000;0} ({(composite.Contributing.Count == 0 ? "no contributing category" : string.Join(", ", composite.Contributing.Select(category => category.Category.ToWireName())))}); {level}. The state describes where the level sits, not where it goes.");
        var signals = top.Count == 0
            ? " No contributing signal."
            : " Top: " + string.Join(", ", top.Select(Describe)) + ".";
        var dissent = dissenting.Count == 0
            ? " No dissenting signal."
            : " Dissenting: " + string.Join(", ", dissenting.Select(Describe)) + ".";
        return $"{head}. {body}{signals}{dissent}";
    }

    private static string Failed(ConditionEvaluation evaluation) =>
        Invariant($"{evaluation.Name} {evaluation.Actual:0.####} not {evaluation.Operator} {evaluation.Required:0.####}");

    private static string Describe(DecisionSignal signal) =>
        Invariant($"{signal.Instrument} {signal.ObservedAt.UtcDateTime:yyyy-MM-dd} {signal.Score:+0.0000;-0.0000;0} × {signal.Certainty:0.00}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
