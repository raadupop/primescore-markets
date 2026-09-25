namespace PrimeScore.Modules.Classification.Aggregation;

/// <param name="PercentileMode">True: regime from the level's ECDF percentile; false: from level boundaries.</param>
internal sealed record DislocationParameters(
    double Threshold,
    double LowFactor,
    double NormalFactor,
    double HighFactor,
    bool PercentileMode,
    double LowBelow,
    double HighAbove,
    double? LowVolUpper,
    double? HighVolLower);

/// <param name="RegimePercentile">Share of the previous levels at or below the observed level; null in level mode or without history.</param>
internal sealed record DislocationResult(
    double MarketObservedIv,
    string Regime,
    double? RegimePercentile,
    int RegimeHistory,
    double SensitivityFactor,
    double SignalImpliedIv,
    double DislocationValue,
    double Threshold,
    bool ThresholdBreached);

/// <summary>
/// SRS CLS-006 as resolved in ADR-0004 §6:
/// <c>SignalImpliedIV = IV × (1 + CompositeScore × k)</c>, <c>Dislocation = SignalImpliedIV − IV</c>,
/// with <c>k</c> chosen by the reference instrument's regime and the threshold compared
/// against the dislocation's magnitude.
/// </summary>
internal static class DislocationCalculator
{
    public const string Low = "low_vol";
    public const string Normal = "normal";
    public const string High = "high_vol";

    /// <param name="history">The reference instrument's previous levels (at most <c>N_L</c>), excluding the observed one.</param>
    public static DislocationResult Compute(double composite, double observedIv, IReadOnlyList<double> history, DislocationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(parameters);
        double? percentile = null;
        string regime;
        if (parameters.PercentileMode)
        {
            if (history.Count == 0)
            {
                regime = Normal;
            }
            else
            {
                // Right-continuous ECDF, as SRS §3 defines it for severity.
                percentile = history.Count(level => level <= observedIv) / (double)history.Count;
                regime = percentile < parameters.LowBelow ? Low : percentile > parameters.HighAbove ? High : Normal;
            }
        }
        else
        {
            regime = observedIv < parameters.LowVolUpper ? Low : observedIv > parameters.HighVolLower ? High : Normal;
        }

        var factor = regime switch
        {
            Low => parameters.LowFactor,
            High => parameters.HighFactor,
            _ => parameters.NormalFactor,
        };
        var signalImplied = observedIv * (1 + (composite * factor));
        var dislocation = signalImplied - observedIv;
        return new DislocationResult(
            observedIv, regime, percentile, history.Count, factor, signalImplied, dislocation, parameters.Threshold,
            Math.Abs(dislocation) >= parameters.Threshold);
    }
}
