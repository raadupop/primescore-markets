using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Contracts;

/// <summary>
/// What the reference index did after each recorded decision (ADR-0008). One state per New York
/// trading day (the last decision recorded for it); the change runs from the reference close on that
/// date to the close exactly <c>h</c> NYSE trading days later, and is left out when either close is
/// missing. Computed from the ledger on read; null for an unknown context or replay.
/// <paramref name="From"/> and <paramref name="To"/> bound the decision dates. With
/// <paramref name="ReplayId"/> the decisions and the reference instrument come from that stored
/// replay instead of the live journal and the active settings.
/// </summary>
public sealed record GetForwardOutcomes(string Context, DateTimeOffset? From = null, DateTimeOffset? To = null, Guid? ReplayId = null)
    : IQuery<ForwardOutcomesReport?>;

/// <param name="ReplayId">The replay whose decisions were measured; null for the live journal.</param>
/// <param name="Days">Decision dates measured: NYSE trading days with a reference close, decided on this reference instrument.</param>
/// <param name="DeployDays">Of those, dates whose last decision was a DEPLOY in an extreme state (the ADR-0008 gate).</param>
/// <param name="LegacyDays">Of those, dates whose last decision carries a pre-ADR-0008 directional label; they count in the baseline and the state table only.</param>
/// <param name="ExcludedDays">Decision dates on this instrument left out because the date is not an NYSE trading day or has no reference close.</param>
/// <param name="OtherInstrumentDays">Decision dates whose decisions were all placed on another reference instrument (for example before a reference change); not measured.</param>
/// <param name="Horizons">One row per horizon, ascending.</param>
/// <param name="States">One row per recorded state label present in the range.</param>
public sealed record ForwardOutcomesReport(
    string Context,
    string ReferenceInstrument,
    Guid? ReplayId,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    int Days,
    int DeployDays,
    int LegacyDays,
    int ExcludedDays,
    int OtherInstrumentDays,
    IReadOnlyList<HorizonOutcome> Horizons,
    IReadOnlyList<StateOutcome> States,
    DateTimeOffset ComputedAt);

/// <summary>Change of the reference close <paramref name="TradingDays"/> NYSE trading days after the decision date.</summary>
/// <param name="DeployCount">DEPLOY days with both closes; the denominator of every DEPLOY figure in the row.</param>
/// <param name="AllCount">Decision days (any gate) with both closes.</param>
/// <param name="DeployMedianAbsChange">Median |change| on DEPLOY days; null without any.</param>
/// <param name="AllMedianAbsChange">Median |change| on all decision days; null without any.</param>
/// <param name="DeployRevertShare">
/// Share of DEPLOY days on which the level moved back toward its median: down after <c>extreme_high</c>,
/// up after <c>extreme_low</c>; an unchanged close counts as not reverted. Descriptive and in sample.
/// </param>
/// <param name="DeployRevertShareLow">Lower 95 percent bound, circular block bootstrap with blocks of max(21, 2h) DEPLOY days; null below two blocks.</param>
/// <param name="DeployRevertShareHigh">Upper 95 percent bound.</param>
/// <param name="AllUpShare">Share of all decision days on which the close rose; null without any.</param>
/// <param name="DeployMeanChangeTowardMedian">Mean change toward the median on DEPLOY days, index points (positive = reverted).</param>
public sealed record HorizonOutcome(
    int TradingDays,
    int DeployCount,
    int AllCount,
    double? DeployMedianAbsChange,
    double? AllMedianAbsChange,
    double? DeployRevertShare,
    double? DeployRevertShareLow,
    double? DeployRevertShareHigh,
    double? AllUpShare,
    double? DeployMeanChangeTowardMedian);

/// <summary>Changes of the reference close grouped by the state recorded with the decision.</summary>
/// <param name="State">The stored state label (or a legacy directional label for decisions before ADR-0008).</param>
public sealed record StateOutcome(
    string State,
    int Count,
    double? MedianChange5,
    double? MedianAbsChange5,
    double? UpShare5,
    double? MedianChange21,
    double? MedianAbsChange21,
    double? UpShare21);
