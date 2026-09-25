using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Classification.Contracts;

/// <summary>
/// Classify every recorded signal that has no final assessment yet, in observation order,
/// each against the history observed strictly before it (SRS CLS-001, ANA-001).
/// </summary>
public sealed record ClassifyPendingSignals : ICommand<ClassifyPendingAck>;

/// <param name="Unavailable">Signals recorded as not classifiable (awaiting consensus, route not implemented, classifier failure).</param>
/// <param name="Composites">Composites recorded after the pass (each with its dislocation when a reference level exists).</param>
public sealed record ClassifyPendingAck(int Classified, int Fallbacks, int Unavailable, int Remaining, int Composites = 0) : ICommandAck;

/// <summary>Why a signal has no assessment of its own.</summary>
public enum UnavailableReason
{
    /// <summary>A macro print without a sourced consensus row; never classified against an invented expectation.</summary>
    AwaitingConsensus,

    /// <summary>The classifier answered 501 for this category (cross-asset and geopolitical routes in v1).</summary>
    RouteNotImplemented,

    /// <summary>No answer from the classifier; retried on later runs.</summary>
    ClassifierUnreachable,

    /// <summary>The classifier answered with a body that failed validation (SRS CLS-004).</summary>
    InvalidResponse,

    /// <summary>The classifier rejected the request as malformed (its own 400 or 422).</summary>
    ClassifierRejected,

    /// <summary>A macro print whose instrument is not in the indicator registry; it cannot be matched to a consensus row.</summary>
    UnknownIndicator,
}

/// <summary>
/// The latest outcome of each signal, newest observation first, optionally for one signal or
/// as of a point in time (observation time of the signal).
/// </summary>
/// <param name="AvailableOnly">Only signals whose latest outcome is an assessment (real or fallback).</param>
/// <param name="Take">At most this many; null returns every match.</param>
public sealed record GetAssessments(
    Guid? SignalId = null,
    DateTimeOffset? AsOf = null,
    string? Instrument = null,
    int? Take = 500,
    bool AvailableOnly = false) : IQuery<IReadOnlyList<AssessmentView>>;

/// <summary>The latest outcome for each of the given signals (UI tape).</summary>
public sealed record GetSignalOutcomes(IReadOnlyList<Guid> SignalIds) : IQuery<IReadOnlyDictionary<Guid, AssessmentView>>;

/// <param name="Score">Signed severity in [-1, 1] (statistical classification, not a forecast); null when unavailable.</param>
/// <param name="IsFallback">Last-known-good assessment reused after a classifier failure (SRS CLS-004).</param>
/// <param name="StalenessSeconds">Age of the reused assessment when it was reused.</param>
/// <param name="FallbackOf">For a fallback, the assessment whose values were reused.</param>
/// <param name="Flags">CLS-009 and fit markers from the classifier, e.g. <c>window_degenerate</c>, <c>unknown_indicator</c>, <c>fit_rejected</c>.</param>
public sealed record AssessmentView(
    Guid AssessmentId,
    Guid SignalId,
    long LedgerSequence,
    CorrelationId CorrelationId,
    SourceCategory Category,
    string Instrument,
    string Variant,
    DateTimeOffset ObservedAt,
    DateTimeOffset AssessedAt,
    bool Available,
    UnavailableReason? Reason,
    string? Detail,
    double? Score,
    string? ScoreType,
    double? Certainty,
    double? HistorySufficiency,
    double? TemporalRelevance,
    string? ClassificationMethod,
    string? EventTaxonomy,
    bool IsFallback,
    double? StalenessSeconds,
    Guid? FallbackOf,
    IReadOnlyList<string> Flags,
    string? ReasoningTrace,
    string? ComputedMetrics,
    int ReferenceWindowLength,
    ConsensusUsed? Consensus);

/// <summary>The sourced expectation a macro print was compared with.</summary>
public sealed record ConsensusUsed(double Value, string Source, string Url, DateTimeOffset RetrievedAt);
