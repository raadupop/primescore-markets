# `/chief-architect` briefing — harness redesign for the classification service

- **Status:** Briefing document. Input to a future `/chief-architect` invocation. Not yet engaged.
- **Date:** 2026-04-18
- **Author:** Radu Pop
- **Context sources:** [ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md), [CLS-001 SRS annex stub](../../apps/classification/doc/adr/srs-annex-cls-001-severity-formula.md), [LIMITATIONS.md](../../apps/classification/LIMITATIONS.md), [HARNESS.md](../../apps/classification/HARNESS.md)

## Why this briefing exists

The classification service's harness (acceptance + architectural fitness)
is structurally unable to catch a class of AI-generated bugs in which the
strategy and its tests co-evolve from the same wrong assumption. The
four-fixture axis-coverage work (OVX Aramco, VIX COVID mid-crisis, VIX
vol crush, VIX normal day) made this concrete — the fixtures collapsed
to regression guards because CLS-001 provides no formula to independently
compute the "correct" severity.

[ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md)
scopes the remediation:

1. Pivot all three RULE_BASED strategies (MARKET_DATA, MACROECONOMIC,
   CROSS_ASSET_FLOW) from `tanh(|z| / _TANH_SCALE)` to ECDF / percentile
   rank with two per-class parameters (`N`, `D`).
2. Add a backtest harness layer (Layer A) with a market-reality oracle.

The decisions are locked at the design level. Implementation requires an
architectural redesign that cuts across the Python classifier and the
.NET ingestion job. This briefing defines the scope of that redesign.

## What the `/chief-architect` engagement must produce

An architectural design document covering the following five concerns.
Each concern has concrete, named decisions the engagement must land.

### 1. Backtest as primary oracle for per-signal severity

**Why it matters.** The self-validating-loop bug class cannot be caught
by an acceptance suite that uses the SRS as its oracle — if the SRS and
the implementation are encoded from the same wrong assumption, the
acceptance suite passes. Layer A's oracle is external: what IV actually
did in the 24–72h after an event. This is the only layer that can catch
the bug class.

**Decisions to land:**

- Oracle shape: binary direction + magnitude bucket has been agreed
  (*"IV moved > 20% post-event → severity ≥ 0.7 must hold"*). Confirm
  the exact IV measurement (SPX 30-day ATM IV? VIX close? indicator-
  specific IV surface?) and the post-event window per event class.
- Event set: SRS §9 has 10 validation events. Layer A should start from
  those and expand. Design the extensibility path — who adds events,
  what provenance is required (mirrors the `ANCHORS.md` source-provenance
  rule), how the set is versioned.
- Failure semantics: backtest failure is not a build break — it is a
  pre-release gate. Design the cadence (nightly? pre-release?), the
  reporting surface, and the remediation loop (how a failure becomes an
  ADR / a registry tune / a formula revision).
- Per-strategy oracle variation: MARKET_DATA uses IV; MACROECONOMIC
  might use same-day equity/bond reaction; CROSS_ASSET_FLOW uses
  correlation breakdown. Lock the oracle per strategy.

### 2. Data-centric pipes-and-filters pipeline model

**Why it matters.** The current service is a FastAPI dispatcher over
strategy classes holding in-memory rolling windows. The ECDF pivot + two
per-class parameters + registry + backtest harness adds enough new data
flow that the "dispatcher + strategy objects" shape starts to strain.
An explicit pipes-and-filters model, aligned with event-study stages,
gives each stage a named data contract and lets the backtest replay
through the same pipeline as production.

**Decisions to land:**

- Stage inventory: expectation formation → parse / validate → score
  (ECDF per strategy) → dispersion-floor check → compose certainty →
  serialize response. (Composite scoring, regime conditioning,
  signal-to-size, and attribution live on the .NET side; the classifier
  only owns up to serialize.)
- Inter-stage data contracts: what flows between stages, typed. Pydantic
  v2 is the current validation layer; decide whether stages share one
  request/response carrier or each stage has its own typed dataclass.
- Replay substitution: the backtest harness should replace the ingestion
  pipe only, reusing the rest. Design the replay seam so Layer A cannot
  accidentally hit a different code path from production.
- Stateful-vs-stateless boundaries: rolling windows are stateful; the
  rest is pure. Locate state explicitly (currently in `app/state.py`)
  and ensure the pipes don't leak it.

### 3. CEP (Complex Event Processing) for Risk Regime Transition detection

**Why it matters.** SRS Definitions carry a "Risk Regime Transition"
concept — a multi-signal pattern persisting > 3 trading days with
cross-asset confirmation. That is a CEP primitive: a windowed pattern
across multiple streams, not a per-signal classification. Today no part
of the system detects it. CLS-006 (IV dislocation) and RSK-002 rely on
regime context that nothing produces.

**Decisions to land:**

- Ownership: Python classifier or .NET? Arguments both ways — Python
  has the rolling windows already; .NET owns composite scoring and can
  reason across signals. Decide explicitly.
- Representation: CEP engine (Esper / Flink / Siddhi — ruled out by
  operational cost), hand-rolled sliding-window aggregator, or a
  scheduled job that reads a signal store. Pick one.
- Output contract: Risk Regime Transition is a classification in its own
  right — what `score_type` does it carry, or does it bypass the
  classifier contract entirely?
