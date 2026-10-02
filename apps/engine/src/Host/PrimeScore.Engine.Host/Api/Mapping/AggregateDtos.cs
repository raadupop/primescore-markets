using PrimeScore.Api.Contracts;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using Kernel = PrimeScore.SharedKernel;

namespace PrimeScore.Engine.Host.Api;

/// <summary>Composite, dislocation and configuration between module views and the contract (brief §5 rule 4).</summary>
internal static class AggregateDtos
{
    /// <remarks>
    /// <c>assessment_count</c> is the number of confirmed assessments the category contributed;
    /// the weighted contributions add up to the score (ADR-0004 §1).
    /// </remarks>
    public static CompositeScore From(CompositeView view) => new()
    {
        Composite_id = view.CompositeId,
        Score = view.Score,
        Weighting_scheme_id = view.WeightingSchemeId,
        Contributing_sources = view.Contributing.Select(category => Category(category.Category)).ToList(),
        Absent_sources = view.Absent.Select(category => Category(category.Category)).ToList(),
        Component_scores = view.Contributing.Select(category => new ComponentScore
        {
            Source_category = Category(category.Category),
            Weighted_contribution = category.WeightedContribution,
            Assessment_count = category.ConfirmedAssessments.Count,
        }).ToList(),
        Computed_at = view.ComputedAt,
        As_of = view.AsOf,
    };

    public static IvDislocation From(DislocationView view) => new()
    {
        Dislocation_id = view.DislocationId,
        Signal_implied_iv = view.SignalImpliedIv,
        Market_observed_iv = view.MarketObservedIv,
        Dislocation_value = view.DislocationValue,
        Dislocation_threshold = view.Threshold,
        Threshold_breached = view.ThresholdBreached,
        Reference_instrument = view.ReferenceInstrument,
        Sensitivity_factor = view.SensitivityFactor,
        Regime = view.Regime,
        Regime_percentile = view.RegimePercentile,
        Computed_at = view.ComputedAt,
    };

    /// <returns>Null when <c>aggregation</c> is not one of the contract's values (an integer the enum converter let through).</returns>
    public static WeightingSettings? ToSettings(WeightingScheme scheme)
    {
        Aggregation? aggregation = scheme.Aggregation switch
        {
            WeightingSchemeAggregation.WEIGHTED_MEAN => Aggregation.WeightedMean,
            WeightingSchemeAggregation.MAX_CONFIRMED_WEIGHTED => Aggregation.MaxConfirmedWeighted,
            _ => null,
        };
        return aggregation is { } known
            ? new WeightingSettings(scheme.Scheme_id?.Trim() ?? "", new Dictionary<string, double>(scheme.Category_weights ?? [], StringComparer.Ordinal), known)
            : null;
    }

    private static SourceCategory Category(Kernel.SourceCategory category) =>
        ApiEnum.TryParse<SourceCategory>(Kernel.SourceCategoryNames.ToWireName(category), out var parsed)
            ? parsed
            : throw new InvalidOperationException($"No contract value for source category {category}.");
}
