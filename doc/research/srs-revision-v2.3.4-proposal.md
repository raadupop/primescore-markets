# SRS v2.3.4 revision proposal: CLS-002, CLS-006, DEC-001 and the validation set

- **Target SRS version:** v2.3.4 (from v2.3.3 by applying the blocks below)
- **Source SRS:** [doc/srs/PrimeScore-SRS.md](../srs/PrimeScore-SRS.md)
- **Status:** Proposed; the operator accepts amendments. The SRS text is not edited here.
- **Date:** 2026-09-25
- **Relates to:** [ADR-0004](../adr/0004-composite-and-dislocation-resolutions.md), which records the
  engine decisions and their trade-offs; this note holds only the proposed requirement text.

Each block replaces or adds text in the named section. Values marked *(default)* are
configuration, seeded as "uncalibrated default".

## 1. §3 Definitions — new and amended entries

- **Context.** A reference instrument, the source categories expected for it, and the
  instruments that belong to each category. Defaults: `equity` (reference VIX; market data
  VIX, VXN, RVX; macro CPI_YOY, INITIAL_CLAIMS; cross-asset SP500, DGS10, DEXUSEU) and
  `oil` (reference OVX; market data OVX; cross-asset DCOILWTICO, DEXUSEU).
- **Corroboration window** (amended). Adds `daily_market_data = 2 trading days` and
  `daily_cross_asset = 2 trading days` *(default)*: the evaluation day and the previous
  trading day on the New York calendar. `macro_release = 1800 s` is unchanged.
- **Confirmed assessment.** An assessment with another assessment of the same category
  from a different signal inside its window, or with `|severity| ≥ p_bypass`. A CLS-004
  fallback may be confirmed but never confirms another assessment; an assessment with
  certainty 0 neither contributes nor confirms.
- **Category staleness `Δt_c`.** The time by which the category's newest assessed
  observation is overdue against its reporting interval (1 trading day for daily
  categories, 1800 s for macro releases) *(default)*; 0 when not overdue.

## 2. CLS-002 — replace the formula block

> CompositeScore = Σ_{c∈P} w_c · d_c(Δt_c) · s_c / Σ_{c∈P} w_c

where `P` is the set of the context's expected categories with at least one confirmed
assessment in its window; `s_c = max⁺_c − max⁻_c`, the largest positive
`severity × certainty` among confirmed assessments of `c` minus the largest magnitude
among the negative ones (each 0 when none); `d_c(Δt_c) = 1` when `Δt_c = 0`, otherwise the
dropout schedule (0.95 for `Δt < 5 min`, 0.7 for `5 ≤ Δt < 30 min`, 0.5 beyond)
*(default)*. An expected category outside `P` is excluded from both sums and reported as
absent with its reason. CompositeScore lies in `[−1, 1]` by construction.

**Rationale addition.** The v2.3.3 form divides by `Σ w_c d_c`, which pushes a stale
category's composite outside `[−1, 1]`; the arg-max selection cannot cancel opposing
signals, which the verification requires.

## 3. CLS-006 — add after the formula

SENSITIVITY_FACTOR shall lie in `(0, 1]`; configuration outside that interval is rejected,
so SignalImpliedIV is never negative. The volatility regime is the ECDF percentile of the
reference instrument's observed level within its own previous `N_L` observations: `low`
below 0.30, `high` above 0.70, `normal` between *(default)*, with factors `low = 1.0`,
`normal = 0.75`, `high = 0.5` *(default)*. The threshold compares the dislocation's
magnitude, since a negative dislocation is admissible. The v2.3.3 example values
(1.5 / 1.0 / 0.6) are withdrawn; `IV × exp(CompositeScore × k)` is noted as a
calibration-era alternative that needs no bound on `k`.

## 4. DEC-001 — add default conditions

A deploy decision requires, each recorded with its required and actual value *(default)*:
`|dislocation_value| ≥ threshold` (equity 1.5 index points, oil 3.0), `|composite| ≥ 0.5`,
`contributing_sources ≥ 1`, `top_signal_certainty ≥ 0.5`, newest contributing observation
at most 2 trading days old, and no active cooldown (always satisfied until RSK-001 exists).
A negative composite that passes the magnitude conditions yields a deploy decision whose
explanation names the vol-compression scenario.

## 5. §9 Validation set — add interpretation

"Deploy by date D" means a deploy decision with observation date in `[D − 1 trading day, D]`.
Replay results are reported per event against the expectation, with the reason when an
event cannot be evaluated (for example a geopolitical-only event with no classifiable
category). The Iran February 2026 criterion is a calibration target, not a release gate.
Thresholds tuned on this set carry an "in-sample" label on their configuration version.

## Application

Apply blocks 1–5 to the SRS source, bump the revision history to v2.3.4 with a one-line
summary per block, and update `doc/PrimeScore-API-v1.yaml` descriptions of `CompositeScore`
and `IvDislocation` to match (description text only; no schema change).
