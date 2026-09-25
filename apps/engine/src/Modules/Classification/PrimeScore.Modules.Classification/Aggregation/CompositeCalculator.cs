using System.Globalization;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Classification.Aggregation;

/// <summary>An available assessment (real or CLS-004 fallback) of one signal, as the composite sees it.</summary>
internal sealed record AssessmentInput(
    Guid AssessmentId,
    Guid SignalId,
    SourceCategory Category,
    string Instrument,
    DateTimeOffset ObservedAt,
    double Score,
    double Certainty,
    bool IsFallback)
{
    /// <summary>Signed <c>severity × certainty</c>.</summary>
    public double Conviction => Score * Certainty;
}

/// <summary>A signal without an assessment of its own, with the reason, used to explain an absent category.</summary>
internal sealed record OutcomeInput(SourceCategory Category, DateTimeOffset ObservedAt, string Reason);

/// <summary>A duration in trading days on the New York calendar, or in seconds.</summary>
internal sealed record Span(int? TradingDays, double? Seconds);

/// <summary>Everything the composite formula reads from configuration (ADR-0004).</summary>
/// <param name="NetMax">True for MAX_CONFIRMED_WEIGHTED (net max), false for WEIGHTED_MEAN (legacy mean).</param>
/// <param name="Dropout">Tiers of <c>d_c(Δt)</c>, ascending; a null bound is the last tier.</param>
internal sealed record CompositeParameters(
    string SchemeId,
    IReadOnlyDictionary<SourceCategory, double> Weights,
    bool NetMax,
    double BypassPercentile,
    IReadOnlyDictionary<SourceCategory, Span> Windows,
    IReadOnlyDictionary<SourceCategory, Span> ReportingIntervals,
    IReadOnlyList<(double? BelowSeconds, double Factor)> Dropout,
    IReadOnlyList<SourceCategory> Expected);

/// <param name="WeightedContribution"><c>w_c · d_c · s_c / Σ w</c>: the category's share of the composite.</param>
/// <param name="StalenessSeconds"><c>Δt_c</c>: how long the category's newest observation is overdue (0 when fresh).</param>
internal sealed record CategoryResult(
    SourceCategory Category,
    double Weight,
    double Discount,
    double StalenessSeconds,
    double NetConviction,
    double WeightedContribution,
    Guid? MaxPositive,
    Guid? MaxNegative,
    IReadOnlyList<Guid> Confirmed,
    IReadOnlyList<Guid> Unconfirmed);

internal sealed record AbsentCategory(SourceCategory Category, string Reason);

internal sealed record CompositeResult(double Score, IReadOnlyList<CategoryResult> Contributing, IReadOnlyList<AbsentCategory> Absent);

/// <summary>
/// SRS CLS-002 as resolved in ADR-0004:
/// <c>CompositeScore = Σ_{c∈P} w_c · d_c(Δt_c) · s_c / Σ_{c∈P} w_c</c>, with
/// <c>s_c = max⁺_c − max⁻_c</c> over confirmed assessments. Pure: the caller supplies the
/// latest outcome of each signal of the context's member instruments observed at or before
/// <c>at</c>.
/// </summary>
internal static class CompositeCalculator
{
    public static CompositeResult Compute(
        DateTimeOffset at,
        CompositeParameters parameters,
        IReadOnlyList<AssessmentInput> assessments,
        IReadOnlyList<OutcomeInput> unavailable)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(unavailable);
        var contributing = new List<CategoryResult>();
        var absent = new List<AbsentCategory>();
        foreach (var category in parameters.Expected)
        {
            var window = parameters.Windows[category];
            // Certainty 0 (unknown indicator, insufficient history) carries no conviction and confirms nothing.
            var live = assessments
                .Where(assessment => assessment.Category == category && assessment.Certainty > 0 && InWindow(assessment.ObservedAt, at, window))
                .ToArray();
            var confirmed = live.Where(assessment => IsConfirmed(assessment, live, parameters.BypassPercentile)).ToArray();
            if (confirmed.Length == 0)
            {
                var uncertain = assessments.Count(assessment => assessment.Category == category && assessment.Certainty <= 0 && InWindow(assessment.ObservedAt, at, window));
                absent.Add(new AbsentCategory(category, AbsentReason(category, live.Length, uncertain, unavailable, at, window)));
                continue;
            }

            var positive = confirmed.Where(assessment => assessment.Conviction > 0).MaxBy(assessment => assessment.Conviction);
            var negative = confirmed.Where(assessment => assessment.Conviction < 0).MinBy(assessment => assessment.Conviction);
            var net = parameters.NetMax
                ? (positive?.Conviction ?? 0) - Math.Abs(negative?.Conviction ?? 0)
                : confirmed.Average(assessment => assessment.Conviction);
            var staleness = Staleness(at, live.Max(assessment => assessment.ObservedAt), parameters.ReportingIntervals[category]);
            contributing.Add(new CategoryResult(
                category,
                parameters.Weights.GetValueOrDefault(category),
                Discount(staleness, parameters.Dropout),
                staleness,
                net,
                0,
                positive?.AssessmentId,
                negative?.AssessmentId,
                confirmed.Select(assessment => assessment.AssessmentId).ToArray(),
                live.Except(confirmed).Select(assessment => assessment.AssessmentId).ToArray()));
        }

