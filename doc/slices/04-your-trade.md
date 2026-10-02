# Slice 4 — Your trade

**Status:** built; click-through not yet run by the operator.

## Question

What do the scheduled releases before my option's expiry mean for my position? Screen: Your
trade (new), linked to each family's Event record.

## Requirements

RSK-004, the measured terms of EXT-004 ([SRS](../srs/PrimeScore-SRS.md) v2.6.0).

## Design

[ADR-0014](../adr/0014-position-event-scenarios.md): the operator enters up to four S&P 500 option
legs with one expiry; nothing is stored. Black-Scholes-Merton with the VIX-family volatility for
the expiry (VIX9D, VIX, VIX3M, VIX6M, interpolated in total variance); maximum loss at expiry;
every past release of each scheduled FOMC, CPI, NFP, GDP or PCE event before expiry replayed on the
position (its event-day move, and its VIX9D change carried by √(9 ÷ days left)), split into the
move part and the volatility part. `GetPositionScenarios` in the Analytics module;
`POST /analytics/position-scenarios` (contract 1.5.0); page `/your-trade?u=&exp=&legs=1C7675,1P7675`.

## UI acceptance

Automated: `PositionScenarioTests.A_long_straddle_matches_an_independent_valuation_and_the_FOMC_replay_through_the_API_and_the_page`
(stub files; value, Greeks and the six-meeting replay against an independent Python calculation,
within a cent, through the API and the page; the example page; an unreadable leg),
`OptionPricingTests` (Hull's example, at-the-money hand values, put-call parity, the normal
distribution to 1e-13, the term structure), `PositionScenarioHandlerTests` (wiring, SPY, input
errors, maximum loss, the square-root-of-time rule).

Independent check on real data, 2026-10-02: a separate script recomputed the straddle below from
Cboe's files and the engine's event record, and matched every figure for all five releases.

Click-through on the demo copy as of 2026-10-02 (closes to 2026-10-01). Start it with
`.\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db`.

| # | Do | See |
| --- | --- | --- |
| 1 | Your trade, no inputs | "Example position": a long at-the-money straddle, as of the close on 2026-10-01, SPX 7,666.45 |
| 2 | Legs: long 1 call 7675, long 1 put 7675; expiry 2026-10-30; Show scenarios | Estimated value $28,164; 29 days to expiry; implied volatility 16.4%; worst loss at expiry $28,164; vega +$1,721; theta -$488 |
| 3 | Check the volatility by hand | Cboe closes on 2026-10-01: VIX9D 14.00, VIX 16.39. Total variance at 29 days: 0.14² × 9 + (0.1639² × 30 − 0.14² × 9) × 20 ÷ 21; √(that ÷ 29) = 16.4% |
| 4 | Releases table | Five rows: NFP-2026-10-02, CPI-2026-10-14, FOMC-2026-10-28, GDP-2026-10-29, PCE-2026-10-29 |
| 5 | Row CPI-2026-10-14 | 155 past releases; from the move +$1,061; from the volatility change -$560; average +$476; median -$312; lost money 94 of 155 (61%); time decay until then -$7,229 |
| 6 | Row FOMC-2026-10-28 | 103 past meetings; average +$2,210; lost money 31 of 103 (30%); time decay until then -$20,731 |
| 7 | Change leg 1 to Short and remove leg 2; Show scenarios | Worst loss at expiry: Unlimited |
| 8 | Click "record →" on the CPI row | The CPI Event record (slice 3) |

Figures change with each new close; step 3's arithmetic always holds for the as-of date shown.

## Deferred

TODO-013 (option prices: per-strike volatility, real entry prices), the Positions, Exits and Risk
modules (stored positions and enforced exits, SRS POS-001, EXT-004 enforcement), TODO-018
(publishing).
