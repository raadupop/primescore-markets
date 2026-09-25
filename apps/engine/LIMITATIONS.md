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
- Business days are Monday to Friday; exchange holidays are not modelled.

## Contract gaps

- The API contract has no endpoint that lists signals; signal views exist in the UI only.
- Basket prices are recorded as CROSS_ASSET_FLOW signals with an adapter payload (`kind: basket_observation`), because the contract's cross-asset payload requires a baseline the source does not provide.
- When a provider revises a value, the first recorded value is kept and the revision is counted, not stored. An API caller that resubmits an observation with a different payload gets a `REJECTED` result naming the recorded signal; nothing is merged.

## Ingestion rules

- The idempotency key is (source identifier, instrument, variant, observation time). The variant separates series that share an instrument: metric type (plus tenor) for market data, indicator type for macro prints (with the reference period for FRED releases), event type for geopolitical events, flow type for cross-asset flows, and a content hash for free text.
- The `fred:` source prefix is reserved for the engine's FRED adapter; API submissions using it are rejected. A macro print names its registry symbol directly (`CPI_YOY`) or after a source name (`econ:CPI_YOY`).
- A classifier reference window is one instrument, one category and one variant, with one point per New York date: where sources overlap on a date, the first recorded wins.
- Incremental FRED pulls always ask FRED; only a backfill reuses a cached response (up to `Fred:CacheHours`), and its provenance carries the time the response was fetched.
