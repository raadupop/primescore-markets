# ADR-0003: Test oracle architecture for the classification service

- **Status:** Accepted
- **Date:** 2026-05-04
- **Deciders:** Radu Pop
- **Supersedes:** —
- **Relates to:** [project ADR-0001](../../../../doc/adr/0001-agent-harness-architecture.md) (the agent harness this oracle architecture instantiates), [classification ADR-0001](0001-per-indicator-tuning-parameters.md), [classification ADR-0002](0002-ecdf-severity-and-backtest-harness.md)

This ADR is **scoped to the test-oracle architecture** for the Python
classification service. It is **not** the agent harness architecture
(that is project-wide and lives at
[`doc/adr/0001-agent-harness-architecture.md`](../../../../doc/adr/0001-agent-harness-architecture.md)).
This ADR locks **Layer 4** of that harness as it instantiates here:
the five oracles + the operational health gate that catch bugs in
the classification service before they ship.

The operational runbook (file paths, commands, fixture schema, build
status, current red-test gaps) lives in
[`apps/classification/HARNESS.md`](../../HARNESS.md). This ADR is the
locked architectural decision; HARNESS.md is the regenerable
inventory.

The classification service is constant infrastructure across all six
PrimeScore AI .NET architecture iterations. It is not under measurement. The
oracle architecture exists so the service stays a stable dependency
under the single-operator + AI-mediated-edits regime named in the
project-wide ADR.

## Context

Two failure modes are demonstrated, not hypothetical:

- **Wrong-level abstraction** ([ADR-0001](0001-per-indicator-tuning-parameters.md)).
  Per-strategy module-level constants were structurally invisible to
  the suite because every strategy had only one indicator under
  test. Homogeneous inputs hid per-indicator vs per-strategy
  collapse. Caught by code review, not by any test — a Case B
  oracle escape.
- **Self-validating loop** ([ADR-0002](0002-ecdf-severity-and-backtest-harness.md)).
  An agent writing both the implementation and the expected fixture
  bands in one pass encoded the same wrong assumption in both, and
  the SRS-anchored oracle accepted it. The classic *calibration
  mismatch with realized market behavior* class — also a Case B
  escape until a reality-derived oracle catches it.

Common diagnosis: a single oracle is not enough. Bugs that live on
an axis the oracle does not see require a *different* oracle, not
more tests of the same kind. The oracle architecture is the
component-level answer.

## Decision

The classification service implements **five oracle classes** plus
**one operational gate**, organized across **three control families**:

- **Spec-derived family.** Expectation is computed from the SRS and
  the OpenAPI contract, independently of the implementation.
- **Structure-derived family.** Expectation is a property of the
  source code (layering, complexity, typing, hygiene, dead code).
- **Reality-derived family.** Expectation is computed from market
  outcomes the classifier never saw.

### The five oracles

1. **Contract shape** (spec-derived). Schema validation of every
   `/classify` and `/health` response against the OpenAPI 3.1
   contract. Catches contract drift between the .NET caller and the
   classifier.
2. **Anchor scenarios** (spec-derived). Black-box replay of trader-
   curated historical events with expected severity bands derived
   from the SRS formula. **Bands derive from the SRS, never from
   current implementation output** — the strict band-derivation rule
   in [`tests/acceptance/fixtures/ANCHORS.md`](../../tests/acceptance/fixtures/ANCHORS.md).
   Catches formula misimplementation against named historical events.
3. **Mathematical axioms** (spec-derived). Universally-quantified
   properties of CLS-001 — sign convention, monotonicity, zero-
   deviation behavior, the CLS-009 parametric-fallback gate. Catches
   the bug class anchors miss: a sign-convention or monotonicity
   violation can pass every named anchor while still being wrong for
   an entire class of inputs. The signed-score migration is the live
   demonstration — anchors agree on extreme-event magnitude while
   axioms catch sign inversion.
4. **Structural fitness** (structure-derived). Architectural
   invariants asserted as pytest tests by subprocess-invoking five
   external tools: `mypy --strict`, `xenon` complexity, `ruff`
   hygiene, `vulture` dead code, `import-linter` layering and
   peer-isolation contracts. Catches wrong-level abstraction,
   layer-boundary violation, complexity blow-up, type-erasure
   regression, dead code, cross-strategy coupling.
5. **Backtest Layer A** (reality-derived). For each historical event
   in a curated set, the realized IV path in the 24–48 h post-event
   window is compared against the severity the classifier emitted.
   Catches *one specific Case B sub-class* — calibration mismatch
   with realized market behavior, the self-validating-loop class
   that produced ADR-0002. **Status: NOT BUILT.** Named here as the
   architectural commitment to an independent reality-derived oracle
   for this component, with named statistical limits (see
   *Acknowledged limits* below).

