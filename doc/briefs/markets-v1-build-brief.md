# PrimeScore Markets v1 — build brief

You are a principal .NET engineer and architect. Build the first meaningful version of
**PrimeScore Markets** in the repository at `D:\Work\invex` (GitHub `raadupop/primescore-ai`,
branch `master`). The Python classifier and the local replay dashboard already exist; the .NET
engine the design documents describe does not. v1 turns the repository from a classifier
prototype with a static page into a product that ingests real observations every day,
classifies them, aggregates them into a composite score and a volatility dislocation, records
what it would have decided, replays any past date without look-ahead, and shows all of it in a
web interface that a user can read and question.

Read this brief completely before touching a file. Where it decides something, implement the
decision. Where it says "ask", stop and ask. Where it is silent, follow the repository's own
documents, listed in §2.

---

## 1. Product definition and honesty rules

**Product.** Event-driven volatility intelligence for stock-options research. The user sees, per
reference instrument (VIX for the equity context, OVX for the oil context), what changed, how
unusual it is relative to five years of history, what the signal-implied volatility scenario is,
whether the system would have deployed, and why. Every number is traceable to a source
observation, a classifier response, a configuration version and a ledger entry.

**What v1 is not.** It is not a forecast, a backtest of returns, a trading system or a broker
connection. It runs in simulation mode only (SRS POS-003). Position construction, exits and
risk limits are Milestone B (§3), behind the same contract, after v1 ships.

**Honesty rules, non-negotiable in code, UI and docs.**

- Distinguish, everywhere they appear: observed events, statistical classifications,
  scenarios, and decisions. A scenario is arithmetic under a stated assumption; label it so.
- Never present the dislocation, the composite, a replay or a validation-set result as
  predictive performance or profitability. The UI copy for the scenario keeps the existing
  wording: "uncalibrated multiplier, not an independent volatility forecast".
- Missing data stays missing. No consensus value, observation or severity is ever invented.
  A category that cannot be classified is shown as absent with the reason.
- Fixture and curated data are labelled with their provenance (provider, series id,
  retrieval time, URL) exactly as `apps/classification/tests/acceptance/fixtures/ANCHORS.md`
  requires.
- Secrets never enter source, configuration files or logs (SEC-002). The FRED key lives in
  `dotnet user-secrets` or environment variables.
- No deployment, no public-site change, no push to any remote without the operator's explicit
  authorization in the session.

---

## 2. Ground truth: read these first, in this order

| Path (relative to `D:\Work\invex`) | Why |
| --- | --- |
| `README.md`, `AGENTS.md`, `CLAUDE.md` | Implemented scope, operator rules (brevity, one destination per fact, Case A / Case B handling) |
| `doc/srs/PrimeScore-SRS.md` (v2.3.3) | Requirements. §3 definitions, §5.1–5.3 and §5.7–5.11 are the v1 core |
| `doc/PrimeScore-API-v1.yaml` | External API contract; single source of truth for the engine's HTTP surface |
| `apps/classification/doc/openapi.yaml`, `apps/classification/AGENTS.md`, `apps/classification/LIMITATIONS.md`, `apps/classification/HARNESS.md` | Classifier contract, boundaries and known limits |
| `infra/registry.yaml`, `infra/README.md` | Indicator classes, symbols, `N`, `N_L`, cadences, bootstrap providers |
| `LIMITATIONS.md`, `docs/STATUS.md` | The two known contradictions (CLS-002 range, CLS-006 negative IV) and the audit baseline |
| `doc/concepts/statistics.md` §8–§23, `doc/concepts/trading.md` | The formulas explained; corroboration, bypass, dropout schedule |
| `doc/session-notes/2026-04-13-sensitivity-factor-risk-analysis.md` | Realistic dislocation sizes (1–4 points, not 10) and the latency reality |
| `doc/session-notes/2026-04-26-classifier-architecture-three-questions.md` | Why the registry is YAML, why Python stays, and that the classifier is process-local stateful |
| `apps/classification/doc/adr/*.md`, `doc/adr/*.md`, `doc/conventions/*.md` | Decision records and the ADR discipline to follow for new ADRs |
| `harness/ORACLE.md`, `harness/STEERING.md`, `scripts/checks.py` | The validation gate; keep it green and extend it |
| `apps/demo/replay_app.py`, `apps/demo/static/*`, `docs/DEMO.md` | The existing replay demo: its data flow is the seed of the engine pipeline; its UI is superseded by v1 |
| `doc/research/volatile-requirements-friction.md`, `doc/research/srs-revision-v2.3.3-answers.md` | Why the spec looks the way it does |

