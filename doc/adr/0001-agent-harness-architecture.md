# ADR-0001: Agent harness architecture for PrimeScore AI

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Radu Pop
- **Supersedes:** —
- **Relates to:** [classification ADR-0001](../../apps/classification/doc/adr/0001-per-indicator-tuning-parameters.md), [classification ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md), [classification ADR-0003](../../apps/classification/doc/adr/0003-test-oracle-architecture.md)

## Glossary

Terms that recur are defined once:

- **Agent harness.** The practice of designing the surrounding
  environment, infrastructure, and feedback loops for AI coding
  agents — the system that turns them into reliable autonomous work
  engines. Not the test suite (that is one layer); not the prompt
  (that is one input); the whole compositional system.
- **Oracle.** Any mechanism that can look at the agent's work and
  declare a verdict (pass / fail / specific issue). Examples:
  unit-test runners, type checkers, lint rules, schema validators,
  specialist reviewers, market-reality checks.
- **Oracle red.** An oracle returned a failure signal. A unit-test
  runner reports a failed assertion; a type checker reports a type
  error. The bug was caught.
- **Oracle escape.** A bug exists in the agent's work but every
  oracle in the harness returned green. By definition not detectable
  from the oracles' output — only catchable when a non-oracle
  observer (specialist review, a separate independent oracle,
  production reality) raises the issue.
- **Case A / Case B.** The two entry points to the steering loop
  (defined later in this ADR). Case A = oracle red. Case B = oracle
  escape.
- **Layer A** (specific to the classification component, defined in
  classification ADR-0003). A planned, currently unbuilt oracle that
  takes a historical event from a curated catalogue, runs the
  classifier on it, and compares the emitted severity against the
  market's realized implied-volatility path in the post-event window.
  Example assertion: classifier emitted severity 0.3, realized IV
  moved 28% over the next 48 h, the pairing falls outside the
  expected band → FAIL. Layer A is one specific instance of a broader
  **independent reality-derived oracle** pattern.
- **Independent reality-derived oracle.** Any oracle whose ground
  truth is *what actually happened in the world* rather than what the
  spec or the implementation predicts. The "independence" is from the
  spec-and-implementation pair — useful precisely because correlated
  errors between spec and implementation cannot fool a separately-
  sourced oracle.
- **Calibration mismatch with realized market behavior.** The bug
  class where the formula's output (e.g. severity) and the formula's
  own oracles agree, but the output disagrees with the market
  outcome. The classic *self-validating loop* — tests and
  implementation co-evolved from the same wrong assumption; both
  pass; the product is wrong. ADR-0002 at the classification service
  identifies this as a demonstrated failure mode.
- **Steering loop.** The discipline applied when a bug is observed:
  classify it (Case A or Case B), respond per a fixed protocol (fix
  in place, open ADR + new control, or accept gap in
  `LIMITATIONS.md`). Detailed below.

## Context

Most code changes — implementation and tests — are proposed by AI
agents in one pass.

Two failure modes are demonstrated, both at the classification service.

**Failure 1 — wrong-level abstraction (classification ADR-0001).** The
classifier declared tuning constants like `_TANH_SCALE` and
`_EXPECTED_FREQUENCY_SECONDS` once per strategy module
(`market_data.py`, `macroeconomic.py`). These constants are properties
of indicators, not of strategies: VIX trades intraday, CPI releases
monthly, INITIAL_CLAIMS weekly. Encoding them per strategy is a
modelling error. The test suite did not catch it because every strategy
under test had exactly one indicator — under that input shape, a
per-strategy constant produces the same output as a per-indicator one.

**Failure 2 — bands derived from execution, not from the spec (classification ADR-0002).**
While authoring axis-coverage fixtures, the operator noticed that
the fixtures couldn't independently verify anything: a single agent
had produced both the strategy code and the expected-band fixtures in one pass,
copying the implementation's output into each fixture rather than deriving the
bands from the SRS on paper. No specialist re-derived them independently.
The tests passed because the "correct" answer in each fixture was just whatever
the implementation already produced — the self-validating loop named in
the glossary. The structural reasons this was the path of least resistance, not a
one-off slip, are listed in the causes below.