### The operational gate

**G1 — `/health` readiness.** While the FastAPI lifespan runs
`populate_windows()`, `/health` returns
`503 {"status": "not_ready"}`. Once `state.is_ready` flips, `/health`
returns `200 {"status": "ready", "windows": {...}}`. The .NET caller
gates ingestion on the 503→200 transition. Catches pre-bootstrap
traffic and silent serving against a degenerate window. Tested by
[`tests/acceptance/test_health_acceptance.py`](../../tests/acceptance/test_health_acceptance.py).

### Why these five — bug-class to oracle mapping

Each oracle exists because a distinct bug class would otherwise
escape:

| Bug class | Oracle that catches it |
| --- | --- |
| Contract drift between .NET and classifier | 1 (contract shape) |
| Formula misimplementation against the spec | 2 (anchor) + 3 (axiom) |
| Sign-convention / monotonicity violation across an input class | 3 (axiom) |
| Wrong-level abstraction; structural drift | 4 (fitness) |
| Calibration plausible-on-paper, wrong against the market | 5 (Layer A — when built) |
| Pre-bootstrap traffic | G1 (`/health`) |

**Why anchor and axiom are separate.** Anchors are existentially
scoped ("for *this* event the score is in band [a, b]"); axioms are
universally quantified ("for *all* inputs satisfying P, the score
satisfies Q"). A formula change can pass every anchor and still
violate an axiom for some input class. Different quantifiers,
different oracles, no duplication. Live evidence: the signed-score
migration produces failures on the axiom layer that no anchor
reaches.

**Why Layer A is separate.** The other four oracles all derive their
expectations from the SRS or the implementation. If those two
co-evolve from a wrong assumption, every oracle accepts the bug.
Layer A's source of truth is the *market* — independent of both SRS
and implementation. It catches the self-validating-loop class
because its ground truth is separately sourced.

### Acknowledged limits of Layer A

Layer A catches *one* specific Case B sub-class. It does **not**
catch:

- Wrong-level abstraction (output is fine; structure is wrong).
  Caught by oracle 4 (fitness) and `/architect` review.
- Frame-of-reference errors where shared assumptions propagate
  through every oracle including Layer A.
- Bugs in Layer A's own ground truth (window choice, IV source
  integrity, regime-misaware bands).
- Novel bug classes outside Layer A's mapping.

**Statistical limits.** Even within its target sub-class, Layer A is
defensible as a *coarse-grained rank-correlation check across the
catalogue* and a *per-event band-violation check*. It is **not**
defensible as a fine-grained continuous-calibration tool: the anchor
catalogue is ~50 events from 2010–2025; vol shocks are rare; post-
event windows have confounding events; non-stationarity matters.
The band-derivation rule and any regime-conditioning are
quantitative-methods decisions and require `/trader` + `/statistician`
review when Layer A is built — not architecture decisions, not a
chief-architect call.

The architectural commitment is to **the pattern** (independent
reality-derived oracle as a valid oracle class for this component).
The skills layer at the project level (Layer 2 of the agent harness)
remains the answer for everything Layer A cannot reach.

### Steering loop here

The two-entry steering loop from the project-wide ADR-0001 applies:

- **Case A (oracle red).** A test failed; one of the five oracles
  caught it. Fix the implementation; add a regression case (new
  anchor, new axiom, new fitness rule) only if the existing oracle
  did not cover the variant.
- **Case B (oracle escape).** Bug exists despite green tests. Only
  enterable when a non-oracle observer (specialist skill review,
  Layer A when built within its target class, code review,
  production observation) raises the issue. Response: open ADR + new
  control in same PR, or `LIMITATIONS.md` entry.

An ADR captures an architectural decision. A bug alone does not
warrant one; a bug plus a new control does. A bug plus an accepted
gap is a `LIMITATIONS.md` entry. Silent gaps are not a permitted
state.

## Trade-offs

- **Five oracles, not three.** Sacrificed: a single uniform
  "acceptance suite". Gained: each oracle catches a distinct bug
  class with a distinct oracle source, and a failing test points at
  the failure mode without diagnostic effort. The signed-score
  migration is the live justification for keeping anchor and axiom
  separate.
- **Layer A specified but unbuilt.** Sacrificed: the actual catch of
  the calibration-mismatch class today. Gained: an explicit
  architectural commitment that the spec-derived family is *not*
  alone sufficient — and a tracked gap in HARNESS.md that
  `LIMITATIONS.md` carries forward.