Then run the existing gate before changing anything, and keep it green throughout:

```sh
cd D:\Work\invex
python -m venv .venv && .venv\Scripts\Activate.ps1
python -m pip install -r apps/classification/requirements-dev.txt
bash harness/check-suite.sh        # expected: 38 passed / 4 skipped classification tests, 47 repository tests, 6/6 import contracts
```

---

## 3. Scope

### Milestone A = v1 (this brief)

| Area | Requirements delivered | Notes |
| --- | --- | --- |
| Signal ingestion | SIG-001, SIG-002, SIG-003, SIG-004 | Four categories ingested and persisted append-only with point-in-time queries; two are classifiable in v1 (§7) |
| Classification | CLS-001 (via the Python classifier), CLS-002, CLS-004, CLS-006, CLS-009 (consumed) | CLS-002 and CLS-006 implemented with the resolutions in §9 |
| Decision | DEC-001, DEC-002, DEC-003 | Deploy/idle with recorded conditions and structured explanation; no positions |
| Analytics | ANA-001 | Point-in-time replay producing the decisions the system would have made |
| Observability | OBS-001 (+ classifier reasoning traces stored, the useful half of OBS-002) | Correlation id from ingestion to decision |
| Audit | AUD-001, AUD-002 | Append-only, hash-chained ledger with a verification command |
| Security | SEC-001, SEC-002 | Two roles (READ, ADMIN); secrets outside the repository |
| Evolvability | EVO-001 (a and b), EVO-002 | Black-box API acceptance suite plus structural suite |
| Non-functional | NFR-001, NFR-003 | Composite within 120 s of ingestion; every referenced parameter configurable without rebuild |
| Web UI | not an SRS requirement; the product requirement of this brief | §10 |

### Milestone B (after v1, same contract, not in this brief)

POS-001, POS-002, POS-003, POS-004, EXT-001, EXT-002, EXT-003, EXT-004, RSK-001, RSK-002,
RSK-003, ANA-002, DEC-004. Reason for deferring: every one of them needs options prices
(strikes, premiums, Greeks) and there is no options data source in the repository; simulating
positions on invented prices would violate the honesty rules. v1 must leave the module slots and
the API endpoints for them in place, returning `501 Not Implemented` with a body that says so.

### Out of v1 entirely

CLS-003, CLS-005, CLS-007, CLS-008 (the AI/LLM geopolitical route and its defenses; needs
an Anthropic key, an evaluation set and prompt-injection tests), SIG-005, OBS-003, SEC-003,
NFR-004, NFR-005, EVO-003 and the DeltaFeed measurement programme (§8.x of the SRS). Leave
the geopolitical route exactly as it is (HTTP 501 in the classifier).

---

## 4. Architecture decision: Modular Monolith over an append-only ledger

The SRS plans six engine iterations for a research study (Transaction Script, Vertical
Slice, Clean + Rich Domain, Clean + Event Sourcing, Modular Monolith, Service Extraction).
v1 is a product, not a study run. Choose the structure that fits the product's actual
forces and keep the study possible later.

**Decision.** Build v1 as a **modular monolith** (the SRS Iteration 5 shape: seven functional
areas as independently bounded modules, vertical slices inside each module, Clean Architecture
dependency rules at module boundaries, all inter-module communication through published
contract assemblies) whose persistence backbone is an **append-only, hash-chained ledger**
with synchronous projections (the single idea from Iteration 4 that this domain genuinely
needs). One deployable process hosts the API, the UI and the schedulers.

**Why this fits.**

