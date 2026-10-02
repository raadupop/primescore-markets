using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Contracts;

/// <summary>
/// The event record of one catalyst family (ADR-0013): for each past event, the S&amp;P 500 move
/// VIX9D priced over its 9 calendar days against the move that happened, the event-day move and the
/// volatility change across the event (OVX only for WPSR and OPEC). Computed on read from the current
/// calendar and Cboe-recorded closes; nothing is stored.
/// </summary>
/// <param name="Family">The family's wire name (<c>FOMC</c>, <c>CPI</c>, ...); an unknown name gives an empty record.</param>
public sealed record GetCatalystOutcomes(string Family) : IQuery<CatalystOutcomeReport>;

/// <param name="ReferenceInstruments">The Cboe series the record reads, for example VIX9D, VIX, SPX or OVX.</param>
/// <param name="Events">Past events, newest first; window-open rows included.</param>
/// <param name="WindowOpen">Rows whose 9-day window ends after the latest close; excluded from the summaries.</param>
/// <param name="BeforeLiveStart">Events whose read date precedes VIX9D's live start (2013-10-01); not rows.</param>
/// <param name="MissingCloses">Events missing a needed close on the read date, event close or window end; not rows.</param>
/// <param name="All">Summary of every complete row.</param>
/// <param name="Latest12">Summary of the 12 most recent complete rows.</param>
public sealed record CatalystOutcomeReport(
    string Family,
    IReadOnlyList<string> ReferenceInstruments,
    IReadOnlyList<CatalystOutcome> Events,
    int WindowOpen,
    int BeforeLiveStart,
    int MissingCloses,
    CatalystOutcomeSummary All,
    CatalystOutcomeSummary Latest12,
    DateTimeOffset ComputedAt);

/// <summary>A null member means "not measured", never zero. Moves are fractions of the index (0.0471 = 4.71%).</summary>
/// <param name="ReadDate">The NYSE trading day immediately before the event's New York date.</param>
/// <param name="EventCloseDate">The first NYSE trading day whose 16:00 close is after the event.</param>
/// <param name="WindowEndDate">The latest NYSE trading day on or before the read date + 9 calendar days.</param>
/// <param name="RatioBefore">VIX9D ÷ VIX − 1 on the read date (ADR-0012).</param>
/// <param name="PricedMove">VIX9D(read) ÷ 100 × √(9/365); null for oil families.</param>
/// <param name="ActualMove">SPX(window end) ÷ SPX(read) − 1, signed; null while the window is open.</param>
/// <param name="InsidePricedRange">|actual| ≤ priced.</param>
/// <param name="BelowStraddleEstimate">|actual| &lt; √(2/π) × priced: smaller than the at-the-money straddle cost estimated from VIX9D (an upper bound on buyer losses).</param>
/// <param name="EventDayMove">SPX(event close) ÷ SPX(read) − 1.</param>
/// <param name="VolatilityChange">VIX9D (OVX for oil families) at the event close minus the read date, index points.</param>
/// <param name="OtherEventsInWindow">Scheduled FOMC, CPI, NFP, GDP, PCE or OPEC events other than this one in (read date, read date + 9 days].</param>
/// <param name="PreviouslyExamined">FOMC or CPI dated on or before 2026-09-27: the research programme already looked at it.</param>
public sealed record CatalystOutcome(
    string CatalystId,
    DateTimeOffset ScheduledAt,
    bool TimeAnnounced,
    DateOnly ReadDate,
    DateOnly EventCloseDate,
    DateOnly WindowEndDate,
    bool WindowOpen,
    double? RatioBefore,
    double? PricedMove,
    double? ActualMove,
    bool? InsidePricedRange,
    bool? BelowStraddleEstimate,
    double? EventDayMove,
    double VolatilityChange,
    int OtherEventsInWindow,
    bool PreviouslyExamined,
    bool? NeverRescheduled);

/// <param name="Count">Complete rows summarised (n).</param>
public sealed record CatalystOutcomeSummary(
    int Count,
    double? MedianPricedMove,
    double? MedianAbsActualMove,
    int? InsidePricedRange,
    int? BelowStraddleEstimate,
    double? MedianAbsEventDayMove,
    double? MedianVolatilityChange,
    int VolatilityFell);
