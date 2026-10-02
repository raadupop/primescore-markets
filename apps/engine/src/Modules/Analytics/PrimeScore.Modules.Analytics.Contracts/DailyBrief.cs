using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Contracts;

/// <summary>
/// The daily brief of one market context (ADR-0016), for the New York date of its latest decision,
/// composed on read from recorded decisions, Cboe closes, the calendar, the ratio and the event record.
/// Dashboard only while Cboe-derived figures stay in personal research use (ADR-0015).
/// </summary>
public sealed record GetDailyBrief(string Context) : IQuery<DailyBrief>;

/// <param name="Error">Why no brief exists (unknown context, no decision yet); the parts are then empty.</param>
/// <param name="NewOnCalendarTotal">All catalysts first recorded since the previous close and scheduled after the brief date; <paramref name="NewOnCalendar"/> lists at most 20.</param>
public sealed record DailyBrief(
    string Context,
    string? ReferenceInstrument,
    DateOnly? BriefDate,
    BriefState? State,
    IReadOnlyList<BriefGridRow> Grid,
    IReadOnlyList<BriefCatalyst> Ahead,
    IReadOnlyList<BriefRecord> Records,
    IReadOnlyList<BriefPassed> Passed,
    IReadOnlyList<BriefCatalyst> NewOnCalendar,
    int NewOnCalendarTotal,
    string? Error,
    DateTimeOffset ComputedAt);

/// <param name="DaysInState">Consecutive NYSE trading days, back from the brief date, whose last decision carried <paramref name="State"/>.</param>
/// <param name="PreviousState">The state of the day that ended the run; null when a day without a decision ended it.</param>
public sealed record BriefState(
    Guid DecisionId,
    DateTimeOffset AsOf,
    string ReferenceInstrument,
    double Level,
    double? Percentile,
    string State,
    int DaysInState,
    string? PreviousState);

/// <summary>One index of the cross-asset grid; the market's own reference index is left out (it is the state line).</summary>
/// <param name="Date">The latest Cboe close on or before the brief date; null when none is recorded.</param>
/// <param name="Percentile">Share of up to 1,260 prior Cboe closes at or below the close.</param>
/// <param name="State">The published state rule with the default bounds (30th and 70th percentiles, 5% tail); <c>unknown</c> below 252 prior closes.</param>
public sealed record BriefGridRow(string Instrument, DateOnly? Date, double? Close, double? Percentile, int PriorCloses, string State);

/// <param name="Family">Wire name (<c>CPI</c>, ...).</param>
public sealed record BriefCatalyst(
    string CatalystId,
    string Family,
    string Title,
    DateTimeOffset ScheduledAt,
    bool TimeAnnounced,
    DateOnly EventCloseDate,
    double? Ratio,
    double? RatioPercentile,
    long LedgerSequence);

/// <param name="Oil">Measured on OVX only (WPSR, OPEC).</param>
public sealed record BriefRecord(string Family, bool Oil, CatalystOutcomeSummary Latest12);

public sealed record BriefPassed(string Family, CatalystOutcome Outcome);
