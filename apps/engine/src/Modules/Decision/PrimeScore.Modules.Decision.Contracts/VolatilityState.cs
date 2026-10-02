namespace PrimeScore.Modules.Decision.Contracts;

/// <summary>
/// Where the reference level sits in its own history, in words (ADR-0008). Derived from the
/// dislocation's percentile, its history length and the configured tail condition; a description
/// of the state, never a forecast of the next move. Stored in the decision's <c>scenario</c> field,
/// which held a directional label (<c>vol-expansion</c>, <c>vol-compression</c>) before ADR-0008.
/// Published so other modules label a level with the same rule (the daily brief's cross-asset grid).
/// </summary>
public static class VolatilityState
{
    public const string ExtremeLow = "extreme_low";
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string ExtremeHigh = "extreme_high";

    /// <summary>Too little level history to place the level: the percentile is not used.</summary>
    public const string Unknown = "unknown";

    /// <summary>The default tail when no <c>level_percentile_tail</c> condition is configured.</summary>
    public const double DefaultTail = 0.05;

    /// <summary>
    /// Prior closes needed before a percentile counts: one trading year. With a handful of closes almost
    /// any new high or low sits at percentile 0 or 1, so the tail gate would fire on most days.
    /// </summary>
    public const int MinimumHistory = 252;

    /// <summary>
    /// <c>min(p, 1 − p)</c>: distance into the nearer tail; null without a percentile or with fewer than
    /// <see cref="MinimumHistory"/> prior closes. Rounded to 12 decimals so an exact boundary (1 − 0.95
    /// against a 0.05 threshold) is not lost to binary rounding; ECDF percentiles have denominators far
    /// below 10¹².
    /// </summary>
    public static double? Tail(double? percentile, int history) =>
        percentile is { } p && history >= MinimumHistory ? Math.Round(Math.Min(p, 1 - p), 12) : null;

    /// <param name="regime">The dislocation's regime (<c>low_vol</c>, <c>normal</c>, <c>high_vol</c>).</param>
    /// <param name="tailOperator"><c>&lt;=</c> or <c>&lt;</c>, as validated for the tail condition.</param>
    /// <param name="tailThreshold">Tail distance that counts as extreme.</param>
    public static string Label(double? percentile, int history, string regime, string tailOperator, double tailThreshold)
    {
        if (Tail(percentile, history) is not { } tail)
        {
            return Unknown;
        }

        if (tailOperator == "<" ? tail < tailThreshold : tail <= tailThreshold)
        {
            return percentile < 0.5 ? ExtremeLow : ExtremeHigh;
        }

        return regime switch
        {
            "low_vol" => Low,
            "high_vol" => High,
            _ => Normal,
        };
    }

    public static bool IsExtreme(string state) => state is ExtremeLow or ExtremeHigh;
}
