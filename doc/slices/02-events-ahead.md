# Slice 2 — Events ahead

**Status:** built; click-through not yet run by the operator. CPI and the Employment Situation
appear only once `Sources:BlsCalendar:UserAgent` names a contact ([ENGINE.md](../../docs/ENGINE.md#catalyst-calendars)).

## Question

What is scheduled in the next 30 days, and how is the short end of volatility priced before it?
Screens: Catalyst calendar, Data & health, ledger entry.

## Requirements

SIG-006, CLS-010, CAT-001, CAT-002, CAT-003 ([SRS](../srs/PrimeScore-SRS.md) v2.4.0).

## Design

[ADR-0009](../adr/0009-cboe-index-history-and-fred-cross-check.md) (source adapters, Cboe of
record, FRED cross-check), [ADR-0010](../adr/0010-classification-of-adapter-context-series.md),
[ADR-0011](../adr/0011-catalyst-calendar-ids-vintages-and-sources.md) (Catalysts module, ids,
vintages), [ADR-0012](../adr/0012-pre-catalyst-ratio-against-weekday-matched-placebo-days.md)
(ratio and placebo baseline, computed on read in Analytics). API: `GET /catalysts`,
`GET /catalysts/{catalyst_id}` (contract 1.3.0). CLI: `sources`, `signals`, `import-catalysts`.

## UI acceptance

Automated: `CatalystTests.Catalysts_list_FOMC_2026_10_28_with_its_ratio_from_ledger_data_and_a_reschedule_adds_a_vintage`
(stub Cboe and Fed files; hand-derived ratio −0.055, n = 10, percentile 50%, read from the page),
`SourceAdapterTests`, `UiSecurityTests` (the Data & health page and its Run now controls).

Click-through on the demo copy of slice 1, after one run with live feeds on 2026-10-01
(`.\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db -PullSources`, then
restart without `-PullSources` to freeze it).

| # | Do | See |
| --- | --- | --- |
| 1 | Data & health → Cboe | Last run 10 of 10 files, no HTTP 403; VIX 9,284 observations and VIX9D 3,958, both latest 2026-09-30 20:15 UTC |
| 2 | Data & health → calendars | FedCalendar 119 new; BeaCalendar 46; EiaCalendar 98; ClaimsCalendar 70 (derived by rule); OpecCalendar 0 (curated file empty); BlsCalendar disabled with "UserAgent must name a contact" |
| 3 | Catalyst calendar, From 2026-10-02 | 11 scheduled catalysts: WPSR 10-07, 10-15, 10-21, 10-28; CLAIMS 10-08, 10-15, 10-22, 10-29; FOMC-2026-10-28 14:00 ET; GDP-2026-10-29 and PCE-2026-10-29 08:30 ET |
| 4 | Row FOMC-2026-10-28 | Ratio −0.131 as of 2026-09-30, read 28 days before; percentile 17%, n = 191; "halo incomplete: no CPI, NFP, GDP, PCE, OPEC calendar reaches the earliest read date"; never rescheduled |
| 5 | Check step 4 by hand | In Cboe's files for 2026-09-30: VIX9D 14.20, VIX 16.34; 14.20 / 16.34 − 1 = −0.1310 |
| 6 | Rows GDP-2026-10-29, PCE-2026-10-29 | Percentile 27% (n = 22) and 10% (n = 10) |
| 7 | Rows WPSR and CLAIMS | "weekly release: every same weekday is an event day", no percentile |
| 8 | Family = FOMC | Only FOMC-2026-10-28 |
| 9 | Click "ledger #…" on the FOMC row | The CatalystScheduled entry with the Fed page URL, file SHA-256 and the entry's hash |

Values in steps 1 to 7 change when the copy pulls newer closes or schedules; the check in step 5
always holds for the as-of date shown.

## Deferred

TODO-014 (basket and macro prints from originators), TODO-016 (VVIX and VIX live starts, before
Cboe runs on the live ledger), TODO-019 (calendar history that completes the halo), TODO-021
(OPEC source), TODO-023 (catalyst id on decisions).
