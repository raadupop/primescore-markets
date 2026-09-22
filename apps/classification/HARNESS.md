# HARNESS — Classification Service

**Per-component harness inventory** for the Python classification
service. This service sits inside PrimeScore AI (the trading system) and is
constant infrastructure across all six .NET architecture iterations
that DeltaFeed measures. It is not under measurement.

The agent harness architecture is locked project-wide in
[`doc/adr/0001-agent-harness-architecture.md`](../../doc/adr/0001-agent-harness-architecture.md);
the test-oracle layer (Layer 4) for this component is locked in
[`doc/adr/0003-test-oracle-architecture.md`](doc/adr/0003-test-oracle-architecture.md).
This file is the regenerable inventory across all five harness
layers as they instantiate at the classification service: file paths,
current dispositions, current gaps, and the response protocol when
an oracle fires.

The harness layer numbering matches project-wide ADR-0001:

- **Layer 1** — Context architecture (what the agent knows here)
- **Layer 2** — Cognitive tools (skills used routinely here)
- **Layer 3** — Tool permissions (specific to this app)
- **Layer 4** — Feedback oracles + operational gate (the runbook)
- **Layer 5** — Decision durability (ADRs, LIMITATIONS, this file)

Plain-language definitions for *oracle*, *Case A / Case B*, *oracle
escape*, *Layer A*, *calibration mismatch* are in the project-wide
ADR's glossary; not repeated here.

---

## Layer 1 — Context

| Artefact | Role |
| --- | --- |
| [`AGENTS.md`](AGENTS.md) | Component onboarding doc: contract pointer, strategy routing, bootstrap, .NET integration boundary, LLM dependency |
| [`CLAUDE.md`](CLAUDE.md) | Auto-discovery pointer stub → `AGENTS.md` |
| [`doc/openapi.yaml`](doc/openapi.yaml) | Normative contract (request/response shapes, score semantics) |
| [`../../doc/srs/PrimeScore-SRS.md`](../../doc/srs/PrimeScore-SRS.md) | Project SRS — CLS-001, CLS-002, CLS-003, CLS-004, CLS-006, CLS-008, CLS-009, EXT-004 |
| [`../../doc/conventions/python-naming.md`](../../doc/conventions/python-naming.md) | Output-focused function/module naming |
| [`app/registry.py`](app/registry.py) + `data/registry/` | Indicator registry — symbol → class, per-class `N_L`, `deviation_kind`, `expected_frequency_seconds` |
| [`../../infra/registry.yaml`](../../infra/registry.yaml) | Shared registry schema (Python + future .NET) |

---

## Layer 2 — Cognitive tools (skills used routinely here)

The skills under [`../../.claude/skills/`](../../.claude/skills/) are
project-wide. The ones routinely relevant to changes in this
component, with the trigger event each is recommended at:

| Skill | When to invoke (concrete trigger) |
| --- | --- |
| `/trader` | Editing a fixture's `expected_band`; changing a registry parameter; reviewing severity output sanity for a real event |
| `/statistician` | Editing the severity formula in `app/strategies/*.py`; tuning per-class `N_L` or window size; designing or reviewing Layer A's band-derivation rule |
| `/risk-officer` | Changing `app/registry.py` or `data/registry/*` calibration entries; widening the closed-universe symbol set |
| `/chief-architect` | Authoring or revising any ADR under `doc/adr/`; reviewing this HARNESS.md's structure |
| `/architect` | Reviewing structural drift, layer violations, pattern conformance against the 6-iteration framework |
| `/adversary` | Adding or modifying a `LIMITATIONS.md` entry; stress-testing a fixture band or formula assumption |
| `/forward-deployed-engineer` | Framing a change for organisational viability; ambiguous problem definition; deployment sequencing |

These are operator-invoked via slash command. The execution model in
project ADR-0001 commits to harness-surfaced reminders (PostToolUse
hook printing "invoke `/trader` for sign-off") so the operator
doesn't have to remember the trigger map. **Hooks NOT YET CONFIGURED
— named gap.**

---

## Layer 3 — Permissions (relevant entries for this app)

Permissions live in
[`../../.claude/settings.json`](../../.claude/settings.json) (global)
and [`../../.claude/settings.local.json`](../../.claude/settings.local.json)
(local override). Entries relevant to this component:

- `Bash(python -m pytest …)` — pytest invocations against
  `tests/acceptance/`, `tests/architecture/`, `tests/integration/`.