- Scope of this briefing: design only. Implementation waits until a .NET
  iteration exists that needs it.

### 4. Closed-universe indicator registry + ingestion-job contract

**Why it matters.** The ECDF pivot requires a registry of
`indicator_class → { N, D, deviation_kind }`. The registry becomes a
shared artefact between the .NET ingestion job and the Python
classifier. This is new: today there is no shared configuration
between the two services beyond the OpenAPI contract.

**SRS requirement impacted: SIG-001.** SIG-001 today defines the four
signal source categories only. The closed-universe registry is a
sub-level constraint on each category (registered symbols produce
normal-path severity; unknown symbols return a CLS-004 degraded-
confidence response). SIG-001 must be extended — or complemented by a
new sub-requirement SIG-001.1 — to anchor the registry in the SRS.
Revision lands with the CLS-001 rewrite in the next SRS pass.

**Decisions to land:**

- **Location and format.** Repo-tracked YAML? Shared HTTP endpoint? A
  dedicated config service? Pick one and defend operational cost.
- **Ownership and approval workflow.** Trader-gated additions. Where do
  PRs land, who reviews, how does the registry version-bump propagate
  to both sides.
- **Bootstrap depth.** Classifier bootstrap pulls from Twelve Data /
  FRED / Finnhub at startup. `N` per class is registry-driven; some
  classes need `N > 20` days. Design the bootstrap API shape change.
- **WebSocket subscription list (.NET side).** Today it is implicit.
  With a registry, it is registry-derived — only registered symbols
  stream. Design the registry-read mechanism on the .NET side.
- **Unknown-symbol fallback.** Per CLS-004: a symbol not in the
  registry returns a well-formed degraded-confidence response, not an
  error. Specify the exact shape across the HTTP boundary.
- **Reload semantics.** A registry change — does it require a redeploy,
  a hot reload, a `/admin/reload` endpoint? Does the classifier
  rebootstrap or diff-apply?
- **No formula computation on .NET.** Plumbing stays thin; only the
  registry-as-contract is new.

### 5. Two-parameter per-class calibration (`N`, `D`)

**Why it matters.** `N` alone is insufficient — `/trader` rejected
single-parameter calibration after stress-testing OVX against VIX under
an ECDF replacement. `D` (minimum-informative-dispersion floor) guards
against ECDF p95 false-highs on flat-history indicators. How `D` is
measured, validated, and updated is non-trivial.

**Decisions to land:**

- `D` measurement: IQR vs std on the rolling history — pick one per
  class or globally.
- `D` validation: how do we know the floor is set right? Backtest Layer
  A validates severity outputs; `D` is an intermediate parameter. Design
  whether Layer A's oracle is sensitive enough to detect a wrong `D`.
- Initial values: per-class `N` and `D` bootstrap values. Historical
  calibration against a named reference period? `/trader`-proposed
  values? Document the methodology.

## Formal acceptance / backtest split, aligned with DeltaFeed

The DeltaFeed experiment treats the classification service as constant
infrastructure across all six .NET iterations. This has implications
for which harness layers are EVO-001 (experiment-gating) and which are
service-level.

| Layer | Experiment status |
| --- | --- |
| Acceptance (classifier) | Service-level. Not EVO-001. |
| Architecture (fitness, classifier) | Service-level. Not EVO-001. |
| Backtest Layer A (classifier) | Service-level. Not EVO-001. The classifier is constant across iterations. Layer A validates the service; a Layer-A regression does not invalidate an iteration's measurement. |
| Backtest Layer B (.NET system, future) | Per-iteration. Not a pass gate for experiment validity, but informative for Insight-6-adjacent findings (does architecture-paradigm X handle regime shifts better?). |

`/chief-architect` should confirm this split is durable under the six
iterations and not accidentally couple EVO-001 to classifier changes.

## Out of scope for this briefing

- Implementation of the ECDF formula in
  [`apps/classification/app/strategies/market_data.py`](../../apps/classification/app/strategies/market_data.py)
  and
  [`apps/classification/app/strategies/macroeconomic.py`](../../apps/classification/app/strategies/macroeconomic.py).
  Downstream of the architectural design.
- CROSS_ASSET_FLOW strategy build (build-step 5). Will land directly on
  ECDF once the architectural design is ratified.
- Layer B scaffolding. Waits for .NET iterations.
- SRS body revision. CLS-001 annex stub is in-repo at
  [`apps/classification/doc/adr/srs-annex-cls-001-severity-formula.md`](../../apps/classification/doc/adr/srs-annex-cls-001-severity-formula.md);
  SRS incorporation is a subsequent revision.

## References

- [ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md) — decision locked
- [CLS-001 SRS annex stub](../../apps/classification/doc/adr/srs-annex-cls-001-severity-formula.md) — normative formula
- [LIMITATIONS.md](../../apps/classification/LIMITATIONS.md) — #1, #3, #4, #5
- [HARNESS.md](../../apps/classification/HARNESS.md) — three-layer test model
- `doc/PrimeScore-SRS-v2.3.1.pdf` — CLS-001, CLS-004, CLS-006, RSK-002, §9 Validation Event Set, Definitions (Risk Regime Transition)
- [`doc/research/event-study-pipeline-notes.md`](event-study-pipeline-notes.md) — companion research notes
