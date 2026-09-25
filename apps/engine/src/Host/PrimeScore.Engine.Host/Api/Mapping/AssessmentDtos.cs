using PrimeScore.Api.Contracts;
using PrimeScore.Modules.Classification.Contracts;

namespace PrimeScore.Engine.Host.Api;

/// <summary>Module assessment views to the contract's <c>SignalAssessment</c> (brief §5 rule 4: mapping lives in the host).</summary>
internal static class AssessmentDtos
{
    public static SignalAssessment From(AssessmentView view) => new()
    {
        Assessment_id = view.AssessmentId,
        Signal_id = view.SignalId,
        Score = view.Score ?? 0,
        Score_type = ApiEnum.TryParse<ScoreType>(view.ScoreType ?? "", out var scoreType) ? scoreType : ScoreType.ANOMALY_DETECTION,
        Certainty = view.Certainty ?? 0,
        History_sufficiency = view.HistorySufficiency,
        Temporal_relevance = view.TemporalRelevance,
        Is_fallback = view.IsFallback,
        Staleness_seconds = view.IsFallback ? view.StalenessSeconds : null,
        Event_taxonomy = view.EventTaxonomy is { } taxonomy && ApiEnum.TryParse<EventTaxonomy>(taxonomy, out var parsed) ? parsed : null,
        Classification_method = ApiEnum.TryParse<ClassificationMethod>(view.ClassificationMethod ?? "", out var method) ? method : ClassificationMethod.RULE_BASED,
        Assessed_at = view.AssessedAt,
    };
}
