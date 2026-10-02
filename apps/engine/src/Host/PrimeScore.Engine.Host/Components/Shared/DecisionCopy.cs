using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;

namespace PrimeScore.Engine.Host.Components.Shared;

/// <summary>
/// Operator-facing explanations of recorded decisions; stored names and values stay unchanged.
/// Since ADR-0008 a decision describes the reference level's state in its own history and asserts
/// no direction. Decisions recorded earlier carry directional labels and came from the refuted
/// composite gate; they are always shown as legacy, never as extreme states.
/// </summary>
internal static class DecisionCopy
{
    /// <summary>The stored <c>scenario</c> values of decisions recorded before ADR-0008.</summary>
    public static bool IsLegacy(string scenario) => scenario is "vol-expansion" or "vol-compression" or "none";

    /// <summary>The outcome in words. The badge names the checks; the state label carries the extremeness.</summary>
    public static string Outcome(DecisionOutcome outcome, string scenario) => (outcome, IsLegacy(scenario)) switch
    {
        (DecisionOutcome.Deploy, false) => "All checks passed",
        (DecisionOutcome.Idle, false) => "Checks not met",
        (DecisionOutcome.Deploy, true) => "Conditions met (legacy gate)",
        _ => "Wait (legacy gate)",
    };

    public static string OutcomeTooltip(DecisionOutcome outcome, string scenario) => IsLegacy(scenario)
        ? "Recorded under the refuted composite gate (ADR-0008), which passed on about half of all days. Kept for reproducibility. No position or order was created."
        : outcome == DecisionOutcome.Deploy
            ? "All checks passed: the reference level is in the extreme tail of its history and the evidence is fresh. A research state, not a direction; no position or order was created."
            : "One or more checks did not pass. No position or order was created.";

    /// <summary>The stored <c>scenario</c> field in words: a state label, or a legacy directional label.</summary>
    public static string Scenario(string scenario) => scenario switch
    {
        "extreme_high" => "Extremely stretched",
        "high" => "Stretched",
        "normal" => "Normal",
        "low" => "Compressed",
        "extreme_low" => "Extremely compressed",
        "unknown" => "Too little history",
        "vol-expansion" => "Above median (legacy directional record)",
        "vol-compression" => "Below median (legacy directional record)",
        "none" => "Neutral composite (legacy directional record)",
        _ => Fmt.Humanize(scenario),
    };

    /// <summary>One line on what the state means and does not mean.</summary>
    public static string StateMeaning(string scenario) => scenario switch
    {
        "extreme_high" or "extreme_low" => "The reference level sits in the extreme tail of its five-year history. In this record levels at either extreme have tended to move back toward the median; that is in sample and not shown to be tradable (the VIX futures test found it priced).",
        "high" => "The reference level is above the high-regime boundary (the 70th percentile by default) but outside the extreme tail.",
        "low" => "The reference level is below the low-regime boundary (the 30th percentile by default) but outside the extreme tail.",
        "normal" => "The reference level sits between the low- and high-regime boundaries of its five-year history.",
        "unknown" => "Fewer than 252 prior closes were available, so the level was not placed.",
        _ => "Recorded under the refuted composite gate, before the state gate existed (ADR-0008). Its directional label is kept only for reproducibility.",
    };

    public static string ConditionName(string name) => name switch
    {
        DeployConditionNames.LevelPercentileTail => "Level in the extreme tail",
        DeployConditionNames.Dislocation => "Scenario gap (legacy)",
        DeployConditionNames.CompositeScore => "Combined signal strength",
        DeployConditionNames.ContributingSources => "Contributing source categories",
        DeployConditionNames.TopSignalCertainty => "Strongest signal confidence",
        DeployConditionNames.NewestObservationAge => "Newest observation age",
        DeployConditionNames.Cooldown => "Active waiting periods (legacy)",
        _ => Fmt.Humanize(name),
    };