- **Pattern-level commitment, not formula-level.** Sacrificed: a
  precise promise about Layer A's bands. Gained: honest framing —
  band-derivation is a quantitative-methods decision; statistical
  limits are real; Layer A is one valid oracle, not the answer to all
  Case B.
- **Hand-rolled axioms, not Hypothesis fuzzing.** Sacrificed: random
  coverage. Gained: deterministic CI, no flake budget. Reconsider if
  a hand-rolled axiom misses a variant a fuzzer would catch.
- **No coverage threshold.** Sacrificed: a familiar lagging metric.
  Gained: avoids the failure mode where coverage gaming substitutes
  for assertion quality.

### Dissenting view

The strongest case against five oracles is that five plus an unbuilt
Layer A is over-instrumented for a single operator: ship with
contract + anchor + fitness and add the rest if a bug demonstrates
the need. Rejected on evidence — ADR-0001 and ADR-0002 already span
four oracle classes between them; the signed-score migration produces
live axiom-oracle failures; and Layer A is the only structural
answer to the self-validating-loop class.

## Cost of being wrong

- **Five turns out to be three plus two redundant.** Reversible:
  collapse via superseding ADR. Cheap.
- **A sixth oracle class is needed.** Cost: one missed failure mode
  until observed. Reversible: superseding ADR.
- **Layer A's band-derivation rule is methodologically flawed at
  build time** (e.g. windowing produces false positives at regime
  transitions). False alarms erode operator trust — the worst
  long-term failure mode of any oracle. Mitigation: `/trader` +
  `/statistician` review of the band-derivation rule before Layer A
  goes live; the same source-provenance discipline ANCHORS.md
  applies to anchor fixtures.

## Out of scope

- **The agent harness as a whole.** That is project-wide; locked in
  [`doc/adr/0001-agent-harness-architecture.md`](../../../../doc/adr/0001-agent-harness-architecture.md).
- **Parallel domain-spec YAML** with attestations / model cards /
  hash-linkage sensors. The contract is `openapi.yaml`; domain
  scenarios are anchor fixtures.
- **Service-layer metamorphic tests.** Deferred to the math-library
  layer if and when one is extracted under `app/math/`.
- **Mutation testing in the per-commit loop.** A nightly
  `mutmut`-on-`app/strategies/*` pass — a meta-check on the test
  suite itself — is a candidate future control, not part of the
  locked architecture today.
- **Coverage-threshold gating.** See trade-offs.
- **AI-judged oracles.** Layer A is statistical, not LLM-based.

## Relationship to HARNESS.md

[`apps/classification/HARNESS.md`](../../HARNESS.md) is the
**inferable per-component harness inventory**: file paths, current
dispositions, layer map across all five harness layers (not just
Layer 4), the running gap list. It is regenerable from this ADR
plus the project-wide ADR-0001 plus the state of the repo.

This ADR is the **locked decision** for Layer 4 (oracles) at this
component: the five oracle classes, the operational gate, the
steering loop. Changing the oracle architecture requires a
superseding ADR; updating the inventory does not.

## References

- [Project ADR-0001](../../../../doc/adr/0001-agent-harness-architecture.md)
  — agent harness architecture (this oracle architecture is Layer 4
  at this component)
- [`apps/classification/HARNESS.md`](../../HARNESS.md) — per-
  component harness inventory and gap map
- [Classification ADR-0001](0001-per-indicator-tuning-parameters.md)
  — wrong-level tuning postmortem
- [Classification ADR-0002](0002-ecdf-severity-and-backtest-harness.md)
  — ECDF severity + registry postmortem and Layer A commitment
- [SRS](../../../../doc/srs/PrimeScore-SRS.md) — requirements (CLS-001,
  CLS-002, CLS-006, CLS-009, EXT-004, §11 acceptance criteria)
- [`apps/classification/doc/openapi.yaml`](../openapi.yaml) — contract
- [`tests/acceptance/fixtures/ANCHORS.md`](../../tests/acceptance/fixtures/ANCHORS.md)
  — anchor catalogue and band-derivation rule
- [`LIMITATIONS.md`](../../LIMITATIONS.md) — accepted-gap register
- ADR format: Michael Nygard, *Documenting Architecture Decisions*
  (2011)
- Ford / Parsons / Kua, *Building Evolutionary Architectures* (2017)
- Federal Reserve, *SR 11-7: Guidance on Model Risk Management*
  (2011) — independent validation principle