        var totalWeight = contributing.Sum(category => category.Weight);
        if (totalWeight <= 0)
        {
            // No expected category with a confirmed assessment (or all weights zero): no conviction.
            return new CompositeResult(0, contributing.Select(category => category with { WeightedContribution = 0 }).ToArray(), absent);
        }

        var weighted = contributing
            .Select(category => category with { WeightedContribution = category.Weight * category.Discount * category.NetConviction / totalWeight })
            .ToArray();
        var score = Math.Clamp(weighted.Sum(category => category.WeightedContribution), -1.0, 1.0);
        return new CompositeResult(score, weighted, absent);
    }

    /// <summary>
    /// At or before <paramref name="at"/> and not before the window's start. A window of n
    /// trading days holds the evaluation day and the n − 1 NYSE trading days before it (ADR-0004
    /// §4), so the close before a holiday still corroborates the first close after it, and an
    /// observation stamped on a weekend or holiday is not kept longer than a trading day's.
    /// </summary>
    public static bool InWindow(DateTimeOffset observedAt, DateTimeOffset at, Span window) =>
        observedAt <= at && observedAt >= WindowStart(at, window);

    /// <summary>The earliest instant inside the window that ends at <paramref name="at"/>.</summary>
    public static DateTimeOffset WindowStart(DateTimeOffset at, Span window) =>
        window.TradingDays is { } days
            ? MarketTime.AtNewYork(MarketTime.AddTradingDays(MarketTime.NewYorkDate(at), -(days - 1)), TimeOnly.MinValue)
            : at.AddSeconds(-window.Seconds!.Value);

    /// <summary>
    /// <c>Δt_c</c>: time past the moment the category's next observation was due, or 0. A
    /// trading-day interval is due at the same New York wall-clock time that many trading
    /// days after the newest observation.
    /// </summary>
    public static double Staleness(DateTimeOffset at, DateTimeOffset newest, Span interval)
    {
        DateTimeOffset due;
        if (interval.TradingDays is { } days)
        {
            var local = TimeZoneInfo.ConvertTime(newest, MarketTime.NewYork);
            due = MarketTime.AtNewYork(MarketTime.AddTradingDays(DateOnly.FromDateTime(local.DateTime), days), TimeOnly.FromDateTime(local.DateTime));
        }
        else
        {
            due = newest.AddSeconds(interval.Seconds!.Value);
        }

        return Math.Max(0, (at - due).TotalSeconds);
    }

    /// <summary><c>d_c(Δt)</c>: 1 when fresh, otherwise the first tier whose bound exceeds Δt.</summary>
    public static double Discount(double stalenessSeconds, IReadOnlyList<(double? BelowSeconds, double Factor)> tiers)
    {
        if (stalenessSeconds <= 0)
        {
            return 1.0;
        }

        foreach (var (below, factor) in tiers)
        {
            if (below is null || stalenessSeconds < below)
            {
                return factor;
            }
        }

        return tiers.Count > 0 ? tiers[^1].Factor : 1.0;
    }

    /// <summary>
    /// Confirmed by another non-fallback assessment of a different signal in the same window,
    /// or by the high-conviction bypass (ADR-0004 §4).
    /// </summary>
    private static bool IsConfirmed(AssessmentInput assessment, IReadOnlyList<AssessmentInput> live, double bypass) =>
        Math.Abs(assessment.Score) >= bypass
        || live.Any(other => other.SignalId != assessment.SignalId && !other.IsFallback);

    private static string AbsentReason(SourceCategory category, int unconfirmed, int uncertain, IReadOnlyList<OutcomeInput> unavailable, DateTimeOffset at, Span window)
    {
        if (unconfirmed > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{unconfirmed} unconfirmed assessment(s) in the window; none corroborated or above the bypass percentile");
        }

        if (uncertain > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{uncertain} assessment(s) in the window with certainty 0 (insufficient history or unknown indicator)");
        }

        var reasons = unavailable
            .Where(outcome => outcome.Category == category && InWindow(outcome.ObservedAt, at, window))
            .GroupBy(outcome => outcome.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key} ({group.Count()} signal(s))"))
            .ToArray();
        return reasons.Length > 0 ? string.Join("; ", reasons) : "no classified signal in the window";
    }
}
