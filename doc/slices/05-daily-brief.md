# Slice 5 — Daily brief

**Status:** built; click-through not yet run by the operator. Dashboard only: Cboe-derived figures
stay in personal research use until Cboe consents ([ADR-0015](../adr/0015-cboe-data-personal-research-use-until-consent.md), TODO-024).

## Question

What matters today, for one market, on one page? Screen: Daily brief (new).

## Requirements

ANA-004 ([SRS](../srs/PrimeScore-SRS.md) v2.7.0).

## Design

[ADR-0016](../adr/0016-daily-brief-composition.md): for the date of the market's latest decision,
the state with days in state, a cross-asset grid (the other indices among VIX, VXN, RVX, VVIX, OVX
and GVZ, ranked among 1,260 prior Cboe trading-day closes), the releases of the next 10 trading
days with their ratio, each family's latest-12 record, releases whose event close is the brief
date, and catalysts first recorded since the previous close. `GetDailyBrief` in the Analytics
module, computed on read; no API, nothing stored or sent. The volatility-state rule moved to the
Decision contracts (`VolatilityState`) so the grid uses the decision's own rule. Page
`/brief?market=equity|oil`.

## UI acceptance

Automated: `BriefTests.UI_the_equity_brief_states_the_level_the_days_in_state_and_that_nothing_is_scheduled`
(Volmageddon: 2018-02-05, VIX 37.32, 99.9th percentile, extremely stretched for 1 day after
stretched, nothing scheduled, grid without Cboe history), `DailyBriefHandlerTests` (days in state,
gaps, the grid rule, composition of releases ahead, records, just passed and new entries).

Independent check on real data, 2026-10-02: the five grid percentiles recomputed from Cboe's files
matched to one decimal.

Click-through on the demo copy as of 2026-10-02 (closes to 2026-10-01). Start it with
`.\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db`.

| # | Do | See |
| --- | --- | --- |
| 1 | Daily brief (US equities) | Red notice "For your own research only"; close of 2026-10-01; VIX 16.39, 36.6th percentile: Normal, 4 days in this state (before: Compressed) |
| 2 | Grid | No VIX row (it is the state line); VXN 50.8th Normal; RVX 29.7th Compressed; VVIX 41.3rd Normal; OVX 82.0th Stretched; GVZ 81.3rd Stretched |
| 3 | Check one grid row by hand | In Cboe's OVX file, 51.69 on 2026-10-01 is at or above 82.0% of the 1,260 trading-day closes before it |
| 4 | Scheduled releases | NFP-2026-10-02 (ratio −0.146, 18%), WPSR-2026-10-07, CLAIMS-2026-10-08, CPI-2026-10-14 (22%), CLAIMS-2026-10-15, WPSR-2026-10-15 |
| 5 | What such releases did before | CPI: inside the priced range 10 of the latest 12; NFP: 10 of 12; CLAIMS: 10 of 12; WPSR: OVX fell after 6 of 12 |
| 6 | Just passed | CLAIMS-2026-10-01: priced ±2.23%; S&P 500 +0.19% by the close; VIX9D −0.20 points |
| 7 | Market: Crude oil | OVX 51.69, 82.0th percentile: Stretched, 7 days in this state (before: records from the retired gate); the grid now includes VIX |
| 8 | Text version at the bottom | The same brief as plain text, ending "Personal research only; derived from Cboe data (ADR-0015)." |

The decision's VIX percentile (36.6th) and a trading-days-only rank (36.3rd) differ because the
decision's window counts Cboe's holiday-session prints (TODO-025).

## Deferred

TODO-024 (Cboe consent, before any delivery), alerts between briefs and delivery channels (after
TODO-024), stored briefs (when one leaves the dashboard), briefs for past dates.
