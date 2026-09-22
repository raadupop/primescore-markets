# Known Limitations

Production-critical weaknesses tracked here. Each entry includes the risk and what a production-grade fix looks like.

---

## 1. ~~Fixed `_TANH_SCALE` calibration~~ — ADDRESSED

**Affects:** MARKET_DATA, MACROECONOMIC (and future CROSS_ASSET_FLOW) strategies

**Status:** Addressed by ADR-0001 Phase B + ADR-0002. Severity is now an ECDF rank against the per-symbol rolling history; there is no `_TANH_SCALE` constant. The remaining per-class calibration knob is `N` (history length) in [`infra/registry.yaml`](../../infra/registry.yaml), paper-computable per indicator without a magic scale constant. The earlier `D` (minimum-informative-dispersion floor) was removed by the ADR-0002 2026-04-27 amendment. Empirical calibration of `N` itself is tracked separately as #6.

---

## 2. ~~No window staleness detection~~ — ADDRESSED

**Affects:** All strategies using rolling windows (MARKET_DATA, MACROECONOMIC, CROSS_ASSET_FLOW)

**Status:** Addressed. Certainty is split into two independently computed dimensions:
- `history_sufficiency`: window fullness (`min(1.0, len(history)/N)`) — the original certainty formula, renamed honestly per ADR-0004
- `temporal_relevance`: exponential decay based on time since last update, relative to the indicator class's `expected_frequency_seconds` (sourced from [`infra/registry.yaml`](../../infra/registry.yaml) per ADR-0001 Phase B)
- `certainty = history_sufficiency × temporal_relevance`

The `RollingWindow` class in `app/state.py` tracks `last_update` alongside values. The `/health` endpoint exposes per-window staleness (last update timestamp + seconds since). Different indicator cadences (daily vol indices, monthly CPI, weekly claims) are now correctly distinguished by class — weekly INITIAL_CLAIMS uses 7-day expected frequency rather than the previous shared 30-day constant.

---

## 3. ~~Fixed `tanh` curve for severity mapping~~ — ADDRESSED

**Status:** Addressed by ADR-0002. Severity is now `ecdf_rank(|deviation|)` against the per-symbol rolling history, not `tanh(value / scale)`. No assumed curve shape, no tuning constant, adapts as the distribution shifts. Calibration of the per-class `N` (history length) is tracked separately as #6. The earlier `D` (minimum-informative-dispersion floor) was removed by the ADR-0002 2026-04-27 amendment and replaced with a global window-degeneracy check.

---

## 4. Test events not validated for exploitability

**Affects:** All 10 historical events in the test suite (AGENTS.md)

**Risk:** The test suite validates that the classifier produces correct severity scores from real data. It does not validate that those scores would have led to profitable trades. Exploitability depends on the full pipeline: composite scoring (CLS-002) aggregates multiple signals, IV dislocation detection (CLS-006) compares signal-implied IV to market-observed IV, and the decision engine (DEC-001) determines whether to deploy capital. A high severity score alone does not mean an event was exploitable.

**Current state:** The Phase 1 convexity analysis tags events with `exploitable: true/false` and `windowHours`, but these are manual estimates based on a rule-of-thumb ("6+ hours and 15%+ spike"), not computed from options market data.

**Production fix:**

- Validate exploitability via the .NET replay system (ANA-001), which replays historical signals through the full composite → dislocation → decision → position pipeline
- Use real intraday IV data (CBOE, OptionMetrics) to measure the actual dislocation window for each event
- Only events with confirmed positive P&L in replay should be considered exploitable

---

## 5. Self-validating-loop bug class at the per-signal severity layer

**Status:** PLANNED — two-part remediation scoped in [ADR-0002](doc/adr/0002-ecdf-severity-and-backtest-harness.md).

**Affects:** All RULE_BASED strategies (MARKET_DATA, MACROECONOMIC, CROSS_ASSET_FLOW). Root cause of #1 (scale fitting) and #3 (fixed `tanh` curve) — those are symptoms of the same underlying gap.