- The v1 domain is a stream of facts: observations, assessments, composites, dislocations,
  decisions. SIG-004 (append-only, point-in-time), AUD-001/AUD-002 (append-only,
  tamper-evident) and ANA-001 (replay the decisions the system would have made) are all
  the same mechanism when the ledger is the source of truth and every read model is a
  projection of it. That mechanism is cheap here and expensive to bolt on later.
- Module boundaries are coarse and machine-enforceable (project references + NetArchTest),
  which is exactly what keeps AI-driven changes contained; the SRS itself predicts fewer
  boundary leaks for Iteration 5 than for Iteration 3.
- Milestone B (positions, exits, risk) attaches as new modules consuming published decision
  contracts, without touching the intelligence modules.
- One operator, one process: no second deployable, no message broker, no distributed tracing
  to operate.

**Why not the others.** Transaction Script rots as soon as the configurable formulas,
regimes and replay semantics multiply; Vertical Slice alone duplicates the ledger,
correlation and point-in-time concerns per slice (the SRS predicts exactly this for CT-03);
Clean Architecture across the whole application is heavier than seven modules need and its
layer rules are the ones the SRS marks as AI-fragile; full Event Sourcing of every aggregate
(configuration, positions later) adds ceremony without benefit; Service Extraction solves a
team and scaling problem this project does not have.

**Relationship to the DeltaFeed study.** Not abandoned, deferred. v1's black-box API
acceptance suite *is* the study's constant test suite (EVO-001a), which is a net gain for the
study. When the study runs, each iteration lives in its own directory or branch with the agent
context restricted per ACX-001 so that v1's code is not visible to it. Record this in the
architecture ADR (§13, M0).

---

## 5. Solution layout

Create `apps/engine/` (remove the stale `apps/core/` line from `.gitignore`; that placeholder
predates this brief). Target **.NET 10** (SDK 10.0.301 is installed), C# 14, nullable and
implicit usings on, `TreatWarningsAsErrors` on.

```
apps/engine/
  PrimeScore.Engine.sln
  Directory.Build.props                    shared analyzers, nullable, warnings as errors
  src/
    Api.Contracts/PrimeScore.Api.Contracts       NSwag-generated from doc/PrimeScore-API-v1.yaml: DTOs, controller bases, typed client
    Host/PrimeScore.Engine.Host                  ASP.NET Core host: API controllers, Blazor UI, hosted schedulers, auth, composition root
    SharedKernel/PrimeScore.SharedKernel         ids, clock, correlation id, canonical JSON, config-version primitives, registry reader
    Ledger/PrimeScore.Ledger                     append-only hash-chained ledger + projection infrastructure (EF Core, SQLite)
    Modules/Ingestion/PrimeScore.Modules.Ingestion(.Contracts)
    Modules/Classification/PrimeScore.Modules.Classification(.Contracts)   classifier client, assessments, composite, dislocation
    Modules/Decision/PrimeScore.Modules.Decision(.Contracts)
    Modules/Analytics/PrimeScore.Modules.Analytics(.Contracts)             replay, validation-set evaluation
    Modules/Configuration/PrimeScore.Modules.Configuration(.Contracts)     versioned schemes, thresholds, regime maps, conditions
    Modules/Positions, Modules/Exits, Modules/Risk                          Milestone B slots: contracts only, endpoints return 501
  tests/
    PrimeScore.Acceptance.Api                    black-box HTTP suite (EVO-001a); references only Api.Contracts
    PrimeScore.Structural                        NetArchTest rules (EVO-001b, EVO-002)
    PrimeScore.Modules.*.Tests                   unit tests with hand-calculated expectations
  data/
    consensus/*.csv                              operator-curated macro consensus with provenance columns (§7)
    validation-events.json                       the ten SRS §9 events with expected outcomes
```

**Structural rules (encode in `PrimeScore.Structural`; they are the definition of the
architecture, and CI fails when they fail):**

1. No module project references another module's implementation project; only
   `*.Contracts` assemblies cross module lines.
2. `PrimeScore.Modules.*.Contracts` reference only `PrimeScore.SharedKernel`; never
   `PrimeScore.Ledger`, never `PrimeScore.Api.Contracts`, never a module implementation.
3. `PrimeScore.Ledger` and `PrimeScore.SharedKernel` reference no module.
4. The Host references module implementations only through each module's registration entry
   point (`services.AddIngestionModule(...)`) and maps module contract types to API DTOs in
   the Host; API DTO types never appear inside modules.
