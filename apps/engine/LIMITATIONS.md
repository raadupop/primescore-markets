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

- One ADMIN account signs into the browser with a password hash from user-secrets or environment variables. READ and ADMIN API tokens remain separate. There is no account recovery service; reset the password locally with the [setup command](../../docs/ENGINE.md#install-and-sign-in).
- Configuration editing uses JSON with validation and rejects saves based on an older version. It does not calibrate parameters or validate an operator's claim that settings were estimated independently.
- Local Development uses HTTP on loopback; HTTPS and service supervision for a hosted installation require operator setup. Passwords, token hashes and ASP.NET data-protection keys are outside the repository and must be protected with the service account's filesystem permissions.

## Data coverage (FRED)

- **VVIX has no FRED series.** The registry maps VVIX to `VVIXCLS` (`verified: false`); FRED answers "series does not exist". Every pull reports it as not pulled; no VVIX value exists in the engine.
- **SP500 on FRED starts 2016-09-26** (FRED's licensed window), not 2011. The other basket series start 2011.
- **EVZ ends 2025-03-11** on FRED; later dates have no EVZ observation.
- **Missing values stay missing.** Observations FRED reports as `.` are skipped and counted, never filled.

## Point-in-time timing

- Daily index closes are stamped 16:15 America/New_York on the observation date. FRED publishes them the next morning, but the value was public at the close.
- CPI and initial claims use each value as **first released** (FRED `output_type=4`) and are stamped 08:30 America/New_York on the first release date. CPI YoY is `100 × (index_t / index_t−12 − 1)` from first releases of the **seasonally adjusted** `CPIAUCSL`; published headline YoY and most consensus figures use the unadjusted index, which can differ by about 0.1 percentage point.
- Basket prices (SP500, DGS10, DCOILWTICO, DEXUSEU) are stamped 16:00 America/New_York on the observation date. FRED may publish them days later (DCOILWTICO about a week, DEXUSEU weekly), so they were not necessarily public at that stamp. They are not classified in v1.
- The classifier's `business_day` cadence counts Monday to Friday and ignores exchange holidays. The engine's composite windows and reporting intervals count NYSE trading days, excluding the rule-based holidays and the one-off closures since 2001 (ADR-0004 §4, §5). A future one-off closure is not known in advance and reads as a trading day without data.

## Contract gaps

- The API contract has no endpoint that lists signals; signal views exist in the UI only.
- Basket prices are recorded as CROSS_ASSET_FLOW signals with an adapter payload (`kind: basket_observation`), because the contract's cross-asset payload requires a baseline the source does not provide.
- When a provider revises a value, the first recorded value is kept and the revision is counted, not stored. An API caller that resubmits an observation with a different payload gets a `REJECTED` result naming the recorded signal; nothing is merged.

## Ingestion rules

- The idempotency key is (source identifier, instrument, variant, observation time). The variant separates series that share an instrument: metric type (plus tenor) for market data, indicator type for macro prints (with the reference period for FRED releases), event type for geopolitical events, flow type for cross-asset flows, and a content hash for free text.
- The `fred:` source prefix is reserved for the engine's FRED adapter; API submissions using it are rejected. A macro print names its registry symbol directly (`CPI_YOY`) or after a source name (`econ:CPI_YOY`).
- A classifier reference window is one instrument, one category and one variant, with one point per New York date: where sources overlap on a date, the first recorded wins.
- Incremental FRED pulls always ask FRED; only a backfill reuses a cached response (up to `Fred:CacheHours`), and its provenance carries the time the response was fetched.

## Classification (M2)

- Only MARKET_DATA signals and macro prints with a sourced consensus row are classified. Geopolitical events and cross-asset flows (including the basket prices) are recorded as **route not in v1**: the classifier answers 501 for those categories, and change #2 (cross-asset) is an open operator decision.
- No consensus source has been chosen yet, so `data/consensus/*.csv` hold headers only and every macro print is **awaiting consensus**. A row needs its consensus source, URL and retrieval time; rows without them are rejected and listed on Sources and health.
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