**Risk:** CLS-001 in the SRS is deliberately qualitative ("severity is quantified; certainty has two independent dimensions combined somehow") with no formula. An AI agent authoring both the strategy and its tests in one pass can encode the same wrong assumption in both — the acceptance suite validates the contract shape and anchor bands, but cannot validate that the formula itself is right, because there is no independent formula to compare against. The 4-fixture axis-coverage work (OVX Aramco, VIX COVID mid-crisis, VIX vol crush, VIX normal day) collapsed to regression guards: their bands reflect whatever the classifier currently outputs, not a paper-computed reference.

**Current state:** Acceptance suite + architectural fitness layer cannot catch this class. Structurally invisible to the existing harness.

**Production fix (two parts, together):**

1. **Tighten CLS-001 with a normative formula.** Landed in the [SRS](../../doc/srs/PrimeScore-SRS.md): ECDF / percentile rank makes severity paper-computable per indicator without a magic scale constant. The formula is inline in the revised CLS-001, matching the CLS-002 / CLS-006 pattern. Companion SRS changes are CLS-009 (RULE_BASED degraded-confidence fallback for the global window-degeneracy guard and unknown indicators; distinct from CLS-004, which is AI-response-specific) and §3 Definitions entries anchoring the vocabulary including the indicator registry. SIG-001 is preserved verbatim; the registry is not a numbered sub-requirement. See [ADR-0002](doc/adr/0002-ecdf-severity-and-backtest-harness.md); the earlier [CLS-001 annex stub](doc/adr/srs-annex-cls-001-severity-formula.md) is superseded by the SRS body.
2. **Add a backtest harness layer with a market-reality oracle.** Acceptance suite asserts the SRS contract (formula-based, deterministic). Backtest Layer A asserts that the classifier's output is consistent with what IV actually did post-event (binary direction + magnitude bucket). Different oracle, different bug class, no duplication. Architectural decision in [ADR-0003](doc/adr/0003-test-oracle-architecture.md); inferable inventory in [HARNESS.md](HARNESS.md).

---

## 6. Registry parameter `N` is operator-set, not anchor-validated

**Status:** PLANNED — `N` calibration via trader sanity gate is `/trader` + `/statistician` work, separate from registry mechanics. The earlier `D` half of this limitation was removed entirely by the ADR-0002 2026-04-27 amendment: the per-class dispersion floor is replaced by a global window-degeneracy check (no calibration ever needed).

**Affects:** All RULE_BASED strategies once ECDF lands ([ADR-0002](doc/adr/0002-ecdf-severity-and-backtest-harness.md)).

**Risk:** [`infra/registry.yaml`](../../infra/registry.yaml) ships with `N` values (504 / 60 / 156) chosen from operator + `/trader` + `/statistician` reasoning, not anchor-validated against the 10 historical events. `/statistician` notes percentile resolution is `1/N`: 504 gives 0.2pp at the tail (fine for separating p99 from p99.5, where capital decisions live); 60 gives 1.7pp (the resolution ceiling for monthly inflation given the 5y window cannot grow without crossing the 2020 regime break); 156 gives 0.6pp (3y weekly claims; tightening to 104 would drop the COVID artifact at the cost of resolution). `/trader` notes the score's *meaning* depends on `N` — "severity 0.85" against 504 daily values means something different from 0.85 against 252; the trader's intuition table must confirm the chosen `N` produces severities that match desk experience for the anchor events.

