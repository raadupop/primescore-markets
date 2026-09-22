# ADR-0002: ECDF severity mapping, two-parameter per-class calibration, and backtest harness layer

- **Status:** Accepted (design locked; implementation deferred to `/chief-architect` engagement)
- **Date:** 2026-04-18 (original); amended 2026-04-27 (window-degeneracy guard)
- **Deciders:** Radu Pop
- **Supersedes:** ADR-0001 Decision 2 (parameters travel in the request payload)
- **Relates to:** ADR-0001 (per-indicator tuning parameters)

> **2026-04-27 amendment.** The `D` (minimum-informative-dispersion floor)
> parameter described in §2 was removed and replaced with a global
> window-degeneracy guard. `D` was vestigial under ECDF: the dispersion
> floor existed to stabilise the prior z-score severity formula, and ECDF
> rank has no division-by-IQR pathology. The corrected design lives in
> the §2 "Window-degeneracy guard" sub-bullet below; the rest of this ADR
> stands.

## Context

During the 4-fixture axis-coverage work (OVX Aramco, VIX COVID mid-crisis,
VIX vol crush, VIX normal day) a structural problem in the acceptance suite
surfaced: the suite cannot catch a class of AI-generated bugs in which
tests and implementation co-evolve from the same wrong assumption. The
four fixtures collapsed to regression guards — their bands reflect whatever
the classifier currently outputs, because no cross-tech formula exists to
independently compute the "correct" severity.

