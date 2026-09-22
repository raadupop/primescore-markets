# Answers — 2026-04-27

Companion to [srs-revision-v2.3.3-open-questions.md](srs-revision-v2.3.3-open-questions.md). Each section
gives the PDF's resolution, then a critical assessment (claims to keep, claims to bound,
claims to reject). Source: `srs-revision-v2.3.3-research-source.pdf`.

---

## Q1 — Long-horizon ECDF: window size and stationarity

**PDF answer:**
- VIX |deviation from median| is **not stationary** over a 5-year horizon. Multiple
  regimes inside any 5y window (e.g., 2017 calm vs 2018 vol spike vs 2020 COVID)
  produce structural breaks. "Volatility paradox": low-vol regimes seed their own
  collapse — distributions cannot be merged.
- Minimum N_L for SE < 0.03 at p=0.5 (worst-case binomial variance):
  - $0.03 > \sqrt{0.25 / N_L} \Rightarrow N_L > 277.78$, so **N_L ≥ 278**.
- Daily indicators (VIX, OVX) at 5y: N=1260, max SE ≈ 0.014. **Passes easily.**
- Weekly (Initial Claims, 3y): N=156, max SE ≈ 0.040. **Fails at the median.**
- Monthly (CPI YoY, 5y): N=60, max SE ≈ 0.065. **Fails badly.**
- Recommended fix for sparse data: drop non-parametric ECDF, fit a parametric
  distribution (Gaussian/log-normal) from mean+variance. Extending lookback to
  23+ years to satisfy ECDF would reach back through too many regimes and is
  rejected.

**Critical assessment:**
- The 278 figure is correct elementary statistics. Adopt it.
- The PDF surfaces but does not resolve a contradiction: if VIX |dev| is
  non-stationary across 5y, then the 1260-sample ECDF is *also* methodologically
  suspect — the SE bound only governs sampling error, not specification error
  from mixing regimes. The right answer is a **regime-conditional ECDF**, which
  is exactly what Q11 addresses. Q1 and Q11 are entangled.
- Parametric fit for monthly macro is acceptable *only after* a
  goodness-of-fit test (Shapiro–Wilk on log-CPI surprises). If the data is
  bimodal (recession vs expansion), Gaussian fit is worse than ECDF.

**SRS impact:** CLS-001 amendment specifies (a) cadence-dependent N_L floor of
278 for ECDF, (b) parametric fallback for indicators where N < 278, gated on
GoF test, (c) regime-conditional ECDF deferred to Q11 resolution.

---

## Q2 — Signed severity schema

**PDF answer:** Migrate `score` from `[0, 1]` to **`[-1, +1]` (signed)**. Rationale:
- Absolute value collapses vol-expansion and vol-crush onto the same vector.
- Composite scoring with signed values lets opposing signals cancel cleanly
  (correctly representing low-conviction conflict). Absolute values would falsely
  aggregate opposing forces into high composite scores.
- IV dislocation (CLS-006) needs the sign to dictate sell-premium vs buy-premium.
- Position-type selection in DEC-001/POS-001 needs the sign natively.

**Critical assessment:**
- Adopt `[-1, +1]`. Schema decision before Iteration 1 starts.
- Sign convention must be locked in the SRS: **+1 = vol expansion / underlying
  shock / surprise on hawkish side**, **−1 = vol compression / mean-reversion
  / dovish surprise**. Pick once, document, never flip.
- The CLS-002 formula `Σ w_c × max_confirmed(severity × certainty)` needs an
  explicit decision for signed inputs. Two options:
  - Aggregate by `|score|` for conviction, retain a separate signed direction
    field aggregated by sign-vote with weights;
  - Aggregate signed scores directly; conviction = `|composite|`.
  The second is cleaner and matches the PDF's "opposing signals cancel" model.

**SRS impact:** OpenAPI `score` range update; CLS-002 formula amendment;
sign-convention table added.

---

## Q3 — Corroboration threshold and bypass *(critical path)*

