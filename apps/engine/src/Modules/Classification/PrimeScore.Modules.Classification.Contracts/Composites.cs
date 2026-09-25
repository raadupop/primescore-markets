using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Classification.Contracts;

/// <summary>
/// The newest composite recorded for a context whose observation time is at or before
/// <paramref name="AsOf"/> (SRS CLS-002, SIG-004); null when none exists.
/// </summary>
public sealed record GetComposite(string Context, DateTimeOffset? AsOf = null) : IQuery<CompositeView?>;

/// <summary>The newest dislocation for a context at or before <paramref name="AsOf"/> (SRS CLS-006); null when none exists.</summary>
public sealed record GetDislocation(string Context, DateTimeOffset? AsOf = null) : IQuery<DislocationView?>;

/// <summary>
/// One point per New York date for a context, oldest first: the last composite recorded for
/// that date and its dislocation. <paramref name="From"/> and <paramref name="To"/> bound the
/// observation time.
/// </summary>
public sealed record GetCompositeHistory(string Context, DateTimeOffset? From = null, DateTimeOffset? To = null) : IQuery<IReadOnlyList<DailyAggregate>>;

/// <summary>
/// Dislocations recorded after a ledger sequence, in recording order, each with its composite
/// and the assessments that composite confirmed: the decision stage's input (brief §6).
/// </summary>
public sealed record GetAggregatesAfter(long AfterSequence, int Take) : IQuery<IReadOnlyList<AggregateRecord>>;

public sealed record AggregateRecord(CompositeView Composite, DislocationView Dislocation, IReadOnlyList<ConfirmedAssessment> Confirmed);

/// <summary>An assessment that entered a composite's net conviction.</summary>
public sealed record ConfirmedAssessment(
    Guid AssessmentId,
    Guid SignalId,
    long LedgerSequence,
    SourceCategory Category,
    string Instrument,
    DateTimeOffset ObservedAt,
    double Score,
    double Certainty,
    bool IsFallback)
{
    /// <summary>Signed severity × certainty.</summary>
    public double Conviction => Score * Certainty;
}

/// <summary>Published after each classification pass, once its composites and dislocations are recorded.</summary>
public sealed record AggregatesRecorded(int Composites, CorrelationId CorrelationId) : IIntegrationEvent;

/// <summary>The contexts that have at least one composite, with their newest observation time.</summary>
public sealed record GetAggregateContexts : IQuery<IReadOnlyList<AggregateContextView>>;

public sealed record AggregateContextView(string Context, int Composites, DateTimeOffset NewestAsOf);

/// <param name="AsOf">Observation time of the signal whose assessment triggered the computation.</param>
public sealed record CompositeView(
    Guid CompositeId,
    long LedgerSequence,
    CorrelationId CorrelationId,
    ConfigVersion ConfigVersion,
    string Context,
    double Score,
    string WeightingSchemeId,
    string Aggregation,
    IReadOnlyList<CategoryContribution> Contributing,
    IReadOnlyList<AbsentCategoryView> Absent,
    Guid TriggerSignalId,
    Guid TriggerAssessmentId,
    DateTimeOffset AsOf,
    DateTimeOffset ComputedAt);

/// <param name="WeightedContribution"><c>w_c · d_c · s_c / Σ w</c>; the contributions add up to the composite.</param>
/// <param name="NetConviction"><c>s_c = max⁺ − max⁻</c> (or the mean under WEIGHTED_MEAN).</param>
/// <param name="Discount"><c>d_c(Δt_c)</c>; 1 when the category is fresh.</param>
public sealed record CategoryContribution(
    SourceCategory Category,
    double Weight,
    double Discount,
    double StalenessSeconds,
    double NetConviction,
    double WeightedContribution,
    Guid? MaxPositiveAssessment,
    Guid? MaxNegativeAssessment,
    IReadOnlyList<Guid> ConfirmedAssessments,
    IReadOnlyList<Guid> UnconfirmedAssessments);

public sealed record AbsentCategoryView(SourceCategory Category, string Reason);

/// <param name="RegimePercentile">Share of the reference instrument's previous levels at or below the observed one; null in level mode.</param>
public sealed record DislocationView(
    Guid DislocationId,
    long LedgerSequence,
    CorrelationId CorrelationId,
    ConfigVersion ConfigVersion,
    string Context,
    Guid CompositeId,
    double CompositeScore,
    string ReferenceInstrument,
    double MarketObservedIv,
    DateTimeOffset IvObservedAt,
    Guid IvSignalId,
    string Regime,
    double? RegimePercentile,
    int RegimeHistory,
    double SensitivityFactor,
    double SignalImpliedIv,
    double DislocationValue,
    double Threshold,
    bool ThresholdBreached,
    DateTimeOffset AsOf,
    DateTimeOffset ComputedAt);

/// <summary>A history point: the day's last composite and, when a reference level existed, its dislocation.</summary>
public sealed record DailyAggregate(
    DateOnly Date,
    DateTimeOffset AsOf,
    Guid CompositeId,
    long CompositeSequence,
    int ConfigVersion,
    string WeightingSchemeId,
    double Composite,
    Guid? DislocationId,
    double? MarketObservedIv,
    double? DislocationValue,
    double? Threshold,
    bool? ThresholdBreached,
    string? Regime,
    double? SensitivityFactor);
