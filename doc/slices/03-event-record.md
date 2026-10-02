# Slice 3 — Event record

**Status:** built; click-through not yet run by the operator.

## Question

What did options price before each past event, and what happened? Screens: Event record (new),
Catalyst calendar (each family links to its record).

## Requirements

CAT-004 ([SRS](../srs/PrimeScore-SRS.md) v2.5.0).

## Design

[ADR-0013](../adr/0013-event-record-priced-against-actual-move.md): priced 9-day move from VIX9D,
actual S&P 500 move over the same window, straddle cost estimate √(2/π) × priced, event-day move,
VIX9D change (OVX for WPSR and OPEC). `GetCatalystOutcomes` in the Analytics module, computed on
read from the Catalysts calendar and Cboe closes. API `GET /analytics/catalyst-outcomes?family=`
(contract 1.4.0, additive). Page `/event-record?family=`.

## UI acceptance

Automated: `EventRecordTests.The_FOMC_record_matches_the_hand_calculation_through_the_API_and_the_page`
(stub Cboe VIX, VIX9D, SPX and the Fed's 2026 calendar; six meetings derived by hand, read from the
API and the page), `CatalystOutcomeHandlerTests` (open windows, missing closes, early events,
overlapping events, oil, event-close rule).

Independent check on real data, 2026-10-02: a separate script recomputed every FOMC, CPI and NFP row
from Cboe's files and matched the engine on all 413 rows to 1e-9.

Click-through on the demo copy as of 2026-10-02 (Cboe closes to 2026-10-01, BLS calendar loaded).
Start it with `.\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db`.

| # | Do | See |
| --- | --- | --- |
| 1 | Event record, family CPI | 155 complete events; 9 before VIX9D's live start. Priced 9-day range ±2.47%; actual 1.41%; inside the priced range 121 of 155 (78%); smaller than the straddle cost estimate 102 of 155 (66%); VIX9D fell 103 of 155 |
| 2 | Same page, Latest 12 | Inside 10 of 12 (83%); smaller than the straddle cost estimate 9 of 12 (75%) |
| 3 | Newest row, CPI-2026-09-11 | Ratio −0.008; priced ±2.78% (read 2026-09-10); actual +0.77% to 2026-09-18; inside yes; below the estimate yes; event day +0.86%; VIX9D −3.23; "previously examined · +1 other event in window" |
| 4 | Check step 3 by hand | Cboe files: VIX9D 17.70 on 09-10 → 17.70 ÷ 100 × √(9/365) = 2.78%. SPX 7,591.70 on 09-10, 7,650.50 on 09-18 → +0.77%. SPX 7,656.98 on 09-11 → +0.86%. VIX9D 14.47 on 09-11 → −3.23 |
| 5 | Family FOMC | 103 complete events; inside 77 of 103 (75%); smaller than the straddle cost estimate 68 of 103 (66%); Latest 12: inside 6 of 12 |
| 6 | Family NFP | 155 complete events; inside 121 of 155 (78%); smaller than the straddle cost estimate 103 of 155 (66%) |
| 7 | Family WPSR | "Oil volatility only"; 92 complete events; OVX fell after 45 of 92 (49%) |
| 8 | Catalyst calendar, From 2026-10-02 | NFP-2026-10-02 and CPI-2026-10-14 listed; each row's "Past events →" opens that family's record |

Counts grow as new events complete; step 4 always holds for the row it names.

## Deferred

TODO-013 (option prices: an event-specific implied move and real straddle costs), TODO-018
(whether any record figure may be published; the priced move reveals the VIX9D level), TODO-019
(GDP and PCE history before 2025), TODO-021 (OPEC dates).