**Sample-size constraint** (`/statistician`): the sample-size argument that previously haunted `D` calibration (≥ 200 non-overlapping IQR observations) does not apply to `N` — `N` is a structural choice, not a parameter estimated from a distribution. The choice is bounded above by regime stability (don't span a regime break) and below by tail resolution (`1/N` percentile-point gap at the extreme).

**Current state:** Registry exists with operator-set values. ECDF code consumes them. Trader sanity gate has not been run.

**Cadence:** `N` calibration is one-shot per class, not continuous. The runtime rolling window is what adapts to fresh data — `N` is a *structural* parameter set deliberately and left alone. Re-trigger only on (a) a regime break (e.g., the next inflation regime change), (b) adding a new symbol to an existing class and confirming class parameters still hold, or (c) the trader sanity gate failing on a new anchor. JVM-heap-size discipline, not weather-forecast discipline.

**Production fix (`N` calibration protocol):**

1. **Data pull.** Per class, pull the longest reasonable history from the verified provider:
   - `equity_vol_index`, `commodity_vol_index`: 10y daily (FRED).
   - `us_inflation_yoy`: 30y monthly (FRED).
   - `us_labor_weekly`: 15y weekly (FRED).
2. **Compute the `|deviation|` series independently of the classifier.** Implement `deviation_kind` directly from the SRS definition in the calibration script (e.g. pandas `pct_change`, explicit YoY arithmetic). **Do not import `app.bootstrap.window_builders`** — that's the ADR-0002 self-validating-loop trap in a different costume. A bug shared between calibration and runtime would launder itself into the calibrated `N` and become invisible. The two implementations agreeing under cross-check is part of the calibration's evidence.
3. **`N` candidate evaluation.** For each class and each candidate `N` in a small grid (e.g. `{252, 504, 756}` for daily-vol classes), bootstrap-resample the `|deviation|` series and compute rank stability for ≥ 5 anchor events per class. Prefer the smallest `N` that holds rank variance ≤ ±5 percentile points *and* respects regime-stability bounds (don't span a known regime break). Document the per-event variance table in the calibration commit.
4. **Trader sanity gate.** Run all 10 anchor events through the chosen `N` and produce the severity-vs-trader-intuition table. Block the merge if any anchor's severity diverges by more than 0.25 from `/trader`'s intuition score without an articulated reason.
5. **Regime-bucketed validation.** Report severity distributions within VIX-bucketed regimes (calm: VIX < 15, normal: 15–25, elevated: 25–40, crisis: > 40). Document divergence per class — flat severity across regimes is either a feature (events are events) or a bug (regime layer is missing); the calibration commit must say which.
6. **Liquidity-window pairing.** Optional in this iteration but blocking before live capital: pair anchor severity with measured intraday IV reaction window from CBOE quotes. Validates that the rank is *useful*, not just statistically sound.

Calibrated `N` values get a `# rationale: …` comment in [`infra/registry.yaml`](../../infra/registry.yaml). Reviewers reject PRs that change `N` without one.

**Reversibility:** Trivial. `git revert` on the registry change. CoBW: bounded — affects severity scores between deploy and recalibration; black-box acceptance fixtures pin to bands and would surface gross drift.

---

## 7. Multi-symbol-per-class is structurally untested

**Status:** ACCEPTED — covered by ADR-0002 design; will be exercised when ECDF lands.

**Affects:** All classes in [`infra/registry.yaml`](../../infra/registry.yaml) with multiple symbols (`equity_vol_index`, `commodity_vol_index`, `us_inflation_yoy`, `us_labor_weekly`).

**Risk:** Current acceptance fixtures cover one symbol per class (`VIX`, `OVX`, `CPI_YOY`, `INITIAL_CLAIMS`). The dedup justification in ADR-0002 assumes that tuning `N` or `D` for a class behaves correctly across all member symbols. Until a fixture exercises a second member of any class, this is asserted rather than tested.

**Current state:** Registry declares 13 symbols across 4 classes. Acceptance fixtures exercise 4 of them.

**Production fix:** Add at least one fixture for a second member of each multi-symbol class:

- `equity_vol_index`: a VVIX or VXN anchor (e.g., VVIX during 2018 Volmageddon)
- `commodity_vol_index`: a GVZ anchor (e.g., gold spike on geopolitical event)
- `us_inflation_yoy`: a CORE_CPI_YOY anchor (e.g., 2022 core inflation peak)
- `us_labor_weekly`: a CONTINUED_CLAIMS anchor (e.g., 2020 COVID labor shock)

`/trader` curates dates per `ANCHORS.md`, `/statistician` reviews that the resulting severity ranks are consistent with same-class siblings under shared parameters.