- `Bash(.venv/Scripts/python …)` — venv-resolved Python.
- `Bash(ruff check …)` and `Bash(ruff check --fix …)` — Layer 4
  hygiene oracle.
- `Bash(mypy …)` — Layer 4 typing oracle.
- WebFetch domains: `fred.stlouisfed.org`, `api.stlouisfed.org`,
  `dol.gov`, `investing.com`, `tradingview.com` — for fixture
  provenance verification per `ANCHORS.md` source-provenance rule.
- `Edit(.claude/skills/{trader,risk-officer,architect}/**)` — skill
  edits scoped to the personas relevant here.

**Hooks: zero configured today.** Sequencing in project ADR-0001
commits to PostToolUse + Stop hooks; until those land, Layer 4
runs operator-manually.

---

## Layer 4 — Feedback oracles + operational gate

The runbook for the test-oracle architecture locked in
[`doc/adr/0003-test-oracle-architecture.md`](doc/adr/0003-test-oracle-architecture.md).
Five oracles + the G1 health gate. Each row below is what an
operator (or an agent reading this) needs to know to invoke or
diagnose.

### Layer map

| # | Oracle / gate | File(s) | Tool |
| --- | --- | --- | --- |
| 1 | Contract shape | [`tests/acceptance/test_contract_shapes.py`](tests/acceptance/test_contract_shapes.py) | `jsonschema` (Draft 2020-12) |
| 2 | Anchor scenarios | [`tests/acceptance/test_anchor_events.py`](tests/acceptance/test_anchor_events.py) + [`tests/acceptance/fixtures/`](tests/acceptance/fixtures/) + [`ANCHORS.md`](tests/acceptance/fixtures/ANCHORS.md) | pytest parametrize over JSON |
| 3 | Mathematical axioms | [`tests/acceptance/test_signed_score_axioms.py`](tests/acceptance/test_signed_score_axioms.py) | pytest with synthetic windows |
| 4 | Structural fitness | [`tests/architecture/`](tests/architecture/) — `test_typing.py`, `test_complexity.py`, `test_code_hygiene.py`, `test_dead_code.py` + `pyproject.toml` `[tool.ruff]`, `[tool.mypy]`, `[tool.importlinter]`, `[tool.xenon]`, `[tool.vulture]` | `mypy --strict`, `xenon`, `ruff`, `vulture`, `import-linter` |
| 5 | Backtest Layer A | *not yet implemented — see Gaps* | TBD (statistical, not LLM-based) |
| G1 | `/health` gate | [`main.py`](main.py) lines 89–103 + [`tests/acceptance/test_health_acceptance.py`](tests/acceptance/test_health_acceptance.py) | FastAPI lifespan + `state.is_ready` |

### Per-oracle detail

#### Oracle 1 — Contract shape

- **Tool.** `jsonschema` (Draft 2020-12), wrapped as plain pytest.
- **Oracle source.** [`doc/openapi.yaml`](doc/openapi.yaml) — the
  normative contract.
- **Assertion shape.** Loads OpenAPI; for each example request/response
  pair, validates against schema. Tests rejection of unknown
  `source_category` (422), missing required field (422), unsupported
  combinations (422), `CROSS_ASSET_FLOW` stub (501).
- **What the operator sees on failure.** A `jsonschema.ValidationError`
  in pytest output naming the offending JSON path and the violated
  schema rule.
- **Operator action.** Either fix the response shape or update
  `openapi.yaml` *and* the .NET caller's contract in lockstep. A
  contract-shape failure is never fixed by changing only the test.
- **Test count today.** 7 tests.

#### Oracle 2 — Anchor scenarios

- **Tool.** Plain pytest parametrize over JSON files;
  `app.state.windows` seeded via
  [`tests/acceptance/conftest.py`](tests/acceptance/conftest.py)
  fixtures.
- **Oracle source.** Each fixture carries `expected_band:
  { expected_score_signed, score_tolerance, temporal_relevance_min,
  sign_convention_check, rationale_pending_trader }` alongside
  `source: { provider, series_id, retrieved_at, url }` and the
  `srs_version`. The band-derivation rule in
  [`tests/acceptance/fixtures/ANCHORS.md`](tests/acceptance/fixtures/ANCHORS.md)
  is strict on two points: bands derive from the SRS formula, never
  from current implementation output; every value cites a verifiable
  public provider (FRED / BLS / Twelve Data / Finnhub /
  Bloomberg-consensus / Reuters-poll, with `series_id`,
  `retrieved_at` ISO timestamp, and URL). No interpolation.