**PDF answer:** Dual-pathway model.
- **High-conviction bypass:** signal exceeds a 4σ anomaly threshold T → corroboration
  waived. Captures flash dislocations where waiting forfeits the move.
- **Below T:** adaptive multi-sensor fusion using covariance/conflict measures
  (down-weight isolated sensors with poor consensus); does not require strict
  same-category corroboration.
- VIX/OVX cross-corroboration: PDF does not answer directly, but its
  conflict-weighted fusion implies cross-category corroboration is permitted
  whenever covariance is non-trivial.

**Critical assessment (statistician):**
- "4σ" is a Gaussian heuristic. VIX |deviation| is fat-tailed and the PDF itself
  acknowledged non-stationarity in Q1. Specifying T as "4σ" on non-Gaussian data
  is methodologically unsound.
- Translate T to an **ECDF percentile** instead: e.g., bypass at long-horizon
  ECDF rank ≥ 0.999. With N=1260, that fires ~1.3 days/5y per indicator —
  realistic for a true outlier.
- **Critical interaction with Q1:** the noise-zone false positives we already
  documented (2017-10-05, 2019-07-15) score 0.90 under current SRS. Whatever
  bypass percentile is chosen must be calibrated against those known false
  positives — bypass must NOT trigger on them. p=0.999 is safe; p=0.90 is not.

**SRS impact:** CLS-002 amendment — replace "same-category corroboration" with
covariance-weighted fusion; add `bypass_percentile` parameter (long-horizon
ECDF rank, default 0.999); confirm against false-positive anchor fixtures.

---

## Q4 — Confirmation timing window

**PDF answer:** Bifurcated by event typology.
- **Intraday microstructure** (order-book imbalances, vol spikes): 100–300 ms.
- **Macro releases** (CPI, NFP, FOMC): 30–60 minutes.

**Critical assessment:**
- 100–300 ms is HFT territory. PrimeScore AI runs over HTTP between .NET and Python
  classifier — round-trip alone is 5–50 ms before any logic. The 100–300 ms
  budget is operationally unreachable.
- Realistic PrimeScore AI targets:
  - Intraday: **1–5 seconds**.
  - Macro: **30–60 minutes** (PDF figure, valid).
- Expired-without-corroboration: PDF doesn't answer. Recommend: signal
  expires (no pending state); next opportunity must re-fire from the
  classifier.

**SRS impact:** CLS-002 amendment — `corroboration_window_seconds` per event
type; AppState signal TTL = window + grace.

---

## Q5 — Source-dropout penalty calibration

**PDF answer:**
- Static 0.7 has no econometric foundation; it is a software-availability
  heuristic misapplied to financial data.
- Outages correlate with stress regimes (MNAR — missing not at random).
- Replace with two mechanisms:
  - **Duration-scaled penalty:** logarithmic in outage duration (microsecond
    dropout ≈ no penalty; multi-minute dropout ≈ severe penalty).
  - **Bayesian imputation:** treat missing source as a latent variable; impute
    from covariance with available correlated sensors. Penalty becomes the
    variance of the imputed value.

**Critical assessment (adversary):**
- Bayesian imputation introduces hidden coupling. If VIX feed is down and
  OVX-imputed VIX is used, the system inherits OVX's crude-specific bias.
- Imputation must be **bounded**: cap |imputed score| at e.g. 0.5, and flag
  the response with a `degraded` reason. Never let imputation produce
  high-conviction scores.
- For Iteration 1 simplicity: implement duration-scaled penalty only; defer
  Bayesian imputation to Iteration 3+ when CLS-002 has a richer object model.

**SRS impact:** CLS-002 amendment — duration-scaled `d_c(Δt)` table per
category; imputation deferred.

---

## Q6 — Real-time MarketObservedIV source

**PDF answer:**
- FRED daily close: unacceptable for live IV dislocation.
- Direct CBOE feeds: μs–ns latency, professional-grade, expensive.
- **Recommended:** Twelve Data WebSocket (`wss://ws.twelvedata.com`) — low
  millisecond latency, no HTTP overhead.
