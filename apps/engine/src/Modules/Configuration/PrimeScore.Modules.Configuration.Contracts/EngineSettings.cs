using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Configuration.Contracts;

/// <summary>
/// Every parameter the SRS calls configurable, as one versioned value (brief §6, NFR-003).
/// Category keys are source-category wire names (<c>MARKET_DATA</c>, ...). A computed record
/// names the version it used; replay may substitute a whole value for its run.
/// </summary>
/// <param name="BypassPercentile">CLS-002 high-conviction bypass: <c>|severity| ≥ p</c> confirms alone.</param>
/// <param name="CorroborationWindows">Per category: how far back assessments take part and corroborate (ADR-0004 §4).</param>
/// <param name="ReportingIntervals">Per category: how long after its newest observation a category is still fresh (ADR-0004 §5).</param>
/// <param name="DropoutSchedule">CLS-002 <c>d_c(Δt)</c> tiers, ascending; the last has no upper bound.</param>
/// <param name="Calibration">How the values were obtained: <c>uncalibrated default</c>, <c>in-sample</c>, ...</param>
public sealed record EngineSettings(
    WeightingSettings Weighting,
    double BypassPercentile,
    IReadOnlyDictionary<string, WindowSpan> CorroborationWindows,
    IReadOnlyDictionary<string, WindowSpan> ReportingIntervals,
    IReadOnlyList<DropoutTier> DropoutSchedule,
    IReadOnlyList<ContextSettings> Contexts,
    string Calibration)
{
    public ContextSettings? Context(string name) =>
        Contexts.FirstOrDefault(context => string.Equals(context.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The contract's <c>WeightingScheme</c> (SRS CLS-002).</summary>
public sealed record WeightingSettings(string SchemeId, IReadOnlyDictionary<string, double> CategoryWeights, Aggregation Aggregation);

public enum Aggregation
{
    /// <summary>Net max of confirmed <c>severity × certainty</c> per category (ADR-0004 §2).</summary>
    MaxConfirmedWeighted,

    /// <summary>Mean of confirmed <c>severity × certainty</c> per category (v2.2.1 legacy).</summary>
    WeightedMean,
}

/// <summary>A duration in trading days on the New York calendar, or in seconds.</summary>
public sealed record WindowSpan(int? TradingDays, double? Seconds)
{
    public static WindowSpan Days(int tradingDays) => new(tradingDays, null);

    public static WindowSpan Of(double seconds) => new(null, seconds);

    public override string ToString() => TradingDays is { } days ? $"{days} trading day(s)" : $"{Seconds} s";
}

/// <param name="BelowSeconds">Upper bound of the tier (exclusive); null for the last tier.</param>
public sealed record DropoutTier(double? BelowSeconds, double Factor);

/// <summary>A volatility context (brief §6): reference instrument, expected categories and their instruments.</summary>
/// <param name="Members">Expected category wire name → instruments that feed it.</param>
/// <param name="DislocationThreshold">Index points; the dislocation's magnitude is compared (ADR-0004 §6).</param>
public sealed record ContextSettings(
    string Name,
    string ReferenceInstrument,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Members,
    double DislocationThreshold,
    SensitivityMap Sensitivity,
    RegimeRule Regime)
{
    public bool Includes(SourceCategory category, string instrument) =>
        Members.TryGetValue(category.ToWireName(), out var instruments)
        && instruments.Contains(instrument, StringComparer.Ordinal);
}

/// <summary>SENSITIVITY_FACTOR per regime (SRS CLS-006); each in (0, 1].</summary>
public sealed record SensitivityMap(double LowVol, double Normal, double HighVol);

public enum RegimeMode
{
    /// <summary>ECDF percentile of the reference level within its previous <c>N_L</c> observations.</summary>
    Percentile,

    /// <summary>Fixed level boundaries (the contract's <c>regime_boundaries</c>).</summary>
    Level,
}

/// <param name="LowBelow">Percentile mode: low regime below this percentile.</param>
/// <param name="HighAbove">Percentile mode: high regime above this percentile.</param>
/// <param name="LowVolUpper">Level mode: low regime below this level.</param>
/// <param name="HighVolLower">Level mode: high regime above this level.</param>
public sealed record RegimeRule(RegimeMode Mode, double LowBelow, double HighAbove, double? LowVolUpper, double? HighVolLower);

/// <summary>One stored version of the settings.</summary>
/// <param name="Changes">What differs from the previous version, one <c>path: old → new</c> line each.</param>
public sealed record SettingsVersion(
    ConfigVersion Version,
    long LedgerSequence,
    DateTimeOffset RecordedAt,
    string ChangedBy,
    string Reason,
    EngineSettings Settings,
    IReadOnlyList<string> Changes);
