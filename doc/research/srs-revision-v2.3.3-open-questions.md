# Open Questions — 2026-04-27

Raised during classifier work (ADR-0002, ECDF formula, false-positive anchors).
Each question is self-contained. Answer in any order. Answers feed SRS amendments,
ADR entries, or registry configuration — noted per question.

---

## CLS-001 — Classifier formula

### Q1 — Long-horizon ECDF: window size and stationarity

**Context:** The current ECDF ranks today's |deviation| against a 20-trading-day
rolling window. This is regime-locked: a calm window makes any drift look
top-decile. The proposed fix ranks against a long-horizon window (~5 years,
~1260 trading days for daily indicators).

**Questions:**
- For VIX specifically: is the distribution of daily |deviation from median|
  stationary over a 5-year horizon, or does it shift across vol regimes
  (2017 calm, 2018 spike, 2020 crisis)?
- What is the minimum long-horizon window size N_L before the ECDF percentile
  standard error drops below 0.03?
- For weekly indicators (INITIAL_CLAIMS, N=156): does a 5-year long-horizon
  window have enough observations, or does the per-indicator window need
  a different lookback?
- For monthly indicators (CPI_YOY, N=60): same question.

**Answer feeds:** SRS CLS-001 amendment, `IndicatorClass` registry field
`long_horizon_n`, bootstrap data pull per symbol.

---

### Q2 — Signed severity: schema and downstream impact

**Context:** Current `score` is `[0, 1]`. Absolute value discards direction.
VIX up-spike and vol crush both score the same. Position type depends on
direction (spike → LONG_STRADDLE; crush → sell-vol or PUT_SPREAD).

**Questions:**
- Should `score` become `[-1, +1]` (signed, direction preserved), or should
  direction be a separate field (`score: [0,1]` + `direction: UP | DOWN`)?
- Which downstream layers consume `score` directly and would need schema updates:
  CLS-002 composite formula, CLS-006 IV dislocation, DEC-001 decision, POS-001?
- Is the CLS-002 formula compatible with signed scores, or does it need an
  absolute-value step?

**Answer feeds:** PrimeScore-API-v1.yaml schema change, SRS CLS-001 + CLS-002
amendment, OpenAPI `score` field range.

---

## CLS-002 — Composite scoring

### Q3 — Corroboration: threshold and bypass  *(critical path)*

**Context:** CLS-002 uses `max_confirmed` — only counts signals that have at
least one corroborating signal within the same source category. This kills
early single-instrument events (flash crash, overnight gap before markets open).

**Questions:**
- What is the minimum corroboration requirement that suppresses noise without
  killing early signals? Options: (a) at least 1 confirming signal in same
  category, (b) at least 1 in any category, (c) no corroboration required if
  severity > threshold T.
- If a high-conviction bypass is added (severity > T → corroboration waived),
  what should T be? Back-test against anchor events: which events would have
  triggered the bypass?
- Should OVX and VIX corroborate each other (both MARKET_DATA)?

**Answer feeds:** SRS CLS-002 amendment (corroboration rule),
DEC-001 high-conviction path.

---

### Q4 — Confirmation timing window

**Context:** Events unfold in time. VIX fires at 9:35am; CROSS_ASSET_FLOW
may not arrive until 9:37am. CLS-002 needs to know whether to wait for
corroboration or act on the first signal.

**Questions:**
- What is the maximum acceptable latency between initial signal and
  corroborating signal for the composite to treat them as the same event?
- Does the confirmation window differ by event type (intraday flash vs
  macro release vs geopolitical)?
- If the window expires with no corroboration, does the initial signal expire
  or stay pending for the next corroboration opportunity?

**Answer feeds:** SRS CLS-002 amendment (timing), signal TTL in AppState.

---

### Q5 — Source-dropout penalty calibration

**Context:** When a category is expected but unavailable, CLS-002 applies
`d_c = 0.7` (configurable default). No derivation stated.

**Questions:**
- What is the rationale for 0.7?
- Should the penalty scale with how long the source has been unavailable
  (e.g. 0.9 for < 5 min, 0.7 for 5–30 min, 0.5 for > 30 min)?
- What penalty makes the composite conservative enough to avoid a false deploy
  but not so conservative that a real event with one dead feed is suppressed?

**Answer feeds:** SRS CLS-002 amendment, registry per-category dropout config.

---

## CLS-006 — IV Dislocation

### Q6 — Real-time MarketObservedIV source

**Context:** CLS-006 requires MarketObservedIV. VIX from FRED is a daily
close — stale during live intraday events.