5. Command handlers do not return domain state; query handlers do not write (CQRS as the SRS
   prescribes from Iteration 3 onward).
6. No handler reads the ledger table directly; all reads go through projections.
7. Positions, Exits and Risk implementation projects do not exist in v1; their contracts do.

---

## 6. Runtime model

**Ledger.** One SQLite database file (WAL mode) per environment, path from configuration
(default `apps/engine/var/engine.db`, Git-ignored). Table `ledger`: `sequence` (monotonic),
`recorded_at`, `kind`, `entity_id`, `correlation_id`, `config_version`, `payload` (canonical
JSON), `prev_hash`, `hash = SHA-256(prev_hash ‖ kind ‖ payload)`. Kinds in v1:
`SignalIngested`, `SignalRejected`, `AssessmentRecorded`, `AssessmentUnavailable`,
`CompositeComputed`, `DislocationComputed`, `DecisionMade`, `ConfigurationChanged`,
`ReplayRun`. An admin command verifies the chain end to end (AUD-002).

**Projections.** Each module owns its read tables (prefix per module, one database, separate
`DbContext` per module, EF Core migrations per module). Projections are updated synchronously
in the same transaction as the ledger append. Every projection row carries the ledger
`sequence` that produced it. `as_of` queries filter on the *observation* timestamp of the
underlying signal, never on `recorded_at` (SIG-004).

**Configuration.** All parameters the SRS calls configurable are data: weighting schemes,
expected categories per context, corroboration windows, bypass percentile, dropout schedule,
sensitivity map, dislocation thresholds, deploy conditions, reference instruments per context,
ingestion schedule. Every change writes `ConfigurationChanged` and bumps a `config_version`;
every computed record carries the version it used; replay may override any of them for its run
without changing the stored version (ANA-001, NFR-003). Seed the defaults from §9 on first
start and mark every seeded value `calibration: "uncalibrated default"` in the UI.

**Pipeline.** `SignalIngested` → classification (per signal, through the Python classifier,
§8) → recompute composite for every context the signal's category and instrument belong to
→ dislocation for the context → decision for the context → ledger entries with one
correlation id from ingestion to decision (OBS-001). Synchronous, in-process, one signal at a
time per context; NFR-001 is trivially met at daily cadence, and the acceptance test asserts
it.

**Scheduling.** A hosted `BackgroundService` runs the FRED pull once per day at a configured
UTC time and on demand from the UI (in-process call, not a new public endpoint). The public
contract gains only `GET /health` (additive; record the amendment in the yaml and CHANGELOG).

**Contexts.** A *context* is a reference instrument plus the categories expected for it. v1
ships two: `equity` (reference VIX; expects MARKET_DATA, MACROECONOMIC, CROSS_ASSET_FLOW) and
`oil` (reference OVX; expects MARKET_DATA, CROSS_ASSET_FLOW). The API's context-less endpoints
(`GET /classification/composite`, `/dislocation`) take an optional `context` query parameter,
default `equity`; add the parameter to the contract as an additive amendment.

---

## 7. Data for v1 and what is honest about each category

| Category | Source in v1 | Classified? | Honest presentation |
| --- | --- | --- | --- |
| MARKET_DATA | FRED daily closes for every registry symbol with `provider: fred` (`VIXCLS`, `OVXCLS`, `VXNCLS`, `GVZCLS`, `EVZCLS`, `RVXCLS`, `VVIXCLS`), full history from 2011 so that `N_L = 1260` is satisfiable from 2016 onward | Yes (classifier `MARKET_DATA` route) | `verified: false` symbols in the registry are ingested but the UI marks their series "provider mapping unverified" |
| MACROECONOMIC | FRED actuals (`CPIAUCSL` → YoY, `ICSA`) plus an operator-curated consensus CSV per indicator with columns `release_date, actual_source, consensus, consensus_source, consensus_url, retrieved_at` | Yes, only for prints that have a consensus row | A print without consensus is ingested and shown as "awaiting consensus", never classified. The classifier's macro payload requires `expected`; do not fabricate it. Do not reuse `app/bootstrap/cpi_consensus.py` values without a cited source per row |
| CROSS_ASSET_FLOW | Basket prices from FRED (`SP500`, `DGS10`, `DCOILWTICO`, `DEXUSEU`) ingested daily | Only after the optional classifier change #2 (§8); otherwise stored and marked "classifier route not implemented (501)" | Absent from the composite with the reason shown |
| GEOPOLITICAL | Structured events only, curated from the SRS §9 validation set, submitted through `POST /admin/signals`, labelled `HUMAN_CURATED` | No (classifier returns 501; CLS-003 is out of v1) | Ingested and visible on the tape; absent from the composite with the reason shown |

