using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;

namespace PrimeScore.Engine.Host.Components.Shared;

/// <summary>Operator-facing explanations of recorded decisions; stored names and values stay unchanged.</summary>
internal static class DecisionCopy
{
    public static string Outcome(DecisionOutcome outcome) => outcome == DecisionOutcome.Deploy ? "Conditions met" : "Wait";

    public static string Scenario(string scenario) => scenario switch
    {
        "vol-expansion" => "Higher volatility",
        "vol-compression" => "Lower volatility",
        "none" => "No directional scenario",
        _ => Fmt.Humanize(scenario),
    };

    public static string ConditionName(string name) => name switch
    {
        DeployConditionNames.Dislocation => "Scenario gap",
        DeployConditionNames.CompositeScore => "Combined signal strength",
        DeployConditionNames.ContributingSources => "Contributing source categories",
        DeployConditionNames.TopSignalCertainty => "Strongest signal confidence",
        DeployConditionNames.NewestObservationAge => "Newest observation age",
        DeployConditionNames.Cooldown => "Active waiting periods",
        _ => Fmt.Humanize(name),
    };

    public static string ConditionMeaning(string name) => name switch
    {
        DeployConditionNames.Dislocation => "Distance between the model scenario and the observed volatility index, regardless of direction.",
        DeployConditionNames.CompositeScore => "Magnitude of the combined directional score on a 0–1 scale. The sign determines the scenario direction.",
        DeployConditionNames.ContributingSources => "Number of data categories with confirmed signals; several instruments in one category count as one.",
        DeployConditionNames.TopSignalCertainty => "Confidence assigned to the strongest contributing signal from reference history and recency. It is not a probability of a profitable trade.",
        DeployConditionNames.NewestObservationAge => "NYSE trading days between the newest contributing observation and this decision, not its age today.",
        DeployConditionNames.Cooldown => "Waiting-period controls are not implemented in this version. This check always records zero.",
        _ => "Recorded condition evaluated against the configured threshold.",
    };

    public static string ConditionActual(ConditionEvaluation condition) =>
        condition.Name == DeployConditionNames.NewestObservationAge && condition.Actual >= 9999
            ? "No contributing observation"
            : ConditionValue(condition.Name, condition.Actual);

    public static string ConditionRequirement(ConditionEvaluation condition) =>
        Requirement(condition.Name, condition.Operator, condition.Required);

    public static string Requirement(string name, string comparison, double value) =>
        $"{Comparison(comparison)} {ConditionValue(name, value)}";

    public static string Summary(DecisionView decision)
    {
        var unmet = decision.Conditions.Where(condition => !condition.Passed).Select(condition => ConditionName(condition.Name)).ToArray();
        return unmet.Length == 0
            ? $"All {decision.Conditions.Count} checks passed for this recorded scenario. Review the evidence and model assumptions below."
            : $"{string.Join(", ", unmet)} {(unmet.Length == 1 ? "does" : "do")} not meet the configured requirements.";
    }

    public static string ConditionValue(string name, double value) => name switch
    {
        DeployConditionNames.Dislocation => $"{Fmt.Number(value, 2)} index points",
        DeployConditionNames.CompositeScore => Fmt.Number(value, 4),
        DeployConditionNames.TopSignalCertainty => Fmt.Percent(value),
        DeployConditionNames.ContributingSources => $"{Fmt.Number(value, 0)} {(value == 1 ? "category" : "categories")}",
        DeployConditionNames.NewestObservationAge => $"{Fmt.Number(value, 0)} {(value == 1 ? "trading day" : "trading days")}",
        DeployConditionNames.Cooldown => Fmt.Number(value, 0),
        _ => Fmt.Number(value, 4),
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
