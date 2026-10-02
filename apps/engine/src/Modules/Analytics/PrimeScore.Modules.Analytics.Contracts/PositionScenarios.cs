using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Contracts;

/// <summary>
/// Event scenarios for an operator-entered S&amp;P 500 option position (ADR-0014): its estimated value
/// and Greeks from the VIX-family implied volatility, its maximum loss at expiry, and each scheduled
/// FOMC, CPI, NFP, GDP or PCE release before expiry replayed with every past release of that family.
/// The position is not recorded and no order exists.
/// </summary>
public sealed record GetPositionScenarios(PositionInput Position) : IQuery<PositionScenarioReport>;

/// <param name="Underlying"><c>SPX</c>, or <c>SPY</c> valued at SPX ÷ 10.</param>
/// <param name="Legs">One to four legs sharing <paramref name="Expiry"/>.</param>
/// <param name="EntryCost">Dollars paid for the whole position (negative when received); null uses today's model value.</param>
/// <param name="Rate">Continuously compounded risk-free rate (0.04 = 4%).</param>
/// <param name="DividendYield">Continuous dividend yield of the index.</param>
public sealed record PositionInput(
    string Underlying,
    DateOnly Expiry,
    IReadOnlyList<PositionLeg> Legs,
    double? EntryCost = null,
    double Rate = 0.04,
    double DividendYield = 0.013);

/// <param name="Right"><c>C</c> (call) or <c>P</c> (put).</param>
/// <param name="Quantity">Contracts; positive long, negative short. Each contract is 100 units of the underlying.</param>
public sealed record PositionLeg(string Right, double Strike, int Quantity);

/// <summary>Dollar figures are for the whole position. Null members mean "not available", never zero.</summary>
/// <param name="Error">Why the position could not be valued (bad input, no data); the other members may then be partly null.</param>
/// <param name="AsOf">The latest NYSE trading day with Cboe SPX and VIX closes.</param>
/// <param name="UnderlyingLevel">SPX close (SPX ÷ 10 for SPY) on <paramref name="AsOf"/>.</param>
/// <param name="ImpliedVolatility">VIX-family volatility interpolated to the expiry, as a fraction (0.205 = 20.5%).</param>
/// <param name="Value">Model value now.</param>
/// <param name="Delta">Dollars per 1-point rise of the underlying.</param>
/// <param name="Gamma">Change of <paramref name="Delta"/> per 1-point rise.</param>
/// <param name="Vega">Dollars per 1 volatility point.</param>
/// <param name="Theta">Dollars per calendar day with level and volatility unchanged.</param>
/// <param name="MaxLossAtExpiry">Largest loss at expiry against the entry cost; null when unlimited.</param>
public sealed record PositionScenarioReport(
    string? Error,
    DateOnly? AsOf,
    double? UnderlyingLevel,
    double? ImpliedVolatility,
    int? DaysToExpiry,
    double? Value,
    double? Delta,
    double? Gamma,
    double? Vega,
    double? Theta,
    double? EntryCost,
    double? MaxLossAtExpiry,
    bool UnlimitedLoss,
    IReadOnlyList<PositionEventScenarios> Events,
    DateTimeOffset ComputedAt);

/// <param name="Family">Wire name (<c>CPI</c>, ...).</param>
/// <param name="DaysFromAsOf">Calendar days from the as-of date to the event close.</param>
/// <param name="DecayToEvent">Value change from now to just before the event with level and volatility unchanged.</param>
/// <param name="Scenarios">Past releases of the family replayed (n).</param>
/// <param name="MeanMovePart">Mean value change from the past event-day move alone (volatility fixed): the gamma side of SRS EXT-004.</param>
/// <param name="MeanVolatilityPart">Mean value change from the past volatility change alone (level fixed): the vega side.</param>
/// <param name="LossShare">Share of scenarios whose total change was negative.</param>
public sealed record PositionEventScenarios(
    string CatalystId,
    string Family,
    DateTimeOffset ScheduledAt,
    DateOnly EventCloseDate,
    int DaysFromAsOf,
    double DecayToEvent,
    int Scenarios,
    double? MeanMovePart,
    double? MeanVolatilityPart,
    double? MeanTotal,
    double? MedianTotal,
    double? LossShare,
    double? Worst,
    double? Best);