**Questions:**
- What data source provides intraday VIX (or OVX) at acceptable latency and
  cost? Options: CBOE real-time (paid), Twelve Data, Yahoo Finance (unofficial),
  broker API.
- What is the acceptable staleness threshold before the dislocation calculation
  is unreliable?
- Is OVX available intraday from a free or low-cost source?

**Answer feeds:** SRS CLS-006 amendment, `infra/registry.yaml` per-symbol
IV source field.

---

### Q7 — SENSITIVITY_FACTOR regime mapping

**Context:** CLS-006 says SENSITIVITY_FACTOR is "higher in low-vol, lower in
high-vol" and "configurable." No values specified and no validation methodology
given.

**Questions:**
- What VIX level breakpoints define low/medium/high vol regimes?
- What SENSITIVITY_FACTOR values correspond to each regime?
- What is the validation methodology?

**Answer feeds:** SRS CLS-006 amendment, registry config for
regime-sensitivity mapping.

---

## DEC-001 — Decision engine

### Q8 — Deploy threshold  *(critical path)*

**Context:** No composite score threshold is specified anywhere in the SRS.
This is the single most consequential number in the system.

**Questions:**
- What composite score threshold triggers a deploy decision?
- Single threshold or two-tier (e.g. > 0.6 = watch, > 0.8 = deploy)?
- Should the threshold be regime-dependent?
- What is the cost asymmetry: how much worse is a missed real event vs a
  false deploy? This drives where the threshold sits.

**Answer feeds:** SRS DEC-001 amendment (deploy threshold), registry config.

---

### Q9 — Theta budget and max hold period

**Context:** Options bleed theta every day. No time-based exit rule exists
in the SRS. A straddle held 10 days in a slow-moving event loses significant
premium.

**Questions:**
- What is the maximum hold period per position type (LONG_STRADDLE,
  LONG_STRANGLE, PUT_SPREAD, CALL_SPREAD)?
- Is the exit rule time-based, P&L-based, or signal-based?
- At what daily theta cost does remaining edge go negative?

**Answer feeds:** SRS EXT-001 amendment, DEC-001 max-hold field.

---

### Q10 — Position management during evolving events

**Context:** SRS POS-001 describes opening positions. Silent on what happens
when new signals arrive while a position is already open.

**Questions:**
- New signal same direction while position is open: add, hold, or do nothing?
- New signal opposite direction (vol crush while long straddle open): close,
  hedge, or ignore?
- Is there a maximum position size cap regardless of signal strength?

**Answer feeds:** SRS POS-001 + EXT-001 amendment, DEC-001 position rules.

---

## Regime detection — not yet in SRS

### Q11 — Regime model: needed or deferred?

**Context:** VIX 25 in a calm 2019 regime differs from VIX 25 after a week of
40+ readings. No regime-detection mechanism exists in the SRS. SENSITIVITY_FACTOR
in CLS-006 gestures at it without specifying it.

**Questions:**
- Is a formal regime model required for Iteration 1, or can regime context be
  implicit in the long-horizon ECDF (Q1)?
- If needed: threshold-based VIX buckets (simplest), HMM (principled), or
  rolling vol-of-vol percentile?
- Which decisions are regime-dependent: deploy threshold (Q8),
  SENSITIVITY_FACTOR (Q7), position sizing, max hold (Q9)?

**Answer feeds:** New SRS requirement (CLS-010 or similar), or fold into
CLS-001 long-horizon amendment.

---

## Summary

| # | Layer | Answer feeds |
|---|---|---|
| Q1 | CLS-001 | SRS amendment, registry `long_horizon_n` |
| Q2 | CLS-001 | API contract change, CLS-002 formula compat |
| Q3 | CLS-002 | SRS CLS-002, DEC-001 bypass — **critical path** |
| Q4 | CLS-002 | SRS CLS-002, AppState TTL |
| Q5 | CLS-002 | SRS CLS-002, registry config |
| Q6 | CLS-006 | SRS CLS-006, infra source spec |
| Q7 | CLS-006 | SRS CLS-006, registry config |
| Q8 | DEC-001 | SRS DEC-001 — **critical path** |
| Q9 | DEC-001/EXT-001 | SRS EXT-001, max-hold config |
| Q10 | POS-001/EXT-001 | SRS POS-001 + EXT-001 |
| Q11 | Regime (new) | New SRS req or fold into Q1 |

Q8 (deploy threshold) and Q3 (corroboration) are load-bearing for
end-to-end validation. Everything else is refinement.