- Staleness threshold: **< 100 ms** for the Newton–Raphson IV inversion
  to remain consistent with current option premium.

**Critical assessment (adversary):**
- Twelve Data **free tier** has aggressive rate limits and tends to throttle
  during peak vol — exactly when the system needs the data. PDF does not
  address this. Production tier is paid.
- 100 ms staleness is fine for the math, but cumulative latency
  (data ingestion + .NET → Python hop + classifier compute + decision engine)
  is the operational constraint, not the IV-update latency alone.

**SRS impact:** CLS-006 amendment — IV source = WebSocket-tier provider;
`infra/registry.yaml` per-symbol IV source field; budget end-to-end latency,
not just feed staleness.

---

## Q7 — SENSITIVITY_FACTOR regime mapping

**PDF answer:** Asset-class-specific regime breakpoints, not a unified scale.

| Regime | VIX | OVX | Mechanics |
|---|---|---|---|
| Low | 0–15 | < 30 | Trend, suppressed premium, high confidence |
| Normal | 15–25 | 30–35 | Mean reversion, balanced books |
| High | 25–30 | 35–50 | Geopolitical, supply shocks, gap-ups |
| Extreme | > 30 | > 50 | Panic, structural outages, margin calls |

Sensitivity behavior:
- **Low regime:** asymmetric *upward* sensitivity — vol expansion is far more
  probable than further compression; mispricing tail risk if static.
- **High regime:** **dampened** sensitivity to avoid chasing transient noise.
- Calibration via central finite-difference on a regime-switching Heston model.

**Critical assessment:**
- Breakpoints are sensible and consistent with VIX historical distribution
  (mean ≈ 19.4%, OVX median ≈ 32.8%).
- PDF gives **no concrete SENSITIVITY_FACTOR values** — only the asymmetric
  shape. Iteration 1 needs numbers. Suggest:
  - Low: 1.3 (asymmetric upward)
  - Normal: 1.0
  - High: 0.7
  - Extreme: 0.5
  Then validate via walk-forward backtest.
- Note: regime classification is itself a Q11 dependency — without a regime
  detector, SENSITIVITY_FACTOR can't be regime-conditional.

**SRS impact:** CLS-006 amendment — breakpoint table per asset class; numeric
SENSITIVITY_FACTOR values to be calibrated; regime detection prerequisite (Q11).

---

## Q8 — Deploy threshold *(critical path)*

**PDF answer:**
- Reject binary threshold; use **tiered Watch / Deploy** model.
  - **Watch:** lower composite score → warm sensors, increase polling, prepare
    execution routes; no capital allocation.
  - **Deploy:** higher composite → allocate capital.
- Calibrate via **multi-variable objective function** (e.g., GT-Score —
  performance + statistical significance + consistency + downside risk),
  NOT max historical return (data snooping).
- No concrete numeric values given.

**Critical assessment (adversary):**
- The PDF dodges the actual number. "Multi-variable objective function" is
  the right methodology but operationally vague.
- For Iteration 1, set initial values via direct read of anchor fixtures:
  - Watch ≥ 0.45 (composite of confirmed signals)
  - Deploy ≥ 0.70
  - Refine via walk-forward backtest before live deployment.
- Threshold must be regime-conditional (Q11 dependency): in extreme regime
  lower the deploy threshold (signals are more reliable); in low regime
  raise it (false positives dominate).

**SRS impact:** DEC-001 amendment — Watch/Deploy tier; numeric defaults
TBD; regime-conditional override table.

---

## Q9 — Theta budget and max hold period

**PDF answer:** This is the most consequential answer in the document.
- For 6–72h convex holds (long straddle/strangle), the dominant risk is **not
  theta** — it is **Vega crush** post-binary-event (CPI, FOMC, earnings).
- Pre-event: IV inflates → both legs gain Vega.
- Event horizon: IV peaks, theta peaks.
- Post-event: IV collapses (vol crush). If realized move < implied move, the
  position is annihilated even on directionally correct moves smaller than
  the implied move.