**What we caught upstream of the code.** Three causes enabled both
failures. The list is what this analysis identified, not a claim of
exhaustiveness.

1. **Qualitative SRS.** CLS-001 was prose-only ("severity is
   quantified; certainty has two independent dimensions combined
   somehow"). With no formula in the spec, an agent can encode any
   formula in the implementation and any band in the test, and both
   pass trivially.
2. **Test infrastructure without axis variance.** Fixtures were
   designed in the same workflow that produced the implementation:
   one indicator per strategy in the input set, and the formula's own
   output as the reference band. No independent oracle; no input
   variation along the axis the bug lived on.
3. **Cognitive skills engaged retrospectively, not proactively.** The
   skills layer (`/trader`, `/statistician`, `/risk-officer`) was not
   part of SRS authoring or fixture design. Both bugs were caught only
   after the operator manually invoked `/trader` outside the
   application's normal flow — in the second case, after the operator
   computed the bands in a separate worksheet and noticed the
   mismatch. Without that out-of-band intervention, both failures
   would have stayed green on every automated oracle.

Common diagnosis: more oracles on the code do not catch these. Both
failures were enabled before the agent wrote a line of code, by
upstream artefacts (the SRS, the test fixtures) authored without
independent review.

Operating constraints shape the response:

- **Single operator.** Any control that depends on social process
  (review board, separate QA team) is already failed. Controls must
  be executable by one person plus their AI agents.
- **AI-mediated edits.** The harness is what stops correlated agent
  errors. It cannot rely on the operator catching what the agent
  missed.
- **Iteration-stable contract.** The classifier must not change shape
  every time a .NET iteration begins; the contract layer makes
  iteration churn safe.
- **Controlled-variable framing** ([SRS](../srs/PrimeScore-SRS.md) ACX-001,
  ACX-002, EVO-001). Agent context is itself a measurement variable
  in the research design; changes to AGENTS.md, skills, or permissions
  require justification because they affect the variable being
  measured.

## Decision

The PrimeScore AI agent harness is **five layers** plus a defined **execution
model** (who fires what, when).

### Layer 1 — Context architecture (what the agent knows)

- `AGENTS.md` at project root + per component.
- `CLAUDE.md` auto-discovery pointer stubs at every level (so Claude
  Code's context loader resolves regardless of which directory the
  session opens in).
- [`.github/copilot-instructions.md`](../../.github/copilot-instructions.md)
  → root AGENTS.md (single source across Claude Code, Codex, Cursor,
  Copilot).
- Document hierarchy: [SRS](../srs/PrimeScore-SRS.md) → OpenAPI (root
  [`doc/PrimeScore-API-v1.yaml`](../PrimeScore-API-v1.yaml) + per-component
  [`apps/classification/doc/openapi.yaml`](../../apps/classification/doc/openapi.yaml))
  → ADRs (project root + per-component) → conventions
  ([`doc/conventions/python-naming.md`](../conventions/python-naming.md)).
- Shared technical context: [`infra/registry.yaml`](../../infra/registry.yaml)
  (indicator registry, per classification ADR-0002).

### Layer 2 — Cognitive tools (specialist review)

The harness has a named slot for specialist review of bug classes
Layer 4 oracles cannot reach. Implementation:
[`.claude/skills/`](../../.claude/skills/). Triggers in §"Concrete
trigger map".

### Layer 3 — Tool permissions (what the agent can do without asking)

- [`.claude/settings.json`](../../.claude/settings.json) — global
  allowlist.
- [`.claude/settings.local.json`](../../.claude/settings.local.json)
  — local override.
- Per ACX-002: the permission set is itself a controlled variable;
  changes require justification.

### Layer 4 — Feedback oracles (how the agent learns it broke something)

Per-component layer. Each component implements feedback oracles sized
to its tech stack and risk profile.

- **Classification service:** five oracles + G1 health gate, locked in
  [classification ADR-0003](../../apps/classification/doc/adr/0003-test-oracle-architecture.md)
  and inventoried in
  [`apps/classification/HARNESS.md`](../../apps/classification/HARNESS.md).
- **.NET iterations (future):** EVO-001 mandates black-box acceptance
  tests against `PrimeScore-API-v1.yaml`; iteration-specific structural
  tests (the architecture being measured varies). Each iteration
  adds its own ADR + per-component HARNESS.md.
- **Universal floor for every component:** at minimum a contract
  oracle + an acceptance oracle + a structural-fitness oracle + an
  operational gate. An independent reality-derived oracle is
  *encouraged* — see ceiling below — but not floor-mandated.

#### The autonomy ceiling

No system of automated oracles can fully verify itself. Each
component closes this gap inside its own Layer 4 instantiation;
classification's is in
[ADR-0003](../../apps/classification/doc/adr/0003-test-oracle-architecture.md)

### Layer 5 — Decision durability (what persists)

- **ADR discipline** (Nygard format). Architectural decisions live in
  `doc/adr/` at the project root and per-component (e.g.
  `apps/classification/doc/adr/`).
- **`LIMITATIONS.md` per component.** Accepted gaps with rationale
  and the conditions that would re-open the question.
- **`HARNESS.md` per component.** Regenerable runbook inventory of
  every harness layer as it instantiates at that component.
- Git history is the immutable substrate. The harness presumes
  Nygard-format ADRs and meaningful commit messages, but those are
  properties of using git well, not separate mechanisms.

## Execution model — who triggers what, when

Default feedback is **agent-triggered or harness-triggered**, with
the operator as the human-in-loop reviewer.

### Three actors

1. **Harness (automated).**
   [`.claude/settings.json`](../../.claude/settings.json) hooks fire
   on well-defined events (PostToolUse, Stop). No human or agent
   action required.
2. **Agent (self-disciplined).** Convention encoded in AGENTS.md
   instructs the agent to surface skill recommendations, run
   commands, or open artefacts when specific concrete events occur.
   Agent reads convention; agent applies it; output flags
   recommendations for the operator.
3. **Operator (manual).** Reads agent output before `git commit`. The
   final human-in-loop. Not a designed mechanism — an unavoidable
   property of running a single-operator system.

### Concrete trigger map

Specific file-path and action triggers:

| Trigger (concrete) | What fires | Actor |
| --- | --- | --- |
| Session start | Auto-load AGENTS.md (root) → AGENTS.md (component) → relevant ADRs | Harness (Claude Code auto-discovery) |
| Tool call | Permission check against `.claude/settings.json` allowlist | Harness |
| `Edit/Write` on `apps/*/app/**/*.py` | Targeted `ruff check` + `mypy` on the file | Harness (PostToolUse hook; see [`.claude/hooks/check-py.sh`](../../.claude/hooks/check-py.sh) → [`harness/check-py.sh`](../../harness/check-py.sh)) |
| `Edit/Write` on `apps/*/tests/acceptance/fixtures/*.json` | Stdout reminder to invoke `/trader` + `/statistician` for band-derivation review | Harness (PostToolUse hook; see [`.claude/hooks/fixture-reminder.sh`](../../.claude/hooks/fixture-reminder.sh) → [`harness/fixture-reminder.sh`](../../harness/fixture-reminder.sh)) |
| `Edit/Write` on a strategy / severity-formula file | Stdout reminder to invoke `/statistician` for formula correctness | Harness (PostToolUse hook) |
| `Edit/Write` on `app/registry.py` or `infra/registry.yaml` | Stdout reminder to invoke `/trader` + `/risk-officer` | Harness (PostToolUse hook) |
| New file under any `doc/adr/` | Stdout reminder to invoke `/chief-architect` for ADR review | Harness (PostToolUse hook) |
| New entry in any `LIMITATIONS.md` | Stdout reminder to invoke `/adversary` to challenge gap acceptance | Harness (PostToolUse hook) |
| Agent attempts to terminate a turn | Bounded self-correction loop with convergence detection (full pytest, classification component) | Harness (Stop hook; see [`.claude/hooks/stop-pytest.sh`](../../.claude/hooks/stop-pytest.sh) → [`harness/steer.sh`](../../harness/steer.sh) → [`harness/check-suite.sh`](../../harness/check-suite.sh)) |
| Oracle red (test runner, type checker, fitness rule) | Steering-loop reasoning protocol (Case A) | Agent reads output; agent classifies per AGENTS.md convention |
| Bug observed despite all oracles green (Case B) | Operator or specialist raises issue; ADR or LIMITATIONS entry | External — see ceiling above |
| Pre-commit | Operator reads agent output and final diff | Operator |

### Steering loop — two entry points

**Case A — oracle red.** An oracle reports a failure. The agent
classifies:

- Common. The failing test correctly states the requirement and the
  implementation is wrong → fix the implementation. No ADR.
- Rare. The failing test itself encodes a wrong assumption (a
  fixture band copied from broken implementation output rather than
  derived from the SRS) → fix the test as a *harness bug*, not a
  service bug. No ADR if the existing oracle class still applies.

**Case B — oracle escape.** All oracles green; the bug exists anyway.
Not visible in any oracle output, so it can only be entered when a
non-oracle observer raises the issue:

- A specialist skill review (`/trader`, `/statistician`, `/architect`,
  `/adversary`) spots a wrong fixture band, a wrong-level abstraction,
  an axis the oracles don't probe.
- A reality-derived oracle, when built and within its target
  sub-class, flags a calibration drift.
- Code review by the operator notices something the agent missed
  (this is how classification ADR-0001 was discovered).
- Production observation (someone sees a wrong score in the field).

The Case B response is operator-and-agent together, after the issue
is raised:

- Open an ADR and add a new control (test, fitness rule, fixture,
  new oracle layer) in the same PR. The ADR names which oracle class
  catches the failure mode going forward.
- Or, if no control is feasible, add a `LIMITATIONS.md` entry with
  failure mode, rationale, and the conditions that would re-open the
  question.

A bug alone does not warrant an ADR. A bug plus a new control does.
A bug plus an accepted gap is a `LIMITATIONS.md` entry. **Silent
gaps are not a permitted state.**

The agent looking only at green oracle output cannot autonomously
detect Case B. This is the ceiling named in Layer 4.

### Concrete hook implementation

The hooks the architecture committed to are wired across three components,
not as inline shell in `.claude/settings.json`:

- **Adapter layer** — `.claude/hooks/{check-py,fixture-reminder,stop-pytest}.sh`.
  Per-agent (Claude Code). Reads the runtime's hook payload, scope-filters,
  delegates to the harness, and wraps any output as the agent's protocol JSON.
  ~20 LOC each. Replacing the agent (Cursor, Codex, Copilot) is one new
  adapter set; the rest of the harness is unchanged.
- **Steering manager** — [`harness/steer.sh`](../../harness/steer.sh). Agent-
  agnostic. Owns per-turn state at `.harness-state/<session>.json`. Implements
  a bounded self-correction loop with convergence detection: at most one
  block-on-red per turn, with retry across attempts gated on whether the
  failing-test fingerprint changed. Termination guaranteed across `green` /
  `skip:no_edits` / `escalate:stuck` / `escalate:budget_exhausted`. Contract:
  [`harness/STEERING.md`](../../harness/STEERING.md).
- **Oracle scripts** — [`harness/check-suite.sh`](../../harness/check-suite.sh)
  (full pytest with `--changed-only`), [`harness/check-py.sh`](../../harness/check-py.sh)
  (ruff + mypy on a single file), [`harness/fixture-reminder.sh`](../../harness/fixture-reminder.sh)
  (advisory text). Pure functions of code state — no JSON, no agent
  awareness, no side effects. Contract:
  [`harness/ORACLE.md`](../../harness/ORACLE.md).

`.claude/settings.json` wires PostToolUse to the two adapter scripts
(`check-py.sh`, `fixture-reminder.sh`) and Stop to `stop-pytest.sh`. See the
trigger-map row entries above for the exact mapping.

## Per-component instantiation rule

For each PrimeScore AI component, the harness MUST manifest as:

- `AGENTS.md` (Layer 1)
- `LIMITATIONS.md` (Layer 5)
- `HARNESS.md` (Layer 5 — the runbook inventory across all five
  layers as instantiated at that component)
- `doc/adr/` (Layer 5)
- Component-specific feedback infrastructure satisfying the universal
  floor in Layer 4 (contract + acceptance + structural-fitness +
  operational gate).

Skills (Layer 2) and permissions (Layer 3) are project-wide; they
extend per-component via `.claude/settings.local.json` and convention.

## Trade-offs

- **Five layers vs simpler three.** Sacrificed: simplicity. Gained:
  each layer has a named owner-failure-mode pair, and "the harness"
  stops being an ambiguous word.
- **Per-component ADRs vs one big harness ADR.** Sacrificed: single
  source for component-level harness decisions. Gained: component
  ADRs evolve without superseding the project-wide one. Iteration
  churn is contained.
- **Skills as operator-invoked-with-reminders, not agent-autonomous.**
  Sacrificed: forced rigor (the skill always runs). Gained: realism;
  skills are judgment calls, not mechanical checks. Convention
  surfaces the recommendation, the operator chooses. Forcing every
  fixture edit through `/trader` would create alarm fatigue and erode
  the signal.
- **No CI gate.** Sacrificed: regression safety net at the network
  boundary. Gained: faster iteration without infrastructure overhead.

## Out of scope

- **Multi-agent orchestration** (parallel agents collaborating on
  one PR). Not used; not part of the harness.
- **Agent self-modification of the harness** (an agent editing its
  own AGENTS.md or hooks). ACX-001 forecloses this without operator
  review.
- **Domain-specific harness components for the LLM-judged
  GEOPOLITICAL strategy** (RAG store, prompt versioning). Their
  oracle layer is fundamentally different and warrants a future
  component ADR.
- **AI-judged oracles.** Layer A and other reality-derived oracles
  in this architecture are statistical, not LLM-based. Using an LLM
  to judge correctness would just be another oracle subject to its
  own correlated-error pathology.
- **Coverage-threshold gating.** Coverage is a lagging metric, not a
  fitness function.

## Relationship to other artefacts

- **Component-specific ADRs** (e.g. classification's
  [`0003-test-oracle-architecture.md`](../../apps/classification/doc/adr/0003-test-oracle-architecture.md))
  instantiate Layer 4 for that component. They do not re-decide the
  harness shape — only how that component implements its oracles.
- **`HARNESS.md` per component** is the regenerable inventory of
  every layer at that component. It changes as the inventory
  changes; it does not require an ADR.
- **This ADR** locks the project-wide architecture. Component ADRs
  lock component instantiations. Changing the architecture →
  superseding project ADR. Changing a component's instantiation →
  component ADR. Changing current state → `HARNESS.md` edit, no ADR.

## References

- [`AGENTS.md`](../../AGENTS.md) — project root context
- [`.claude/skills/`](../../.claude/skills/) — Layer 2 cognitive tools
- [`.claude/settings.json`](../../.claude/settings.json) +
  [`settings.local.json`](../../.claude/settings.local.json) — Layer 3
  permissions
- [SRS](../srs/PrimeScore-SRS.md) — ACX-001, ACX-002, EVO-001 (controlled-
  variable framing)
- [Classification ADR-0001](../../apps/classification/doc/adr/0001-per-indicator-tuning-parameters.md)
  — wrong-level abstraction postmortem
- [Classification ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md)
  — self-validating-loop postmortem and ECDF + Layer A commitment
- [Classification ADR-0003](../../apps/classification/doc/adr/0003-test-oracle-architecture.md)
  — Layer 4 instantiation at the classification service
- ADR format: Michael Nygard, *Documenting Architecture Decisions*
  (2011)
- Anthropic, *Building Effective Agents* (2024)
- Ford / Parsons / Kua, *Building Evolutionary Architectures* (2017)
- Federal Reserve, *SR 11-7: Guidance on Model Risk Management*
  (2011) — independent validation principle
