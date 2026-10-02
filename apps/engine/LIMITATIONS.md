# Engine limitations

Limits that hold even when every check passes. Architecture and ledger limits are in
[ADR-0003](../../doc/adr/0003-engine-modular-monolith-over-hash-chained-ledger.md).

## Replay (M5)

- Replay recomputes composites and decisions from recorded classifications, not fresh classifier calls ([ADR-0006](../../doc/adr/0006-replay-from-recorded-classifications.md)). Missing consensus, unsupported routes and recorded fallbacks remain as recorded. The ledger sequence captured before each run bounds every input; observation timestamps bound each historical calculation.
- The run uses the configuration at its captured ledger sequence, with validated temporary overrides. It does not reconstruct which configuration was operated on an historical date. The complete effective settings are stored with the replay; overridden runs are labelled `in-sample`.
- The ten-event report evaluates the equity/VIX market-data proxy. Geopolitical classifications and unsourced macro inputs are absent. SRS true/false-positive labels are target descriptions, not measured performance. No positions, exits or returns are simulated.
- Replay decisions are stored inside their `ReplayRun`, not returned by the live `/decisions` or `/audit` endpoints. Open the replay's ledger entry to inspect its settings, input sequence and timeline.
- `config_overrides` uses configuration field names in snake_case. Objects merge, arrays replace; `contexts` also accepts an object keyed by existing context name. Unknown fields, null values and invalid parameter values are refused. Classifier formulas and registry values cannot be overridden.
- The API returns all replay decisions in one response; long ranges produce large payloads. The UI and report use the same stored run.

## Operator access (M6)

