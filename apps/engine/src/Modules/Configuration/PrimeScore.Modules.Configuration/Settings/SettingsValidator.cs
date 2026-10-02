using System.Globalization;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Configuration.Settings;

/// <summary>
/// Rejects settings the formulas cannot use, each error as <c>path: problem</c>. The CLS-006
/// bound <c>SENSITIVITY_FACTOR ∈ (0, 1]</c> keeps the signal-implied IV non-negative (ADR-0004 §6).
/// </summary>
internal static class SettingsValidator
{
    public static IReadOnlyList<string> Validate(EngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.Weighting.SchemeId))
        {
            errors.Add("weighting.scheme_id: required");
        }

        foreach (var (category, weight) in settings.Weighting.CategoryWeights)
        {
            Category($"weighting.category_weights.{category}", category, errors);
            if (!double.IsFinite(weight) || weight < 0)
            {
                errors.Add($"weighting.category_weights.{category}: must be a finite number ≥ 0");
            }
        }

        if (!settings.Weighting.CategoryWeights.Values.Any(weight => weight > 0))
        {
            errors.Add("weighting.category_weights: at least one weight must be above 0");
        }

        if (!double.IsFinite(settings.BypassPercentile) || settings.BypassPercentile is <= 0 or > 1)
        {
            errors.Add("bypass_percentile: must lie in (0, 1]");
        }

        Spans("corroboration_windows", settings.CorroborationWindows, errors);
        Spans("reporting_intervals", settings.ReportingIntervals, errors);
        Dropout(settings.DropoutSchedule, errors);

        if (settings.Contexts.Count == 0)
        {
            errors.Add("contexts: at least one context is required");
        }

        foreach (var duplicate in settings.Contexts.GroupBy(context => context.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            errors.Add($"contexts.{duplicate.Key}: defined more than once");
        }

        // A reference instrument selects its context in PUT /config/dislocation-threshold, so it must be unique.
        foreach (var shared in settings.Contexts.Where(context => !string.IsNullOrWhiteSpace(context.ReferenceInstrument))
            .GroupBy(context => context.ReferenceInstrument.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            errors.Add($"contexts: {string.Join(" and ", shared.Select(context => context.Name))} share the reference instrument {shared.Key}");
        }

        foreach (var context in settings.Contexts)
        {
            Context(context, settings, errors);
        }

        DeployConditions(settings.DeployConditions, errors);

        if (string.IsNullOrWhiteSpace(settings.Calibration))
        {
            errors.Add("calibration: required (e.g. 'uncalibrated default', 'in-sample')");
        }

        return errors;
    }

    private static void Context(ContextSettings context, EngineSettings settings, List<string> errors)
    {
        var path = $"contexts.{context.Name}";
        // Lower case: the API's context parameter is matched case-insensitively against stored records.
        if (string.IsNullOrWhiteSpace(context.Name) || !context.Name.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '_' or '-'))
        {
            errors.Add($"{path}.name: lower-case letters, digits, '_' or '-' only");
        }

        if (string.IsNullOrWhiteSpace(context.ReferenceInstrument))
        {
            errors.Add($"{path}.reference_instrument: required");
        }

        if (context.Members.Count == 0)
        {
            errors.Add($"{path}.members: at least one expected category is required");
        }

        foreach (var (category, instruments) in context.Members)
        {
            Category($"{path}.members.{category}", category, errors);
            if (instruments.Count == 0 || instruments.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"{path}.members.{category}: at least one instrument, none empty");
            }

            if (!settings.CorroborationWindows.ContainsKey(category) || !settings.ReportingIntervals.ContainsKey(category))
            {
                errors.Add($"{path}.members.{category}: the category needs a corroboration window and a reporting interval");
            }

            if (!settings.Weighting.CategoryWeights.ContainsKey(category))
            {
                errors.Add($"{path}.members.{category}: the weighting scheme has no weight for this category");
            }
        }

        if (!double.IsFinite(context.DislocationThreshold) || context.DislocationThreshold <= 0)
        {
            errors.Add($"{path}.dislocation_threshold: must be a finite number above 0");
        }

        Factor($"{path}.sensitivity.low_vol", context.Sensitivity.LowVol, errors);
        Factor($"{path}.sensitivity.normal", context.Sensitivity.Normal, errors);
        Factor($"{path}.sensitivity.high_vol", context.Sensitivity.HighVol, errors);

        var regime = context.Regime;
        if (regime.Mode == RegimeMode.Percentile)
        {
            if (!(regime.LowBelow > 0 && regime.LowBelow < regime.HighAbove && regime.HighAbove < 1))
            {
                errors.Add($"{path}.regime: percentiles must satisfy 0 < low_below < high_above < 1");
            }
        }
        else if (regime.LowVolUpper is not { } low || regime.HighVolLower is not { } high
            || !double.IsFinite(low) || !double.IsFinite(high) || !(low > 0 && low < high))
        {
            errors.Add($"{path}.regime: level boundaries must satisfy 0 < low_vol_upper < high_vol_lower");
        }
    }

    /// <summary>SRS CLS-006 as resolved: a factor outside (0, 1] could make the signal-implied IV negative.</summary>
    private static void Factor(string path, double factor, List<string> errors)
    {
        if (!double.IsFinite(factor) || factor is <= 0 or > 1)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: {factor} is outside (0, 1]; SENSITIVITY_FACTOR above 1 can make the signal-implied IV negative"));
        }
    }

    private static void Spans(string path, IReadOnlyDictionary<string, WindowSpan> spans, List<string> errors)
    {
        foreach (var (category, span) in spans)
        {
            Category($"{path}.{category}", category, errors);
            var valid = (span.TradingDays, span.Seconds) switch
            {
                ({ } days, null) => days is >= 1 and <= 260,
                (null, { } seconds) => double.IsFinite(seconds) && seconds > 0,
                _ => false,
            };
            if (!valid)
            {
                errors.Add($"{path}.{category}: give either trading_days (1–260) or seconds (> 0), not both");
            }
        }
    }

    private static void Dropout(IReadOnlyList<DropoutTier> tiers, List<string> errors)
    {
        if (tiers.Count == 0 || tiers[^1].BelowSeconds is not null)
        {
            errors.Add("dropout_schedule: needs at least one tier and the last tier must have no upper bound");
        }

        double previous = 0;
        for (var index = 0; index < tiers.Count; index++)
        {
            var tier = tiers[index];
            if (!double.IsFinite(tier.Factor) || tier.Factor is < 0 or > 1)
            {
                errors.Add($"dropout_schedule[{index}].factor: must lie in [0, 1]");
            }

            if (tier.BelowSeconds is { } below && (!double.IsFinite(below) || below <= previous))
            {
                errors.Add($"dropout_schedule[{index}].below_seconds: tiers must be ascending and above 0");
            }

            if (index < tiers.Count - 1 && tier.BelowSeconds is null)
            {
                errors.Add($"dropout_schedule[{index}].below_seconds: only the last tier may be unbounded");
            }

            previous = tier.BelowSeconds ?? previous;
        }
    }

    private static void DeployConditions(IReadOnlyList<DeployCondition>? conditions, List<string> errors)
    {
        if (conditions is null)
        {
            errors.Add("deploy_conditions: required");
            return;
        }

        foreach (var name in DeployConditionNames.Required.Where(name => !conditions.Any(condition => condition.Name == name)))
        {
            errors.Add($"deploy_conditions.{name}: missing");
        }

        foreach (var group in conditions.GroupBy(condition => condition.Name, StringComparer.Ordinal))
        {
            var path = $"deploy_conditions.{group.Key}";
            var condition = group.First();
            if (group.Count() > 1)
            {
                errors.Add($"{path}: defined more than once");
            }

            if (!DeployConditionNames.Configurable.Contains(group.Key, StringComparer.Ordinal))
            {
                errors.Add($"{path}: unknown condition; expected {string.Join(", ", DeployConditionNames.Configurable)}");
                continue;
            }

            if (!DeployConditionNames.Operators.Contains(condition.Operator, StringComparer.Ordinal))
            {
                errors.Add($"{path}.operator: must be one of {string.Join(" ", DeployConditionNames.Operators)}");
            }
            else if (group.Key == DeployConditionNames.LevelPercentileTail && condition.Operator is not ("<=" or "<"))
            {
                // A DEPLOY must record an extreme state (ADR-0008); any other operator would fire on ordinary days.
                errors.Add($"{path}.operator: must be <= or < (a DEPLOY records an extreme state)");
            }

            var (min, max) = group.Key switch
            {
                DeployConditionNames.LevelPercentileTail => (0.0, 0.25),
                DeployConditionNames.CompositeScore or DeployConditionNames.TopSignalCertainty => (0.0, 1.0),
                DeployConditionNames.ContributingSources => (0.0, 4.0),
                _ => (0.0, 260.0),
            };
            if (!double.IsFinite(condition.Threshold) || condition.Threshold < min || condition.Threshold > max)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}.threshold: {condition.Threshold} is outside [{min}, {max}]"));
            }
        }
    }

    private static void Category(string path, string category, List<string> errors)
    {
        if (!SourceCategoryNames.TryParseWireName(category, out _))
        {
            errors.Add($"{path}: '{category}' is not a source category (MARKET_DATA, MACROECONOMIC, GEOPOLITICAL, CROSS_ASSET_FLOW)");
        }
    }
}