- Mathematical decision rule: **if the trade thesis was IV expansion into the
  catalyst, exit before the catalyst.** To hold through, expected Gamma
  contribution must demonstrably exceed expected Vega drop.

**Critical assessment (trader):**
- This reframes EXT-001 entirely. Exit timing is **catalyst-relative**, not
  calendar-relative.
- For event-driven PrimeScore AI strategies, the typical lifecycle is:
  - Open position 6–24h before known catalyst, IV expansion drives P&L.
  - Exit at T-30min before catalyst announcement.
  - Hold through only when classifier composite exceeds a high threshold AND
    expected Gamma > Vega drop estimate.
- Theta is secondary but not zero. For non-catalyst-driven volatility events
  (geopolitical surprises with no scheduled time), theta dominates after ~48h.

**SRS impact:** EXT-001 amendment — exit rule = catalyst-relative for scheduled
events, time-based 48h fallback for unscheduled events; DEC-001 must compute
expected Gamma vs Vega-crush before any "hold through catalyst" decision.

---

## Q10 — Position management during evolving events

**PDF answer:**
- Same-direction follow-on signal: add only after weighing against portfolio
  heat and overall risk exposure.
- Opposing-direction signal under signed `[-1, +1]` schema: **reduce delta
  exposure** on existing position rather than open a new opposing position
  (avoids round-trip bid-ask costs).
- **Global position size cap is non-negotiable** — hardcoded, derived from
  Monte Carlo of worst historical drawdown across many random seeds.

**Critical assessment (risk):**
- "Reduce delta exposure" is correct in principle but the PDF doesn't specify
  *by how much*. Default rule for Iteration 1:
  - Opposing signal with `|score| ≥ 0.5` → close 100%.
  - Opposing signal with `|score| ∈ [0.3, 0.5)` → close 50%.
  - Below 0.3 → ignore (noise-zone).
- Global cap should be specified as **% of risk capital per position** AND
  **% of total portfolio at risk** simultaneously. Single cap is insufficient.

**SRS impact:** POS-001 + EXT-001 amendment — opposing-signal action table;
DEC-001 portfolio-heat check; per-position and aggregate caps.

---

## Q11 — Regime detection: needed or deferred?

**PDF answer:** **Strictly required.**
- Implicit ECDF context fails: rolling ECDF absorbs new extremes into "the
  new normal" over weeks, dampening severity scores during persistent
  regime shifts (memory decay).
- Recommended: **Adaptive Hierarchical Hidden Markov Model (AH-HMM)** —
  meta-regime layer over base HMM. Latent states: Equity Trend, Normal,
  Rate Shock, Volatility Shock. Observable: log returns, variance, bid-ask
  spread, macro indicators.
- All downstream parameters (deploy threshold Q8, SENSITIVITY_FACTOR Q7,
  position sizing, max hold Q9) must conditionally bind to the latent state.

**Critical assessment (adversary):**
- AH-HMM is a heavy lift: Baum-Welch fitting, state interpretation,
  validation against turning points, and operational stability. This is
  research-grade infrastructure for Iteration 1's Transaction Script baseline.
- Pragmatic phasing:
  - **Iteration 1:** threshold-based VIX/OVX regime classifier
    (the breakpoint table from Q7). Crude but operational.
  - **Iteration 3+ (Clean Architecture):** introduce HMM, replace threshold
    classifier without touching downstream consumers.
  - AH-HMM if/when meta-regime adaptation justifies the complexity.
- The PDF's "strictly required" framing is correct *for production trading*,
  but PrimeScore AI is also a measurement vehicle. Adopting AH-HMM in Iteration 1
  would make the classification service no longer constant infrastructure
  across iterations — violates ACX-001.

**SRS impact:** New requirement (CLS-010) — regime-state output, with
"threshold-based" as the v1 implementation and HMM as a roadmap item.
Downstream params (Q7, Q8, Q9) gain `regime` parameter.

---

## Critical-path summary