- The header's brand link returns to the presentation website, including before sign-in. Local destinations use engine port 5080, the umbrella's brand preview on 8090 and Markets on 8091; start the umbrella launcher to serve all three. Other deployments use the public domains.
- Navigation visibility is not an access-control boundary; routes and APIs enforce their own authorization. The anonymous sidebar reported on 2026-09-25 was an unconditional layout render; HTTP regression checks now cover navigation before sign-in, after sign-in and after sign-out.
- HTTP acceptance checks do not exercise browser focus rendering. The heading outline reported on 2026-09-25 came from route-navigation focus; its CSS suppression preserves heading focus and control focus indicators. Browser checks remain necessary for focus styling changes.
- One ADMIN account signs into the browser with a password hash from user-secrets or environment variables. READ and ADMIN API tokens remain separate. There is no account recovery service; reset the password locally with the [setup command](../../docs/ENGINE.md#install-and-sign-in).
- Configuration editing uses JSON with validation and rejects saves based on an older version. It does not calibrate parameters or validate an operator's claim that settings were estimated independently.
- Local Development uses HTTP on loopback; HTTPS and service supervision for a hosted installation require operator setup. Passwords, token hashes and ASP.NET data-protection keys are outside the repository and must be protected with the service account's filesystem permissions.

## Data coverage (Cboe)

- **Files (first row as fetched 2026-09-28):** VIX 1990-01-02, SPX 1975-01-02, VVIX 2006-03-06, VIX6M 2008-01-02, VXN 2009-09-14, RVX 2009-09-16, VIX3M, OVX and GVZ 2009-09-18, VIX9D 2011-01-04. EVZ has no file (TODO-015).
- **Publication lag**, measured 2026-09-28: VIX3M 18:01 ET the same day, VIX9D 21:51 ET, VIX about 57 hours later. The adapter polls from 18:00 to 08:00 New York; a file updated after 08:00 is read the next evening or with **Run now**. A close not yet published is a gap, never an estimate.
- **Reconstructed marker** (ADR-0009), from live starts checked 2026-09-29: VIX9D before 2013-10-01 and VIX6M before 2013-11-27 are `true`; VIX before 2003 is `true`, 2003 is unmarked and 2004 onward `false` (Cboe states only the year); every VVIX row is unmarked (no dated launch found); the other files start after launch (`false`). **Do not enable `Sources:Cboe` on the live ledger until TODO-016 settles the dates**: a recorded marker cannot be corrected.
- VVIX's second row (2006-03-15) is implausibly low against its neighbours and is kept as published.
- SPX is stamped 16:00, like the FRED basket's SP500 it continues; the official close comes from the closing auction moments later. The volatility indices are stamped 16:15.
- Stamps before 1987 (SPX 1975-1986) use that year's US daylight-saving rule, which the engine applies itself because Windows' time-zone data carries only the 1987-2006 rule for earlier years.
- A changed past value is not seen until the file gains a new date, and then only within the last `OverlapRows` (10) recorded dates; the revision is counted, the first recorded value kept.
- A missing close (empty, `.`, zero or negative) stays missing and is counted once, by the run that records the next close. A changed header, column count or date format fails that file (item error, partial run) and records nothing from it; a challenge page answered with status 200 fails the same way.
- The first run records the full history (about 55,000 signals for the ten files). Rows before 2011-01-01 are recorded and feed reference windows but are not assessed; VIX9D, VIX3M, VIX6M and SPX closes are not assessed; a Cboe close for a date FRED already recorded is not assessed again (ADR-0010).
- File validators live in memory, so each file is downloaded once in full after a restart.

## Data coverage (FRED)

- **FRED history is frozen** (ADR-0009). `fred:` rows recorded from 2011 until 2026-09-29 stay in the ledger, are never republished, and remain the series of record for their dates, so reference windows, dislocation history and outcomes still use them. Whether published figures may rest on them is open (TODO-018).
- FRED is a read-only cross-check and records nothing. Its values are held in memory for one run; flags name the index, FRED series and date, never FRED's value. The registry maps VVIX to `VVIXCLS`, which FRED does not know: every cross-check reports it as an item error unless VVIX is listed in `Sources:Fred:ExcludedSymbols`.
- **Stopped updating on 2026-09-29:** the cross-asset basket (SP500, DGS10, DCOILWTICO, DEXUSEU), the macro prints (CPI_YOY, INITIAL_CLAIMS) and EVZ (TODO-014, TODO-015). Composites narrow accordingly: those members have no later observation. Neither is assessed today (macro prints await consensus; cross-asset is not classified), so recorded composites do not change, but once either route opens, the outcomes journal is not like-for-like across that date until TODO-014 lands.
- Historical FRED rows: SP500 starts 2016-09-26 (FRED's licensed window); EVZ ends 2025-03-11; values FRED reported as `.` were skipped and counted, never filled.

## Catalyst calendars

- **Back-filled dates are the dates releases happened**, as the originators' archives show them now, not as first announced (the 2013 and 2025 shutdown moves are invisible there). A catalyst is back-filled when its scheduled time is not after the engine first recorded it; its `never_rescheduled` is unknown (null, absent in the API) unless an import gives the original date ([ADR-0011](../../doc/adr/0011-catalyst-calendar-ids-vintages-and-sources.md)).
- History by source: FOMC from 2013 and CPI/NFP from 2013 (archive pages); GDP and PCE from 2025-01 (BEA feed); WPSR from the week ending 2024-12-27 through EIA's last listed week, never beyond; CLAIMS 60 weeks back to 63 days ahead. Anything earlier needs `import-catalysts` (TODO-019).
- CLAIMS dates are derived by rule (Thursday 08:30 New York, Wednesday before a federal-holiday Thursday), not read from DOL (TODO-020). A one-off DOL change is not known until an imported or listed date corrects it.
- OPEC dates exist only as curated in `data/catalysts/opec.csv` (TODO-021).
- FOMC 2013–2015 decisions are assumed at 14:00 New York; the statements of those years give no time (TODO-022).
- A historical Fed or BLS page is fetched on every run until one recorded vintage carries its URL, so a page whose rows all came from another source first is fetched again each run.
- A catalyst that disappears from its source, or whose date turns into TBD, is not changed: the run is flagged and the operator decides. A disagreeing lower-precedence source (the claims rule against an imported date) flags on every run.
- **Ratio** ([ADR-0012](../../doc/adr/0012-pre-catalyst-ratio-against-weekday-matched-placebo-days.md)): reads only `cboe:` closes, so it has no value until Cboe history is recorded. It ranks against the current calendar (latest vintages), not the calendar as known on each read date. Where a halo family's calendar starts after the earliest read date, the halo is incomplete; the page names the unscreened families. CLAIMS and WPSR carry no halo and get no baseline. With the one-week offset read the day before, VIX9D's nine-day horizon from the read date reaches the family's own past event, so those placebo values are not event-free.

## Point-in-time timing

- Daily index closes are stamped 16:15 America/New_York (SPX 16:00) on the observation date. Cboe publishes them hours to days later (FRED, for its history, the next morning), but the value was public at the close.
- FRED history (recorded until 2026-09-29): CPI and initial claims use each value as **first released** (FRED `output_type=4`) and are stamped 08:30 America/New_York on the first release date. CPI YoY is `100 × (index_t / index_t−12 − 1)` from first releases of the **seasonally adjusted** `CPIAUCSL`; published headline YoY and most consensus figures use the unadjusted index, which can differ by about 0.1 percentage point.
- FRED basket prices (SP500, DGS10, DCOILWTICO, DEXUSEU) are stamped 16:00 America/New_York on the observation date. FRED may publish them days later (DCOILWTICO about a week, DEXUSEU weekly), so they were not necessarily public at that stamp. They are not classified in v1.
- The classifier's `business_day` cadence counts Monday to Friday and ignores exchange holidays. The engine's composite windows and reporting intervals count NYSE trading days, excluding the rule-based holidays and the one-off closures since 2001 (ADR-0004 §4, §5). A future one-off closure is not known in advance and reads as a trading day without data.

## Contract gaps

- The API contract has no endpoint that lists signals; signal views exist in the UI and the CLI `signals` verb. The file SHA-256 and reconstructed marker are in the module contracts, dashboard and CLI, not the API contract.
- The catalyst ratio's diagnostics (day before or not, placebo days, halo exclusions, no-session reads, unscreened families) are on the Catalysts page and in the module contract, not in the API. `DecisionRecord` has no `catalyst_id` yet (TODO-023). Consensus shows "not yet captured".
- Basket prices (FRED history, Cboe SPX) are recorded as CROSS_ASSET_FLOW signals with an adapter payload (`kind: basket_observation`), because the contract's cross-asset payload requires a baseline the source does not provide.
- When a provider revises a value, the first recorded value is kept and the revision is counted, not stored. An API caller that resubmits an observation with a different payload gets a `REJECTED` result naming the recorded signal; nothing is merged.

## Ingestion rules

- The idempotency key is (source identifier, instrument, variant, observation time). The variant separates series that share an instrument: metric type (plus tenor) for market data, indicator type for macro prints (with the reference period for FRED releases), event type for geopolitical events, flow type for cross-asset flows, and a content hash for free text.
- The prefixes `fred:`, `cboe:`, `cboe-vx:`, `bls:`, `bea:`, `cal:`, `nowcast:`, `pm:`, `gdelt:`, `gpr:` and `usgs:` are reserved for the engine's source adapters; API submissions using them are rejected. A macro print names its registry symbol directly (`CPI_YOY`) or after another source name (`econ:CPI_YOY`). Rows submitted through the API before a prefix was reserved keep API treatment.
- A classifier reference window is one instrument, one category and one variant, with one point per New York date: where sources overlap on a date, the first recorded wins.
- No FRED response is cached (ADR-0009). Delete the cache an earlier version left under `var/cache/fred`.

## Classification (M2)

- Only MARKET_DATA signals and macro prints with a sourced consensus row are classified. Geopolitical events and cross-asset flows (including the basket prices) are recorded as **route not in v1**: the classifier answers 501 for those categories, and change #2 (cross-asset) is an open operator decision.
- No consensus source has been chosen yet, so `data/consensus/*.csv` hold headers only and every macro print is **awaiting consensus**. A row needs its consensus source, URL and retrieval time; rows without them are rejected and listed on Sources and health.
- Adapter-recorded rows are assessed locally as unavailable, without a classifier call, when dated before `Classification:AdapterHistoryFrom` (default 2011-01-01), when they are market data outside the indicator registry, or when their series already has an earlier-recorded row for the same New York date (ADR-0010). Assessing earlier history needs a research registration first.
- A signal is classified against the values recorded when it was classified. A value that arrives later for an earlier date does not change assessments already recorded.
- CPI YoY is derived from the seasonally adjusted index; most published consensus figures refer to the unadjusted headline, so a consensus row can differ from the print's basis by about 0.1 percentage point.
- The classifier counts weekdays, not exchange trading days, when it ages the previous close: the first close after an NYSE holiday gets temporal relevance exp(−1) = 0.37 for business-day indicators such as OVX, which lowers its certainty (e.g. OVX on 2025-04-21, after Good Friday). The engine's own windows use the NYSE calendar (ADR-0004 §4).
- Date-time query parameters without an offset (`as_of=2026-02-27T21:15:00`) are read as UTC.
- A date-time query parameter that cannot be read (`as_of=yesterday`) is a 400 with the parameter named, where it would otherwise have been ignored and current data returned. The published contract does not list this 400 yet; the amendment is an open operator question.
- `GET /classification/assessments` returns every match in one response, newest observation first, without paging. At v1 volumes (a few thousand signals) that is a few megabytes at most; paging is a contract change.
- Free text (`UNSTRUCTURED`) and macro prints without a numeric value need the language-model route (SRS CLS-003), which is not in v1. They are recorded as **route not in v1** without calling the classifier.
- The classifier is called one signal at a time. After three consecutive failures (no answer or an invalid one) it is not called again in that run; the remaining signals get their CLS-004 outcome locally and are retried on the next run.

## Composite and dislocation (M3)

- Every parameter is an uncalibrated default (ADR-0004): weights, windows, the bypass percentile, the sensitivity map and the thresholds (equity 1.5, oil 3.0 index points). The two contexts and their thresholds await operator confirmation.
- In daily data an assessment can be confirmed only by another market-data assessment (cross-asset flows would qualify but are not classified in v1); a macro print counts for 30 minutes after its release, so a composite recorded at the close does not include the morning's print. Geopolitical events are never classified in v1, so that category is always absent.
- A composite is recorded each time an assessment of one of its context's instruments is recorded, and is never recomputed: a configuration change applies from the next composite, and a late value for an earlier date adds a composite at that date without changing later ones.
- `threshold_breached` compares the dislocation's magnitude with the threshold, so a vol-compression dislocation can breach it (ADR-0004 §6); the contract's description says `dislocation_value >= threshold`.
- `PUT /config/dislocation-threshold` answers 400 with one error per invalid field (a threshold not above 0, a sensitivity factor outside (0, 1], regime boundaries out of order, or an unknown key); the contract lists only 200 for it. The SRS example sensitivity map (1.5 / 1.0 / 0.6) is refused because 1.5 lies outside (0, 1].
- The regime percentile ranks the reference level among its previous `N_L` (1260) closes; the history starts in 2011, so until about 2016 it ranks against fewer closes (the count is on each dislocation record).
- `component_scores[].assessment_count` counts the confirmed assessments that entered the category's net conviction.
- Settings can be changed through the API with an ADMIN token or through the Configuration screen after operator sign-in. An editor opened before another saved change must reload before saving.

## Decisions (M4)

- A decision is recorded for every dislocation, that is for every classified signal of a context member (ADR-0005). With the default contexts and the v1 classifier routes only market-data closes qualify: three decisions per trading day for equity (VIX, VXN, RVX) and one for oil (OVX); basket prices and macro prints awaiting consensus get none. History shows each day's last decision and marks the days where it is DEPLOY. `decided_at` is the observation time the decision refers to, while the audit trail's `from` and `to` bound the recording time (ADR-0005).
- The contract's `DecisionRecord` has no context field, so `GET /decisions` returns both contexts' decisions without saying which is which; the UI and the audit entry's input snapshot name it. Adding the field is an open operator question.
- `urgency_tier` and `requires_approval` are left out, and the audit trail records only DEPLOY and IDLE events: approvals, positions and cooldowns are Milestone B. The "no active cooldown" condition is therefore always recorded as held.
- `PUT /config/deploy-conditions` accepts `composite_score`, `contributing_sources`, `top_signal_certainty` and `newest_observation_age_trading_days`, each replacing the condition of the same name; the dislocation threshold stays per context. A refused change answers 400, which the contract does not list for this endpoint.
- A decision takes its four configurable conditions from the configuration in force when it is recorded, and names that version. Its `dislocation` condition uses the threshold on its dislocation record, set under that record's version. After a configuration change a decision can therefore name a newer version than its composite and dislocation, and a changed dislocation threshold applies from the next dislocation.
- DEPLOY is a research outcome in simulation mode: no order is placed and no position is opened.
- With the uncalibrated default conditions, the day's last decision is IDLE on 30.7% of trading days for equity and 49.2% for oil (2018-01-02 to 2026-09-22, FRED data as of 2026-09-25). SRS DEC-002 verifies at least 70%. Market data alone clears `|composite| ≥ 0.5` and a 1.5-point dislocation on most days. Calibrating the conditions is an operator decision; values tuned on the validation set must carry the "in-sample" label (brief §9.8).

## State gate and outcomes record (ADR-0008)

- The state is a rank of the reference level among its prior closes. It describes where the level sits,
  not where it goes. Pre-registered tests found no tradable rule in it on 2019 onward: spot reversion
  after extreme states is real in the record but priced in VX futures.
- Decisions recorded before ADR-0008 keep the refuted composite gate and directional labels
  (`vol-expansion`, `vol-compression`, `none`) so history replays exactly. They are shown as
  "legacy gate", never as extreme states, and the outcomes record counts them only in the all-days
  baseline and the state table. History under the current gate needs a replay measured with `--replay`.
- A percentile counts only with at least 252 prior closes; below that the state is `unknown` and the
  tail check records 1. The tail condition accepts only `<=` or `<`, so a DEPLOY always records an
  extreme state. The percentile is computed in both regime modes; level boundaries only relabel regimes.
- The 5 percent tail is a design choice for alert frequency, not a fitted parameter. With five years
  of history the percentile is sticky: VIX spends more days in the lower tail than the upper.
- Outcomes are changes from the reference close on the decision's NYSE trading day to the close exactly
  h trading days later; holiday-session prints, missing closes and decisions on another instrument are
  left out and counted. The reversion share counts extreme days, which are strongly autocorrelated;
  intervals use blocks of max(21, 2h) extreme days, but a run of extreme days is still one episode.
- `GET /analytics/outcomes` and `outcomes-report` recompute on every read. Over 15 years that is a few
  seconds; there is no cache.
- A full-history replay stores every decision in one `ReplayRun` ledger entry (tens of megabytes).