Root cause: [CLS-001 in the SRS](../../../../doc/srs/PrimeScore-SRS.md) was at
the time deliberately qualitative ("severity is quantified; certainty
has two independent dimensions combined somehow") with no formula. This is
intentional per Insight 7 (spec precision treated as an experimental
variable in DeltaFeed), but at the product layer it produces an
unfixable bug class: if the SRS does not specify how severity is
computed, an agent can encode any formula in the implementation and any
expected band in the test, and both pass trivially.

Three external inputs sharpened the diagnosis:

1. **`/trader` critique of fixed-scale `tanh`.** `tanh(|z| / _TANH_SCALE)`
   assumes a time-invariant distribution. No vol indicator has that across
   2017–2024. Window absorption and regime shifts silently degrade the
   mapping.
2. **`/trader` stress-test of OVX vs VIX under an ECDF replacement.** ECDF
   closes the *scale* gap (VIX 10–80 vs OVX supply-shock tails are
   normalized by rank within each indicator's own history). It does **not**
   close the *non-stationarity / event-clustering* gap: OVX's sparse-event
   history produces false p95 ranks on modest moves off a flat window, and
   autocorrelated post-event ramps mean the second identical shock ranks
   lower than the first because the window has absorbed the first.
3. **Market-reality oracle gap.** The acceptance suite oracle is the SRS
   contract. A separate oracle — what IV actually did after the event — is
   needed to catch the self-validating-loop class. Same fixture data, a
   different oracle, a different bug class. No duplication.

## Decision

Three co-decided pieces (the harness layer that originally rode along
with this ADR is now in [ADR-0003](0003-test-oracle-architecture.md)):

### 1. ECDF / percentile-rank mapping for all RULE_BASED strategies

Replace `severity = tanh(|z| / _TANH_SCALE)` with an ECDF-rank mapping
over a per-indicator history window. The normative formula and the
per-strategy `deviation` definitions live in **SRS §3 and §5.2 CLS-001**.
Subsequent SRS amendments make the severity signed and re-anchor the
window to a long-horizon reference; the **ECDF-rank mapping itself is
the durable decision recorded here**.

**GEOPOLITICAL stays out.** EVENT_ASSESSMENT via LLM, severity not
derived from a statistical distribution, different oracle class
entirely.

### 2. Per-class registry parameters

Per-indicator-class registry entries carry two parameters; a third
degeneracy guard is global, not per-class:

- **`N`** — history-window length. Controls how many past `|deviation|`
  values the ECDF ranks against. Longer windows are more stable but slower
  to adapt to regime shifts.
- **`expected_frequency_seconds`** — the normal update cadence for this
  indicator class (e.g. 86400 for daily, 2592000 for monthly). Used by the
  Python classifier to compute `temporal_relevance` via exponential decay,
  and by the .NET ingestion job to set staleness alert thresholds. Moving
  this from request payload (ADR-0001 Decision 2) to the registry removes
  the caller as a trust boundary for calibration data.
- **`deviation_kind`** — per-class data-shape parameter selecting which
  deviation formula applies (`level_vs_median`, `surprise`, etc.).

**Window-degeneracy guard (amended 2026-04-27, replaces the original `D`
parameter).** CLS-009's first guard condition is a global window-
degeneracy check, not a per-class dispersion threshold:

```text
is_window_degenerate(H) ≡ |{ round(v, 4) : v ∈ H }| < k_min
```

where `H` is the history window and `k_min = 10` is a single global
constant. Properties:

- **Distribution-free.** No assumption about the shape of `|deviation|`.
- **Sample-size independent.** No empirical p5 to estimate; no
  autocorrelation correction needed.
- **First-principles constant.** A percentile rank over fewer than ten
  distinct values has resolution coarser than `1/10 = 10pp`; below that
  the rank is not informative regardless of distribution.
- **Detects scale collapse.** A flat or near-flat window has few distinct
  values, regardless of absolute scale.
- **One global parameter, not per-class.** No calibration ever needed.

The rounding to 4 decimals is defensive against float-noise spurious
distinctness in possible future derived series; on the present sources
(VIX/OVX 2dp, CPI YoY 1dp, claims integers) it is a no-op.

Why this replaced `D`: `D` was carried over from the prior z-score
severity model, where dividing by a small IQR inflated normal-day moves.
ECDF rank does no such division — a tight-but-non-flat window simply
produces compressed rank distances, which is correct behavior, not a
bug. The only residual ECDF edge case is **insufficient distinct points
to rank against**, which is a count problem, not a magnitude problem.
Two specialists rejected `D` as specified: `/statistician` (per-class
absolute `D` is undefendable at the sample sizes available — a defensible
empirical p5 of the rolling-IQR distribution requires ≥ 200 non-
overlapping IQR observations, unreachable for monthly series), and
`/trader` ("minimum dispersion floor" is not a parameter anyone on a vol
desk quotes or sizes a trade against). A scale-invariant ratio
replacement (`IQR < α × median(|deviation|)`) was rejected as the
interquartile coefficient of dispersion is bounded for typical financial
series and cannot detect scale collapse.

One per-class parameter (`N` alone) is insufficient — `/trader` rejected
this explicitly. `expected_frequency_seconds` supersedes ADR-0001
Decision 2.

### 3. Closed-universe indicator registry

The classifier does not own the indicator catalogue. A shared registry
maps `symbol → { indicator_class, N, deviation_kind, expected_frequency_seconds }`.
Approvals gate additions (trader-reviewed). Unknown indicators trigger the
CLS-009 degraded-confidence fallback rather than silent zeros.

Registry ownership, file format, and reload semantics are a `/chief-architect`
decision (deferred). The five rationale properties (closed-universe safety,
calibration per class, shared .NET/Python contract, git audit trail,
operational approval gate) are documented in the Consequences section below.

### 4. Backtest harness — Layer A

The market-reality-oracle backtest layer originally co-decided with the
ECDF pivot has been promoted to [ADR-0003](0003-test-oracle-architecture.md),
which now owns the harness architecture in full (the three durable
controls and the three test layers with distinct oracles).

### Scope of the ECDF pivot

In scope: MARKET_DATA, MACROECONOMIC, CROSS_ASSET_FLOW (currently stubbed —
build-step 5 lands directly on ECDF, not on tanh-then-rewrite).

Out of scope: GEOPOLITICAL (LLM-judged).

## SRS requirements impacted

Landed in the [SRS](../../../../doc/srs/PrimeScore-SRS.md). The CLS-001
ECDF formula, CLS-009 degraded-confidence fallback, the indicator
registry definition, and the §11 acceptance criterion extension are
all in §3 and §5.2 — see the SRS for normative text. SIG-001's "no
fewer than four" phrasing was preserved verbatim.

## Consequences

### Positive

- Magic `_TANH_SCALE` constants eliminated. Per-signal severity becomes
  paper-computable directly from the revised CLS-001.
- Acceptance suite can catch formula misimplementation; backtest Layer A
  can catch calibration drift and the self-validating-loop class the
  acceptance suite structurally cannot see.
- CROSS_ASSET_FLOW (build-step 5) lands on the final formula first —
  avoids a tanh-then-rewrite cycle and its associated test churn.
- Acceptance criterion §11.12 gains a third entry (CLS-001), tightening
  the spec's own verification contract.

### Negative / cost

- `/chief-architect` engagement required to design the full transition
  (ingestion contract, registry format, bootstrap reload semantics).
- CLS-001 moves from an implicit qualitative specification to an
  explicit quantitative one. The harness's "qualitative-requirements"
  surface shrinks by one as a result.

### Coherence under subsequent SRS amendments

Subsequent SRS revisions amended CLS-001 / CLS-002 / CLS-006 / EXT-004
on top of the ECDF foundation laid here: severity became signed (sign
encoding vol-expansion vs vol-compression), the ECDF reference window
moved to a long-horizon `H_L` of binomial-SE-bounded length `N_L`, a
parametric fallback covers indicator classes whose `N_L` is
unattainable (monthly macro), the CLS-002 source-dropout penalty
became duration-scaled, and EXT-004 added catalyst-relative exit and
a Vega-crush gate. **The decisions in this ADR are not displaced by
those amendments**: ECDF rank remains the per-signal severity
mapping, the indicator registry remains the closed-universe
catalogue, the global window-degeneracy guard (replacing `D`) remains
the resolution-floor check. The amendments refine, not invert, the
foundation.

### Why a registry, not alternatives

The decision to structure the closed-universe indicator catalogue as
a registry (vs. hard-coded parameters, symbol-format inference, or no
closed-universe constraint at all) rests on five properties:

- **Closed-universe safety.** Unknown symbols must fail loud. The
  alternatives — accept-everything-with-defaults (silent miscalibration)
  or hard-coded allow-list in code (which *is* a registry, just with
  worse ergonomics) — are either unsafe or no different in substance.
* **Calibration per class, not per symbol:**
  * Parameters are scoped per class. `N`, `deviation_kind`, and `expected_frequency_seconds` live in the registry keyed by indicator class. Multiple symbols in the same class share parameter values, eliminating duplication when tuning.
  * Rolling-window history is scoped per symbol. Each symbol maintains its own ECDF reference distribution. Pooling history across symbols within a class is out of scope and would require a per-class exchangeability argument.

- **Shared contract between .NET ingestion and Python classifier.**
  The registry is the single source of truth for the .NET-side
  WebSocket subscription list and the Python-side calibration
  parameters. A file is the minimum viable shared artefact across the
  HTTP boundary; in-memory alternatives would require a second
  synchronisation mechanism.
- **Audit trail in git.** Parameter evolution (`N` extended, a new class
  added) lives in git history when the registry is a file. In code it
  gets mixed with logic changes and gets lost in review.
- **Operational approval gate.** Adding an indicator class is a
  trading-risk decision. A PR against a registry file makes that
  decision visible in review; a code change buries it among
  unrelated logic.

### Ingestion-job impact (.NET side)

The registry is a shared artefact between the .NET ingestion job and the
Python classifier:

- **Bootstrap depth is registry-derived.** Some classes will need
  `N > 20` days, changing the Twelve Data / FRED / Finnhub REST pull
  shape at classifier startup.
- **Symbol → indicator-class mapping lives in the registry** and both
  sides must agree.
- **WebSocket subscription list (.NET side) becomes registry-derived** —
  only registered symbols stream.
- **Unknown symbols trigger the CLS-009 degraded-confidence fallback**
  across the HTTP boundary, not a silent zero.
- **No formula computation moves to .NET.** Plumbing stays thin. Only
  the registry-as-contract is new between the two services.

Registry ownership, file format, and reload semantics are a
`/chief-architect` decision.

### CROSS_ASSET_FLOW build-order impact

Step 5 (currently stubbed at
`apps/classification/app/strategies/cross_asset.py`) lands directly on
the ECDF formula. Anchor fixtures for the two CROSS_ASSET_FLOW events
(China deval 2015, SVB flight-to-quality 2023) will be authored against
the ECDF formula from the start.

## References

- Plan: `C:\Users\Radu\.claude\plans\structured-percolating-parrot.md`
- ADR-0001: [`0001-per-indicator-tuning-parameters.md`](0001-per-indicator-tuning-parameters.md)
- CLS-001 severity-formula SRS annex stub (Superseded by SRS):
  [`srs-annex-cls-001-severity-formula.md`](srs-annex-cls-001-severity-formula.md)
- Limitations cross-reference: [`../../LIMITATIONS.md`](../../LIMITATIONS.md) #1, #3, #4, #5
- Test-oracle architecture (Layer 4 of the project harness): [ADR-0003](0003-test-oracle-architecture.md); inferable inventory in [`../../HARNESS.md`](../../HARNESS.md); project-wide harness in [`doc/adr/0001-agent-harness-architecture.md`](../../../../doc/adr/0001-agent-harness-architecture.md)
- `/chief-architect` briefing: `doc/research/chief-architect-briefing-harness-redesign.md`
- SRS: [`doc/srs/PrimeScore-SRS.md`](../../../../doc/srs/PrimeScore-SRS.md)
  (CLS-001, CLS-009, §9 Validation Event Set)
- ADR format: Michael Nygard, *Documenting Architecture Decisions* (2011)