    public static string ConditionMeaning(string name) => name switch
    {
        DeployConditionNames.LevelPercentileTail => "How far the reference level sits into either tail of its five-year history: the smaller of its percentile and one minus it. A rank, not a forecast. A 5% tail would fire on one day in ten for a stationary level; VIX fired on 13.5% of days in the 2016–2026 replay.",
        DeployConditionNames.Dislocation => "Legacy check: the observed level multiplied by a rank of the observed level. It carried no independent information and no longer gates decisions.",
        DeployConditionNames.CompositeScore => "Magnitude of the combined signal score on a 0–1 scale: how far the strongest confirmed input sits from its own median, as a rank. A threshold on it fires on a fixed share of days; optional since ADR-0008.",
        DeployConditionNames.ContributingSources => "Number of data categories with confirmed signals; several instruments in one category count as one.",
        DeployConditionNames.TopSignalCertainty => "Confidence assigned to the strongest contributing signal from reference history and recency. It is not a probability of a profitable trade.",
        DeployConditionNames.NewestObservationAge => "NYSE trading days between the newest contributing observation and this decision, not its age today.",
        DeployConditionNames.Cooldown => "Legacy check that always recorded zero; no longer evaluated.",
        _ => "Recorded condition evaluated against the configured threshold.",
    };

    public static string ConditionActual(ConditionEvaluation condition) => condition.Name switch
    {
        DeployConditionNames.NewestObservationAge when condition.Actual >= 9999 => "No contributing observation",
        DeployConditionNames.LevelPercentileTail when condition.Actual >= 1 => "Too little level history",
        _ => ConditionValue(condition.Name, condition.Actual),
    };

    public static string ConditionRequirement(ConditionEvaluation condition) =>
        Requirement(condition.Name, condition.Operator, condition.Required);

    public static string Requirement(string name, string comparison, double value) =>
        $"{Comparison(comparison)} {ConditionValue(name, value)}";

    public static string Summary(DecisionView decision)
    {
        var unmet = decision.Conditions.Where(condition => !condition.Passed).Select(condition => ConditionName(condition.Name)).ToArray();
        if (IsLegacy(decision.Scenario))
        {
            return unmet.Length == 0
                ? "Recorded under the refuted composite gate (ADR-0008), which passed on about half of all days. It does not mean the level was extreme."
                : $"Recorded under the refuted composite gate (ADR-0008). {string.Join(", ", unmet)} did not meet the requirements of that gate.";
        }

        return unmet.Length == 0
            ? $"All {decision.Conditions.Count} checks passed: the reference level is in the extreme tail of its history and the evidence is fresh. This describes the state; it is not a directional forecast."
            : $"{string.Join(", ", unmet)} {(unmet.Length == 1 ? "does" : "do")} not meet the configured requirements.";
    }

    public static string ConditionValue(string name, double value) => name switch
    {
        DeployConditionNames.LevelPercentileTail => Fmt.Number(value * 100, 2) + "% from the nearer end",
        DeployConditionNames.Dislocation => $"{Fmt.Number(value, 2)} index points",
        DeployConditionNames.CompositeScore => Fmt.Number(value, 4),
        DeployConditionNames.TopSignalCertainty => Fmt.Percent(value),
        DeployConditionNames.ContributingSources => $"{Fmt.Number(value, 0)} {(value == 1 ? "category" : "categories")}",
        DeployConditionNames.NewestObservationAge => $"{Fmt.Number(value, 0)} {(value == 1 ? "trading day" : "trading days")}",
        DeployConditionNames.Cooldown => Fmt.Number(value, 0),
        _ => Fmt.Number(value, 4),
    };

    /// <summary>"99.9th percentile of 1,260 prior closes"; "Not recorded" for legacy decisions, which stored no percentile.</summary>
    public static string Percentile(double? percentile, string scenario, int? history = null) => percentile switch
    {
        { } p => $"{Fmt.Ordinal(p * 100)} percentile" + (history is { } n ? $" of {Fmt.Count(n)} prior closes" : " of prior closes"),
        null when IsLegacy(scenario) => "Not recorded (before ADR-0008)",
        _ => "No level history",
    };

    private static string Comparison(string comparison) => comparison switch
    {
        ">=" => "At least",
        ">" => "More than",
        "<=" => "At most",
        "<" => "Less than",
        "==" => "Exactly",
        _ => comparison,
    };
}