Phase legend:
- **SRS pre-work** = must land in SRS amendment + OpenAPI v2 *before* Iteration 1
  starts. Touches the constant Python classification service (ACX-001) or the
  external API contract — both are frozen for the duration of an iteration.
- **Iteration 1** = implementable inside the .NET Transaction Script baseline
  using the contract from SRS pre-work. These items live entirely in the .NET
  app (composite scoring, decision engine, position management, regime stub) —
  no change to the Python classification service or the OpenAPI contract.
- **Iteration 3+** = research-grade work; defer until the Clean Architecture
  rewrite when the cost of swapping internals is low.
- **Deferred** = needs calibration data, paid feed, or further research.

| # | Phase | Resolution | Blockers / dependencies |
|---|---|---|---|
| Q1 | **SRS pre-work** | N_L ≥ 278; parametric fallback for sparse macro; regime-conditional ECDF deferred to Iter 3+ | Coupled with Q2/Q3/Q11-stub — single CLS-001 amendment |
| Q2 | **SRS pre-work** | Signed `[-1, +1]`; lock sign convention | Cascades to CLS-002 (Q3), DEC-001 (Q8), POS-001 (Q10) — must ship together |
| Q3 | **SRS pre-work** | Bypass at long-horizon ECDF percentile ≥ 0.999 (NOT 4σ) | Needs Q2 signed schema; covariance fusion deferred to Iter 3+ |
| Q4 | **SRS pre-work** | Intraday 1–5 s, macro 30–60 min (PDF's 100–300 ms unreachable) | Defines AppState TTL — must be in contract |
| Q5 | **Iteration 1** | Duration-scaled `d_c(Δt)` penalty | Bayesian imputation deferred to Iter 3+ |
| Q6 | **Deferred** | WebSocket-tier IV; ≤ 100 ms staleness | Needs paid data feed; Iter 1 can use FRED close for backtest only, not live |
| Q7 | **Iteration 1** | Asset-class regime breakpoints (PDF table) | SENSITIVITY_FACTOR numeric values calibrated in walk-forward (deferred) |
| Q8 | **Iteration 1** | Tiered Watch/Deploy, provisional 0.45 / 0.70 | Final values need walk-forward; regime override needs Q11-stub |
| Q9 | **SRS pre-work** | Catalyst-relative exit rule; Gamma vs Vega-crush gate | Reframes EXT-001 — must be in SRS before Iter 1 builds exits |
| Q10 | **Iteration 1** | Opposing-signal close ladder (100% / 50% / 0%); per-position + aggregate caps | Depends on Q2 signed schema |
| Q11 | **Iter 1 (stub) + Iter 3+ (HMM)** | Threshold classifier in .NET (preserves ACX-001); HMM/AH-HMM after Iter 6 | Stub satisfies Q7/Q8 regime-binding; full HMM is research track |

**Coordinated SRS pre-work bundle (must land together):**
Q1 + Q2 + Q3 + Q4 + Q9. These touch CLS-001, CLS-002, EXT-001, and the
OpenAPI score schema. Shipping any one without the others leaves downstream
consumers (Iteration 1's .NET app) with an incoherent contract.

**Findings that override or refine the PDF:**
- **4σ → 0.999 ECDF percentile** (Q3): Gaussian heuristic on fat-tailed data
  is unsound; use the empirical distribution we already maintain.
- **100–300 ms intraday → 1–5 s** (Q4): HFT figure operationally unreachable
  for PrimeScore AI's HTTP-coupled architecture.
- **Imputation bounded** (Q5): Bayesian imputation cannot produce
  high-conviction scores; cap and flag.
- **AH-HMM phased** (Q11): full HMM is Iteration 3+ work; threshold classifier
  for Iteration 1 to preserve classification service as constant infrastructure.

**Cross-cutting dependency:** Q1 (regime non-stationarity) and Q11 (regime
detection) are the same problem at different layers. Resolving Q11 with even
a simple threshold classifier feeds back into a regime-conditional ECDF,
which is the structurally correct fix to the noise-zone false positives.