- **What the operator sees on failure.** A pytest assertion of the
  form `expected score in [a, b], got x` with the fixture filename
  in the test ID.
- **Operator action.** If the implementation drifted, fix it. If a
  new variant slipped past existing anchors, author a new fixture
  with full provenance and add a row to `ANCHORS.md`.
- **Test count today.** 11 parametrised tests over 11 fixtures.

#### Oracle 3 — Mathematical axioms

- **Tool.** Plain pytest with synthetic windows; no fuzzing library.
  Axioms are stated as concrete assertions over hand-chosen
  representatives of each precondition class.
- **Oracle source.** SRS §3 / CLS-001 sign convention, monotonicity,
  zero-deviation behavior; CLS-009 parametric-fallback gate.
- **Assertion shape.** Universally-quantified properties: score in
  `[-1, +1]`, vol-compression returns negative, vol-expansion returns
  positive, symmetric deviations have opposite signs, parametric
  path engages for low-cadence indicator class, parametric-gate
  failure returns degraded certainty, zero deviation returns
  near-zero score.
- **What the operator sees on failure.** A pytest assertion naming
  the axiom (e.g. `test_vol_compression_returns_negative_score`).
- **Operator action.** Fix the implementation. An axiom failure
  means the formula is wrong for an entire class of inputs, not just
  one event; anchors alone cannot reach this.
- **Test count today.** 7 tests.

#### Oracle 4 — Structural fitness

- **Tools and exact configuration** (all in
  [`pyproject.toml`](pyproject.toml)):
  - `test_typing.py` → `mypy --config-file pyproject.toml app`.
    `[tool.mypy]` sets `python_version = "3.11"`, `strict = true` on
    `app/models`, `app/routing`, `app/strategies`. Other modules
    tracked at default strictness.
  - `test_complexity.py` → `xenon --max-absolute B --max-modules B
    --max-average A app`.
  - `test_code_hygiene.py` → `ruff check --output-format=json app`.
    `[tool.ruff.lint]` enables `E, F, I, B, SIM, C90, PLR`; McCabe
    ceiling is 10; documented per-file-ignores carry ADR
    cross-references.
  - `test_dead_code.py` → `vulture app --min-confidence 80`, with
    `tests/fixtures` excluded and FastAPI / pytest decorators in the
    ignore list.
  - `import-linter` contracts under `[tool.importlinter]`: four
    peer-to-peer forbidden contracts (each strategy may not import
    any other strategy — MARKET_DATA, MACROECONOMIC, CROSS_ASSET,
    GEOPOLITICAL); a "models pure" contract forbidding
    strategies/routing/state/config imports from `app/models`; a
    layered contract `routing > strategies > math > state/config >
    registry/models`.
- **Oracle source.** The configurations themselves — first-class
  tests, not a separate pipeline.
- **What the operator sees on failure.** Tool stdout captured into
  the pytest assertion (mypy error list, xenon offender list, ruff
  JSON, vulture confidence-tagged finding, import-linter contract
  name).
- **Operator action.** Fix the structural drift, or, if the violation
  is intentionally accepted, tag the waiver `# TODO(ADR-NNNN)` with
  a superseding ADR opened in the same change. Untracked waivers
  are not allowed.
- **Test count today.** 4 tests (one per tool); import-linter runs
  inside `test_typing.py` collection.

#### Oracle 5 — Backtest Layer A (NOT BUILT)

- **Status.** No `tests/backtest/`, no `app/backtest/`, no
  scaffolding.
- **Intended location.** `tests/backtest/` with provider-traced
  post-event IV series alongside each anchor.
- **Tool (TBD at build time).** Plain pytest with a separate data
  ingest path and a pytest marker (`@pytest.mark.backtest`) so it
  does not run in the per-commit loop.
- **Oracle source.** Realised IV outcome from the same providers as
  anchors (Twelve Data, Finnhub historical OHLC for `^VIX`, `^OVX`),
  *not* the SRS formula. This independence is the entire point.
- **Cadence.** Nightly / pre-release, not per-commit.
- **What the operator sees on failure (when built).** An assertion
  of the form `event E: classifier emitted severity s, realized IV
  moved x % in next 48 h → expected severity in [a, b], FAIL`.
- **Operator action.** Diagnose calibration drift versus regime
  change; the response is not always *"fix the code"* — sometimes it
  is *"the market regime moved and the registry `N_L` needs review"*.
