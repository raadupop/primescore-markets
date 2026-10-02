using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Decision.Contracts;

public enum DecisionOutcome
{
    /// <summary>
    /// Every condition held (SRS DEC-001): since ADR-0008, the reference level sits in the extreme
    /// tail of its own history and the evidence is fresh and confirmed. A recorded research state
    /// with no direction and no position; the API keeps the name <c>DEPLOY</c>.
    /// </summary>
    Deploy,

    /// <summary>At least one condition failed: zero exposure while processing continues (SRS DEC-002).</summary>
    Idle,
}

/// <param name="Operator">How <paramref name="Actual"/> is compared with <paramref name="Required"/>.</param>
/// <param name="Detail">What the actual value is, in words (e.g. which assessment was the top signal).</param>
public sealed record ConditionEvaluation(string Name, string Operator, double Required, double Actual, bool Passed, string Detail);

/// <summary>A confirmed assessment named in a decision's explanation (SRS DEC-003).</summary>
/// <param name="Conviction">Signed severity × certainty.</param>
public sealed record DecisionSignal(
    Guid SignalId,
    Guid AssessmentId,
    SourceCategory Category,
    string Instrument,
    DateTimeOffset ObservedAt,
    double Score,
    double Certainty,
    double Conviction);

/// <summary>One deploy or idle decision for a context, with everything it was computed from.</summary>
/// <param name="AsOf">Observation time of the signal whose assessment triggered it (the contract's <c>decided_at</c>).</param>
/// <param name="Scenario">
/// The volatility state: <c>extreme_low</c>, <c>low</c>, <c>normal</c>, <c>high</c>, <c>extreme_high</c> or
/// <c>unknown</c> (ADR-0008). Decisions recorded earlier hold the directional labels <c>vol-expansion</c>,
/// <c>vol-compression</c> or <c>none</c>, which the data refuted.
/// </param>
/// <param name="TopContributing">Confirmed assessments on the composite's side, strongest first.</param>
/// <param name="Dissenting">Confirmed assessments against the composite's sign.</param>
/// <param name="LevelPercentile">The reference level's ECDF percentile within its prior closes; null before ADR-0008 or without history.</param>
public sealed record DecisionView(
    Guid DecisionId,
    long LedgerSequence,
    CorrelationId CorrelationId,
    ConfigVersion ConfigVersion,
    string Context,
    DecisionOutcome Outcome,
    string Scenario,
    Guid CompositeId,
    double CompositeScore,
    Guid DislocationId,
    double DislocationValue,
    double DislocationThreshold,
    string ReferenceInstrument,
    double MarketObservedIv,
    double SignalImpliedIv,
    Guid TriggerSignalId,
    IReadOnlyList<ConditionEvaluation> Conditions,
    IReadOnlyList<DecisionSignal> TopContributing,
    IReadOnlyList<DecisionSignal> Dissenting,
    string Explanation,
    DateTimeOffset AsOf,
    DateTimeOffset RecordedAt,
    double? LevelPercentile = null);

/// <summary>
/// Decisions whose observation time lies in <c>[From, To]</c>, newest first; optionally one context,
/// one outcome, or the decisions recorded under one correlation id (tracing a signal to its decision).
/// </summary>
/// <param name="Take">At most this many; null returns every match.</param>
public sealed record GetDecisions(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Context = null,
    DecisionOutcome? Outcome = null,
    CorrelationId? CorrelationId = null,
    int? Take = 500) : IQuery<IReadOnlyList<DecisionView>>;

public sealed record GetDecision(Guid DecisionId) : IQuery<DecisionView?>;

/// <summary>Evaluate temporary decisions without recording live aggregates or decisions. SettingsJson is the canonical EngineSettings snapshot.</summary>
public sealed record GetReplayDecisions(DateTimeOffset From, DateTimeOffset To, long MaxSequence, ConfigVersion Version, string SettingsJson)
    : IQuery<IReadOnlyList<DecisionView>>;

/// <summary>The observation time, outcome, composite score and state of a context's decisions, oldest first (History markers, forward outcomes).</summary>
public sealed record GetDecisionOutcomes(string Context, DateTimeOffset? From = null, DateTimeOffset? To = null) : IQuery<IReadOnlyList<DecisionOutcomePoint>>;

/// <param name="State">The stored <c>scenario</c> field: a state label since ADR-0008, a directional label before.</param>
/// <param name="ReferenceInstrument">The instrument whose level the decision placed; outcomes are measured on it.</param>
public sealed record DecisionOutcomePoint(DateTimeOffset AsOf, long LedgerSequence, DecisionOutcome Outcome, double CompositeScore, string State,
    string ReferenceInstrument);

/// <summary>Record a decision for every dislocation that has none yet (normally started by the classification pass).</summary>
public sealed record MakePendingDecisions : ICommand<MakePendingDecisionsAck>;

public sealed record MakePendingDecisionsAck(int Deploy, int Idle) : ICommandAck;

/// <summary>The contract's audit event types that v1 records; approvals and positions are Milestone B.</summary>
public enum AuditEventType
{
    DeployDecision,
    IdleDecision,
}

/// <summary>An append-only audit record of a decision (SRS AUD-001), read from its ledger entry.</summary>
/// <param name="InputSnapshot">Canonical JSON of the values the decision read.</param>
/// <param name="OutputSnapshot">Canonical JSON of what it produced.</param>
public sealed record AuditEntryView(
    Guid AuditId,
    AuditEventType EventType,
    Guid EntityId,
    string InputSnapshot,
    string OutputSnapshot,
    DateTimeOffset RecordedAt,
    long SequenceNumber);

/// <summary>Audit entries in ledger order, filtered by entity, event type and recording time.</summary>
public sealed record GetAuditEntries(
    Guid? EntityId = null,
    AuditEventType? EventType = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IQuery<IReadOnlyList<AuditEntryView>>;