FRED access: `fredapi` is the Python side's client; the .NET side calls the FRED JSON API
directly with the key from user-secrets. Respect FRED terms: cache responses, never redistribute
raw series through the public site. Idempotent ingestion key: `(source_identifier, instrument,
observed_at)`; re-pulling the same day changes nothing.

---

## 8. The classifier boundary

The Python classifier is the constant. Do not change its formulas, registry values, fixtures
or test assertions. Two changes are approved because v1 cannot meet ANA-001 without the first,
and the second is a fully specified RULE_BASED formula the SRS already owns.

**Change #1 (required, M2): caller-supplied reference window.** Add an optional
`reference_window` object to `ClassifyRequest` in `apps/classification/doc/openapi.yaml`:
`{ "values": number[], "last_update": date-time }`. When present, the strategy computes the
response against that window exactly as it does today against `state.windows[symbol]`, and
**does not read or mutate process state**. When absent, behaviour is unchanged. Rationale:
the classifier is process-local stateful (see the 2026-04-26 session note); point-in-time
replay needs the window as of the signal's timestamp, which only the engine's ledger can
supply; this is the "every request carries its own context" option that note describes, and
it is what `apps/demo/replay_app.py` already does in-process by seeding `state.windows`.
Deliverables: contract change, implementation across the two live strategies, acceptance
tests proving state is untouched and that a supplied window reproduces the anchor fixtures
byte for byte, an ADR in `apps/classification/doc/adr/` in the repository's ADR style, and an
entry in `apps/classification/LIMITATIONS.md` §6 noting that the engine now owns history.
The engine builds the window from its ledger: all observations of the symbol strictly before
the signal's observation time, most recent `N_L` (or `N` when `N_L` is null), read from
`infra/registry.yaml`.

**Change #2 (recommended, after M3): CROSS_ASSET_FLOW strategy.** Implement `CORR_DEVIATION`
per CLS-001 and `statistics.md` §13–§14: baseline correlation over the long window, current
correlation over a short window (both lengths from a new registry class, e.g.
`equity_rates_corr` with `N_L = 1260`), signed deviation, ECDF rank, certainty. Same test
discipline as the market-data anchors: hand-derived bands, sourced series. If you cannot
source the pair history with provenance, do not implement it; leave the route at 501.

Anything else in the classifier: ask.

---

## 9. Resolved specification conflicts (implement these; propose them as SRS v2.3.4)

Write each as an ADR in `doc/adr/` (project level) and collect them in
`doc/research/srs-revision-v2.3.4-proposal.md`. Do not edit the SRS text itself; the operator
accepts amendments.

1. **CLS-002 range violation.** As written, a stale-but-present category discounts the
   denominator and pushes the composite outside `[-1, 1]`. Implement instead
   `CompositeScore = Σ_{c ∈ present} w_c · d_c(Δt_c) · s_c / Σ_{c ∈ present} w_c`, where
   `present` = categories with at least one confirmed assessment in the window, `d_c(Δt_c)`
   is the SRS dropout schedule applied to the category's staleness (1.0 when fresh), and
   `s_c` is the signed category conviction. Range is `[-1, 1]` by construction; staleness
   reduces magnitude, which is the intent the rationale states.