- **Statistical limits to remember.** ~50 anchor events from
  2010–2025 means coarse-grained rank-correlation across the
  catalogue + per-event band-violation. Not a fine-grained
  calibration tool. Band-derivation rule and any regime-conditioning
  require `/trader` + `/statistician` review when Layer A is built.
  See [ADR-0003](doc/adr/0003-test-oracle-architecture.md)
  *Acknowledged limits of Layer A* for the full enumeration.

#### Operational gate G1 — `/health`

- **Behaviour.** While the FastAPI lifespan runs `populate_windows()`,
  `/health` returns `503 {"status": "not_ready"}`. Once
  `state.is_ready` flips, `/health` returns `200 {"status": "ready",
  "windows": { "<source>/<symbol>": { indicator_class, values_count,
  last_update, staleness_seconds } }}`.
- **Where it lives.** [`main.py`](main.py) lines 89–103.
- **Tested by.** [`tests/acceptance/test_health_acceptance.py`](tests/acceptance/test_health_acceptance.py)
  (2 tests).
- **What the .NET caller does.** Gates ingestion on the 503 → 200
  transition. No `/classify` call before ready.

### How to run

From `apps/classification/`:

```text
pytest                       # oracles 1–4 + G1 gate test (31 tests: 7 + 11 + 7 + 4 + 2)
FRED_API_KEY=… pytest tests/integration/test_bootstrap.py
                             # live-API bootstrap, skipped by default
# Layer A — NOT BUILT
```

There is no `.pre-commit-config.yaml`, no `.github/workflows/`, no
`Makefile` in this app today. Invocation is manual `pytest`. **This
is a named gap** in project ADR-0001's execution model — sequencing
step 1 (hooks) and step 3 (CI) close it. Until then, regression can
reach `master` if the operator forgets to run `pytest` between edit
and commit.

### Build status

| # | Oracle / gate | Status | Live red-test gaps |
| --- | --- | --- | --- |
| 1 | Contract shape | BUILT | — |
| 2 | Anchor scenarios | BUILT | `market_data_vix_low_vol_regime_2017_10_05`, `market_data_vix_normal_day_2019_07_15` (long-horizon ECDF migration) |
| 3 | Mathematical axioms | BUILT | `test_vol_compression_returns_negative_score`, `test_zero_deviation_returns_near_zero_score`, `test_parametric_gate_failure_returns_degraded_certainty` (signed-score migration) |
| 4 | Structural fitness | BUILT | — (with documented `# TODO(ADR-0001)` waiver) |
| 5 | Backtest Layer A | **NOT BUILT** | All — no scaffolding exists |
| G1 | `/health` gate | BUILT | — |

The signed-score and long-horizon-ECDF reds in oracles 2 and 3 are
expected during the in-flight migration; they are the harness
working as designed, not harness defects.

### Case A vs Case B at this component

Per project ADR-0001, the steering loop has two entry points:

- **Case A — oracle red** is what every red row in the build-status
  table represents. Pytest output makes the bug class visible. The
  agent reads the failure and classifies. Almost always: fix the
  implementation.
