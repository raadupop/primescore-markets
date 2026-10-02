# ADR-0014: Position event scenarios: past events replayed on an entered option position

**Status:** Accepted

**Date:** 2026-10-02
**Deciders:** Radu Pop (operator); coding agent

## Context

Slice 4 answers: what do the scheduled events before my option's expiry mean for my position?
SRS EXT-004 already asks for the inputs, the expected gain from the move (Γ · ΔS²) against the
expected loss from the volatility drop (Vega · ΔIV) per catalyst type, but left their
distributions "configurable". The event record (ADR-0013) now measures both per family. Forces:

- No option prices or broker: the operator types the position; the engine sends no order.
- The only free implied volatilities are Cboe's S&P 500 indices: VIX9D, VIX, VIX3M and VIX6M,
  constant maturities of 9, 30, 93 and 184 calendar days, variance measures across strikes.
- A per-strike volatility (skew) needs option prices; none are licensed (TODO-013).
- Telling a person to hold or exit their own position is a personal recommendation
  (`doc/research/recommendation-rules-and-disclosures.md`); showing measured figures is not.

## Decision

**Input, not stored.** The position (S&P 500 index options, up to four legs sharing one expiry,
optional entry cost) is a query; nothing enters the ledger. SPY is valued at SPX ÷ 10, an
approximation that ignores tracking and dividend timing. `GetPositionScenarios` in the Analytics
module; API `POST /analytics/position-scenarios` (contract 1.5.0, READ role, no state change).

**Valuation.** Black-Scholes-Merton with continuous dividend yield, defaults r = 4.0% and
q = 1.3%, both editable. Time is calendar days ÷ 365 to 16:00 New York on the expiry date. The
as-of date is the latest NYSE trading day with Cboe SPX and VIX closes. The implied volatility for
the expiry interpolates the VIX-family closes on that date linearly in total variance (σ² · t),
flat beyond 9 and 184 days, and applies to every strike. Delta, gamma and vega are analytic;
theta is a one-day revaluation. Dollar figures use the 100 multiplier.

**Maximum loss at expiry.** The payoff is piecewise linear, so it is evaluated at zero and at each
strike, minus the entry cost (today's model value when none is given). Unlimited when the net call
quantity is negative.

**Event scenarios.** Events: scheduled FOMC, CPI, NFP, GDP and PCE with an event close after the
as-of date and on or before expiry. For each, every past event of the family with an event-day
move in the event record is one scenario, applied at the event close with the remaining time
τ_e days:

- level S × (1 + M_i), M_i the past event-day move;
- volatility IV + ΔVIX9D_i × min(1, √(9 ÷ τ_e)), the past VIX9D change carried to the option's
  remaining time by the square root of time (short tenors move more; an option with 9 days or
  fewer left takes the full change), floored at 1 point. Spreading the past event's variance over
  τ_e was rejected: with a few days left it produced drops of tens of points that the floor hid;
- result: the position's value after minus before, both at τ_e, so time decay is separate and
  shown as the decay until the event.

Per event: n, the mean of the move part (volatility fixed) and of the volatility part (level
fixed), the mean and median total, the share of scenarios that lost money, the worst and the best.
The move and volatility parts are EXT-004's two terms, measured instead of configured.

**Wording.** Figures only: no "hold", "exit", "buy" or "sell". Every figure is labelled an estimate
from indices with one volatility for all strikes.

## Consequences

- An operator sees, before each scheduled release, how past releases of that type would have moved
  the value of the position, split into the move and the volatility change.
- Positions with skew exposure (wings, risk reversals) are mispriced by one flat volatility.
- Historical moves are applied at today's level, so a calm-period record understates a stressed
  market and the reverse.
- Releases on one date (GDP and PCE) are listed separately and their scenarios do not combine.

## Trade-offs

Full revaluation per scenario was chosen over a Greeks approximation: it costs more computation
but stays correct for directional and short positions, whose gains and losses curve with the move.

## References

ADR-0009, ADR-0012, ADR-0013. SRS EXT-004, RSK-004. `doc/slices/04-your-trade.md`.