2. **CLS-002 opposing-signal cancellation.** The max-of-|conviction| selection cannot cancel
   opposing signals, but the verification demands it. Implement
   `s_c = max⁺_c − max⁻_c`, where `max⁺_c` is the largest positive `severity × certainty`
   among confirmed assessments in category `c` and `max⁻_c` the largest magnitude among the
   negative ones (each 0 when none). Two opposing near-equal signals cancel; one strong signal
   with weak same-direction company keeps its full value; a single high-conviction bypass
   signal contributes alone.
3. **Expected vs absent categories.** "Expected" categories are configured per context
   (§6). An expected category with no confirmed assessment is excluded from the sums (as the
   SRS says) and listed in `absent_sources`; the deploy condition
   `min_contributing_sources` (§9.6) is where absence bites. A category that is not expected
   is neither listed nor penalized.
4. **Corroboration windows for daily cadence.** Add `daily_market_data = 2 trading days`
   and `daily_cross_asset = 2 trading days` to the window table; keep
   `macro_release = 1800 s`. A confirmed assessment is one with a second confirmed-eligible
   assessment of the same category inside the window, or one whose `|severity| ≥ p_bypass`
   (default 0.999). Unconfirmed assessments are dropped from the composite when their window
   lapses, never kept pending.
5. **CLS-006 negative implied volatility.** Configuration validation rejects any
   `SENSITIVITY_FACTOR` outside `(0, 1]`, so `1 + composite × k ≥ 0` always. Regime
   detection uses the ECDF percentile of the reference instrument's observed level within its
   own `N_L` window: `low` below p30, `high` above p70, `normal` between; defaults
   `low = 1.0, normal = 0.75, high = 0.5`. The SRS example values (1.5/1.0/0.6) are recorded
   as rejected in the ADR, with the exponential form `IV × exp(composite × k)` noted as a
   calibration-era alternative. Keep the SRS arithmetic otherwise unchanged and expose
   `sensitivity_factor` and `reference_instrument` on the dislocation record as the contract
   already does.