- **Case B — oracle escape** at this component covers everything
  that survives the green-pytest state. **Layer A (when built)
  catches one specific Case B sub-class** — calibration mismatch
  with realized market behavior. Other Case B sub-classes
  (wrong-level abstraction the fitness layer misses, frame-of-
  reference errors, novel bug classes outside any oracle's mapping)
  remain reachable only through specialist-skill review or external
  observation. That is the autonomy ceiling, not a gap to close.

---

## Layer 5 — Decision durability

| Artefact | Role |
| --- | --- |
| [`doc/adr/0001-per-indicator-tuning-parameters.md`](doc/adr/0001-per-indicator-tuning-parameters.md) | Wrong-level tuning postmortem (Phase A) |
| [`doc/adr/0002-ecdf-severity-and-backtest-harness.md`](doc/adr/0002-ecdf-severity-and-backtest-harness.md) | ECDF severity formula + indicator registry + Layer A commitment |
| [`doc/adr/0003-test-oracle-architecture.md`](doc/adr/0003-test-oracle-architecture.md) | Five-oracle architecture for this component (Layer 4 of project harness) |
| [`doc/adr/srs-annex-cls-001-severity-formula.md`](doc/adr/srs-annex-cls-001-severity-formula.md) | Superseded annex (folded into SRS) |
| [`LIMITATIONS.md`](LIMITATIONS.md) | Accepted-gap register |
| This file | Regenerable per-component harness inventory |

The steering loop applies here. An ADR captures an architectural
decision. A bug alone does not warrant one; a bug plus a new control
does. A bug plus an accepted gap is a `LIMITATIONS.md` entry.
**Silent gaps are not a permitted state.**

---

## Response protocol — when an oracle fires

When any of oracles 1–5, or the G1 gate, flags a failure:

1. **Classify** the failure mode and identify which oracle caught it.
2. **Caught by the responsible oracle (Case A) — harness working as
   designed.** Fix the bug; add a regression case (new anchor /
   axiom / fitness rule / Layer A scenario) only if the existing
   one didn't cover the variant. No ADR. Normal commit + test
   discipline. The common path.
3. **Bug observed despite green pytest (Case B) — harness gap.** Pick
   one:
   - **Add a new control.** Open an ADR for the architectural
     decision (new fitness rule / axiom / anchor class / contract
     clause / Layer A scenario type). The bug appears in Context as
     the trigger. The ADR must name which oracle class catches the
     failure mode going forward.
   - **Accept the gap.** Document in [`LIMITATIONS.md`](LIMITATIONS.md)
     with failure mode, rationale, and the conditions that would
     re-open the question.

---

## Current gaps

Tracked here so the next task can pick them up. Closing a gap that
requires a new control opens an ADR per the response protocol above;
gaps closed by routine implementation work do not.

### Layer 4 (oracles) — in-flight migrations

- **Signed-score migration in implementation.** SRS CLS-001 specifies
  signed severity in `[-1.0, +1.0]`; verify strategy code emits
  signed scores end-to-end and that the OpenAPI score range is
  migrated in lockstep with the .NET-side schema. *Currently failing
  axioms: `test_vol_compression_returns_negative_score`,
  `test_zero_deviation_returns_near_zero_score`.*
- **Long-horizon ECDF implementation.** SRS CLS-001 anchors severity
  to a long-horizon reference window `H_L` of length `N_L`
  (binomial-SE bounded, ≥ 278); verify per-symbol windows run at that
  depth in production bootstrap, not at the legacy short-window
  depth. *Currently failing anchors:
  `market_data_vix_low_vol_regime_2017_10_05`,
  `market_data_vix_normal_day_2019_07_15`.*
- **Parametric fallback path.** SRS CLS-001 specifies a parametric
  fit fallback for indicator classes whose `N_L` is unattainable
  (monthly macro). Verify the fallback exists and routes through
  CLS-009-style degraded confidence. *Currently failing axiom:
  `test_parametric_gate_failure_returns_degraded_certainty`.*

### Layer 4 (oracles) — unbuilt

- **Backtest Layer A.** [ADR-0003](doc/adr/0003-test-oracle-architecture.md)
  specifies the market-IV-outcome oracle layer; scaffolding does
  not exist in the repo today. Project ADR-0001 names the autonomy
  ceiling: even when built, Layer A only catches one Case B
  sub-class.
- **CROSS_ASSET_FLOW anchors** — deferred until the strategy lands
  on ECDF.
- **GEOPOLITICAL anchors** — deferred until those strategies exist;
  EVENT_ASSESSMENT via LLM, not covered by ECDF.

### Layer 3 (permissions) — automation gaps

- **No PostToolUse hooks.** Project ADR-0001 commits to a
  `ruff + mypy` PostToolUse hook on `app/**/*.py` and a fixture-
  reminder hook on `tests/acceptance/fixtures/*.json`. Until this
  lands, Layer 4 is operator-manual.
- **No Stop hook.** Project ADR-0001 commits to a `pytest tests/ -x`
  Stop hook so the agent cannot terminate a turn with unfixed reds.
  Until this lands, regression can reach `master`.

### Layer 2 (cognitive tools) — convention gap

- **No AGENTS.md convention** instructing the agent to surface
  skill-invocation reminders in end-of-turn output (project ADR-0001
  sequencing step 2). Today the operator must remember the trigger
  map themselves.

### Cross-cutting / future

- **Property-based tests on `_compute_temporal_relevance`** — deferred
  until the function is extracted to `app/math/temporal.py`.
- **Mutation testing (`mutmut`).** A meta-check on the test suite
  itself, scoped to `app/strategies/*.py` and `app/math/*.py`.
  Deferred. Not part of the locked architecture; candidate future
  control.
- **CI workflow (GitHub Actions).** Project ADR-0001 sequencing step
  3 — defence in depth against bypassed local hooks.
