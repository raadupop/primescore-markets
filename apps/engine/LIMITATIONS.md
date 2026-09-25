# Engine limitations

Limits that hold even when every check passes. Architecture and ledger limits are in
[ADR-0003](../../doc/adr/0003-engine-modular-monolith-over-hash-chained-ledger.md).

## Data coverage (FRED)

- **VVIX has no FRED series.** The registry maps VVIX to `VVIXCLS` (`verified: false`); FRED answers "series does not exist". Every pull reports it as not pulled; no VVIX value exists in the engine.
- **SP500 on FRED starts 2016-09-26** (FRED's licensed window), not 2011. The other basket series start 2011.
- **EVZ ends 2025-03-11** on FRED; later dates have no EVZ observation.
- **Missing values stay missing.** Observations FRED reports as `.` are skipped and counted, never filled.

## Point-in-time timing

- Daily index closes are stamped 16:15 America/New_York on the observation date. FRED publishes them the next morning, but the value was public at the close.
- CPI and initial claims use each value as **first released** (FRED `output_type=4`) and are stamped 08:30 America/New_York on the first release date. CPI YoY is `100 × (index_t / index_t−12 − 1)` from first releases of the **seasonally adjusted** `CPIAUCSL`; published headline YoY and most consensus figures use the unadjusted index, which can differ by about 0.1 percentage point.
- Basket prices (SP500, DGS10, DCOILWTICO, DEXUSEU) are stamped 16:00 America/New_York on the observation date. FRED may publish them days later (DCOILWTICO about a week, DEXUSEU weekly), so they were not necessarily public at that stamp. They are not classified in v1.
- The classifier's `business_day` cadence counts Monday to Friday and ignores exchange holidays. The engine's composite windows and reporting intervals count NYSE trading days: the rule-based holidays plus the one-off closures since 2001 (ADR-0004 §4, §5). A future one-off closure is not known in advance and reads as a trading day without data.

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
- In daily data only market data and cross-asset flows can corroborate each other; a macro print counts for 30 minutes after its release, so a composite recorded at the close does not include the morning's print. Geopolitical events are never classified in v1, so that category is always absent.
- A composite is recorded when a member's assessment is recorded and is never recomputed: a configuration change applies from the next composite, and a late value for an earlier date adds a composite at that date without changing later ones.
- `threshold_breached` compares the dislocation's magnitude with the threshold, so a vol-compression dislocation can breach it (ADR-0004 §6); the contract's description says `dislocation_value >= threshold`.
- `PUT /config/dislocation-threshold` answers 400 with the problems for a value the formulas cannot use; the contract lists only 200 for it. The SRS example sensitivity map (1.5 / 1.0 / 0.6) is refused.
- The regime percentile ranks the reference level among its previous `N_L` (1260) closes; the history starts in 2011, so until about 2016 it ranks against fewer closes (the count is on each dislocation record).
- `component_scores[].assessment_count` counts the confirmed assessments that entered the category's net conviction.
- Settings can be changed through the API with an ADMIN token; editing from the Configuration screen arrives with the operator sign-in (M6).