6. **DEC-001 default conditions (uncalibrated, all configurable):** `dislocation_value ≥
   threshold` (equity 1.5 index points, oil 3.0, from the 2026-04-13 note's realistic sizes),
   `|composite| ≥ 0.5`, `contributing_sources ≥ 1`, `top_signal_certainty ≥ 0.5`, `newest
   contributing observation ≤ 2 trading days old`, `no active cooldown` (always true in v1,
   present so Milestone B can wire RSK-001 without a contract change). Every condition's
   required and actual value is recorded (DEC-001) and shown (DEC-003).
7. **Sign interpretation.** A negative composite yields a negative dislocation and, when the
   magnitude conditions pass, a `DEPLOY` decision whose explanation says "vol-compression
   scenario"; the UI colours expansion lime and compression blue, as the public site does.
8. **Validation set semantics.** "Deploy by date D" means a `DEPLOY` decision exists with
   observation date in `[D − 1 trading day, D]`. Replay results are *reported* against the
   SRS §9 expectations, per event, with the reason when an event is not evaluable (for
   example a geopolitical-only event with no classifiable category). The Iran Feb 2026
   criterion is a calibration target shown in the UI, not a gate that blocks v1. Thresholds
   may be tuned on this set only with an explicit "in-sample" label on the configuration
   version.

---

## 10. Web UI

Blazor Web App in the Host project, interactive server render mode, no frontend build
toolchain. Port the design tokens of `sites/markets/` (near-black surfaces, lime accent
`#c5f56a`, blue `#91b4ff`, Space Grotesk bundled locally with its OFL licence, the bar-chart
wordmark, the panel, button, status and slider styles). Charts are Razor components emitting
native SVG: one hue per series, the composite line in blue, decision markers in lime,
thresholds and grid recessive, a legend whenever two or more series are drawn, a hover
crosshair with a tooltip on line charts, and a table view for every chart. Text uses the text
colours, never the series colour.

Screens, each with the empty, loading and degraded states designed rather than left blank:

1. **Overview.** Per context: latest observed IV, latest composite (signed, with contributing
   and absent categories and reasons), dislocation (value, threshold, breached), latest
   decision with a one-line explanation, data freshness per source, a five-year sparkline of
   composite and dislocation. Everything links to its ledger entry.
2. **Signal tape.** Chronological signals across all four categories with validation status,
   assessment (severity, certainty, its two dimensions, method, degraded flags such as
   `window_degenerate`, `unknown_indicator`, fallback and staleness), provenance and the
   classifier's reasoning trace. Filters: context, category, instrument, date range, `as_of`.
3. **Composite and dislocation history.** Daily series for the selected context with decision
   markers, regime shading from the sensitivity map, the active thresholds, and the table.
4. **Decision explorer.** One decision: conditions evaluated with required vs actual,
   contributing and dissenting signals linked to their assessments, dislocation, configuration
   version, correlation id, and the audit chain segment.
5. **Replay.** Pick a validation event or a date range, optionally override configuration,
   run, and see the decision timeline beside the expected outcome, with "not evaluable"
   stated where it applies. Results are stored as `ReplayRun` ledger entries.
6. **Configuration.** View and edit (ADMIN) every parameter in §9, with version history; each
   edit records who, when and the diff.
7. **Sources and health.** Per source adapter: last successful pull, next scheduled run,
   last error; registry view; classifier `/health` and version; ledger verification button
   with its last result.

The existing Python demo (`apps/demo`) stays runnable and tested until the operator retires
it after M5; do not delete it in v1.

---

## 11. API and contracts

Contract-first, as `doc/archive/PrimeScore-PROJECT-CONTEXT-18Mar.md` intended: NSwag generates
`PrimeScore.Api.Contracts` (DTOs, controller base classes, typed client) from
`doc/PrimeScore-API-v1.yaml` at build time; the Host implements the generated bases. Allowed
contract amendments in v1, each recorded in the yaml `info.version` bump and `CHANGELOG.md`:
`GET /health`, the optional `context` query parameter, and the `501` responses for Milestone B
endpoints. Nothing else changes in the contract without asking.

Endpoints implemented in v1: `POST /admin/signals`, `GET /classification/composite`,
`GET /classification/assessments`, `GET /classification/dislocation`, `GET /decisions`,
`GET /decisions/{id}`, `GET /audit`, `GET /logs`, all `PUT /config/*` that correspond to §9
parameters, `POST /replay`, `GET /health`. Endpoints returning 501 with an explanatory body:
`/positions*`, `/exits`, `/risk/status`, `/config/risk-limits`, `/config/holding-period`,
`/config/execution-mode`, `/config/approval-threshold`.

**Authentication (SEC-001).** Bearer tokens mapped to roles `READ` and `ADMIN`; token
hashes in user-secrets or environment, never in files under the repository. The UI uses cookie
authentication with one operator account (password hash from secrets) carrying `ADMIN`.
Unauthenticated calls get 401, `READ` calls to admin endpoints get 403, and the acceptance
suite proves both.

---

## 12. Testing and gates

- **API acceptance suite (EVO-001a).** Separate test host, pure HTTP, references only
  `PrimeScore.Api.Contracts`. Starts the engine with a fresh temporary database and the Python
  classifier as a subprocess (`BOOTSTRAP_MODE=disabled`, registry path pinned, exactly as
  `tests/demo/test_replay.py` does). Loads data only through `POST /admin/signals`. Covers
  every Milestone A requirement in §3 and, per SRS §11 criterion 12, includes
  **hand-calculated expected outputs** for CLS-002 and CLS-006 computed on paper from the
  §9 formulas before the implementation exists; the anchor fixtures in
  `apps/classification/tests/acceptance/fixtures/` are the sourced inputs. Never derive an
  expected value from program output.
- **Structural suite (EVO-001b).** NetArchTest rules from §5, one test per rule, failing
  before the code that satisfies them exists.
- **Unit tests** inside modules for the ledger hash chain, point-in-time filtering,
  corroboration windows, the `s_c` net-max, the dropout schedule, regime detection and
  configuration validation, each with worked examples in the test names.
- **Replay evaluation.** A test that runs the ten §9 events and writes a report
  (`apps/engine/var/validation-report.md` and the UI) rather than asserting outcomes, except
  that every event must run to completion without error.
- **Gates.** Add `harness/check-engine.sh` (`dotnet build`, `dotnet test` for structural,
  unit and acceptance suites) and call it from `scripts/checks.py` after the existing stages
  so `bash harness/check-suite.sh` remains the single gate. Extend
  `.github/workflows/checks.yml` with a .NET 10 job on Ubuntu and Windows. Mutation testing,
  SAST and the measurement spreadsheet belong to the study, not to v1.

---

## 13. Milestones, each ending in a runnable, demonstrable state

| # | Deliverable | Done when |
| --- | --- | --- |
| M0 | Solution skeleton, NSwag generation, ledger with hash chain and verification, SharedKernel, empty modules with contracts, structural suite, `GET /health`, UI shell with the design, CI job, ADR-0003 (architecture, §4) | `bash harness/check-suite.sh` green including the new .NET stage; structural tests pass; UI shows the shell with health |
| M1 | Ingestion: `POST /admin/signals` with per-signal validation (SIG-002), FRED adapter with backfill and daily schedule, idempotency, point-in-time queries, signal tape screen | Full VIX and OVX history in the ledger; `as_of` acceptance test passes; tape screen lists sourced signals |
| M2 | Classifier change #1; classification of MARKET_DATA and consensus-backed MACROECONOMIC signals with assessments, traces, CLS-004 fallback and CLS-009 flags stored; assessment views | Python gate green with the new tests; assessments for every classifiable signal; degraded states visible |
| M3 | Composite, dislocation, versioned configuration, history screen, ADR-0004 (§9.1–9.5 resolutions) and the v2.3.4 proposal | Hand-calculated CLS-002 and CLS-006 acceptance tests pass; NFR-001 test passes |
| M4 | Decisions with conditions and explanations, audit endpoint, decision explorer, correlation ids end to end | DEC-001/002/003 and AUD-001/002 acceptance tests pass; a decision can be traced from a FRED observation to its ledger entry in the UI |
| M5 | Replay endpoint and screen, validation-set evaluation and report, optional classifier change #2 | Ten events run to completion; report generated; the Iran Feb 2026 result is shown truthfully whichever way it falls |
| M6 | Auth, hardening, runbook (`docs/ENGINE.md`), README and STATUS scope tables, LIMITATIONS updates, CHANGELOG, classification LIMITATIONS §6 note | SEC-001/002 acceptance tests pass; a new operator can set up and run v1 from the runbook alone |

Commit at the end of each milestone with a message that names the milestone and the
requirements it delivers; never commit secrets, `var/`, or generated code that the build
regenerates. Do not push.

---

## 14. Working rules for the agent

- Contract first: change the yaml, regenerate, then implement. Tests before implementation
  for every formula.
- Keep `bash harness/check-suite.sh` green at every commit; Case A (red test that states the
  requirement correctly) means fix the implementation; a wrong assertion is a harness bug to
  flag, not to edit silently.
- Follow `doc/conventions/adr-discipline.md` and `doc/conventions/python-naming.md`. New
  ADRs are numbered after the existing ones and record trade-offs.
- Brevity in every artifact you write, per the root `AGENTS.md`: one destination per fact,
  link instead of restating.
- Stop and ask only for: a FRED API key not being available, a consensus source for the
  macro CSV, any contract change beyond §11, any classifier change beyond §8, any question
  §9 does not already answer, and anything touching `sites/`, deployment or remotes.
- Report at the end of each milestone: what runs (exact commands), what the tests show
  (numbers, skips, failures), what was decided beyond this brief and why, and what remains.
  State plainly what the replay report shows; never round it up into a claim about
  predictive power.

---

## 15. Open items the operator resolves before or during the build

1. FRED API key: provision it into `dotnet user-secrets` for the Host and acceptance test
   projects (the classifier's key stays in its own `.env`).
2. Consensus data: choose the source for `data/consensus/*.csv` (a public release calendar
   with archived consensus, or manual entry with URLs) and confirm that reuse of those
   numbers is permitted.
3. Confirm the two v1 contexts (equity/VIX, oil/OVX) and the default thresholds in §9.6.
4. Decide whether classifier change #2 is in v1 (recommended) or waits.
5. After M6: whether the public Markets page copy is updated to describe the engine, and
   whether the restyled dashboard from `D:\Work\primescore` (commits `56cf439`, `07ec2ab`) is
   ported here or retired with the demo.
