# Slice 1 — Volatility state

**Status:** built; click-through not yet run by the operator.

## Question

Where does volatility stand against its own history, and what followed past extremes?
Screens: Market overview, analysis page, Decision journal, Outcomes record, Historical replay.

## Requirements

DEC-001, DEC-003, DEC-005, CLS-006, ANA-001, ANA-003, AUD-001, AUD-002 ([SRS](../srs/PrimeScore-SRS.md) v2.4.0).

## Design

[ADR-0008](../adr/0008-directional-scenario-refuted.md). The Classification module records the
level's percentile among up to 1,260 prior closes in both regime modes; the Decision module
labels the state and gates on the tail; the Configuration module upgrades stored settings to the
tail gate on start-up; the Analytics module computes the outcomes record on read
(`GET /analytics/outcomes`, contract 1.2.0). CLI: `replay`, `outcomes-report`.

## UI acceptance

Automated (`bash harness/check-suite.sh`): `DecisionTests.UI_research_analysis_explains_the_reference_values_and_conditions_without_implying_execution`,
`DecisionTests.UI_decision_journal_lists_Volmageddon_first_as_all_checks_passed_in_the_extremely_stretched_state`,
`OutcomesTests.UI_outcomes_record_shows_the_API_counts_with_ISO_dates_and_the_in_sample_caveat`,
`ReplayTests` (the replay page), all on the Volmageddon fixture with hand-counted percentiles.

Click-through on the demo copy built 2026-10-01 (live ledger to 2026-09-22, Cboe closes to
2026-09-30; replay `645c8cdb-8ba3-46c7-b181-85f17fde9973`). Start it with
`.\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db` and sign in.

| # | Do | See |
| --- | --- | --- |
| 1 | Market overview, US equities | VIX 16.34 as of 2026-09-30; 35.5th percentile of 1,260 prior closes; state Normal; Checks not met; 3 of 4 checks passed; No position opened |
| 2 | Check step 1 by hand | In Cboe's `VIX_History.csv`, 447 of the 1,260 closes before 2026-09-30 are at or below 16.34: 447 / 1,260 = 0.3548 |
| 3 | Market overview, Crude oil | OVX 52.24 as of 2026-09-30; 83.1st percentile; state Stretched; Checks not met |
| 4 | US equities → Read this analysis | Check "Level in the extreme tail": 35.48% from the nearer end, requirement at most 5.00%, Not met; the other three checks Passed |
| 5 | Decision journal | Newest first: 2026-09-30 rows marked Checks not met; older rows marked "legacy gate". The three tiles add up to Records shown |
| 6 | Historical replay → saved run "State gate, 2016 to 2026-09-30 (demo)" | Observations read 27,068; Decision records 10,817; All checks passed 1,357; a note that the table shows the first 300 records |
| 7 | "What followed the decisions in this run →" (US equities) | Decision days 2,695, 2016-01-04 → 2026-09-22, 35 left out; All checks passed 363; 21 trading days: 363 of 2,680, moved back toward median 77%, interval 67% to 87% |
| 8 | Same page, state table | Extremely stretched 126 days, median change at 21 days −9.28, up 13%; Extremely compressed 237 days, +0.63, up 72% |
| 9 | Switch the market to Crude oil | All checks passed 264; 21 trading days: moved back 76%, interval 64% to 89% |
| 10 | Historical replay → run 2018-02-01 to 2018-02-09 (admin) | 2018-02-02 Checks not met, Stretched; 2018-02-05 All checks passed, Extremely stretched. The extreme state appears on the shock day, not before it |

Counts in steps 7 and 9 that read "of N" grow when later closes are recorded; the shares do not
change unless an extreme day gains a horizon.

## Deferred

TODO-011 (replay on the live ledger), TODO-010 (forward test of the catalyst crush), TODO-012
remainder (§9 events replaced by forward tests in slice 6).
