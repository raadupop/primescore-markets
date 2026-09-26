# ADR discipline

Convention for writing and maintaining Architecture Decision Records under
`doc/adr/` (project-wide) and `apps/*/doc/adr/` (per-component).

ADR format follows Michael Nygard, *Documenting Architecture Decisions* (2011).
This document adds project-specific discipline.

## Core property: ADRs are immutable

An ADR records a decision at a point in time. Once **Accepted**, the body of
an ADR is treated as immutable. Permitted edits are limited to:

- Typos, broken links, formatting fixes.
- **Status transitions only** — Accepted → Deprecated → Superseded.
- Adding a back-reference to a superseding ADR (one line under "Status").

If the decision needs to change, write a **new ADR that supersedes it**. Do
not mutate the original. The git history of `doc/adr/` is the audit trail of
how the architecture evolved; in-place edits destroy that trail.

## What ADRs MUST NOT contain

These cause ADR rot. They belong elsewhere — see "Where mutable content
lives" below.

- **"Where the project stands today" / current-state notes.** The world
  changes; the ADR doesn't. Current state goes in `HARNESS.md`.
- **"Not yet configured" / "to be added" / "TODO" markers.** Forward work
  goes in [doc/todo/registry.yaml](../todo/registry.yaml).
- **Sequencing or work plans.** "Step 1: do X. Step 2: do Y." Work plans
  belong in the ToDo registry or in PR descriptions.
- **Quoted snippets of code, config, or JSON.** Snippets drift from the
  source they were copied from. Reference *concepts* and link to the file by
  path, not by content. If a code shape genuinely needs to be locked, lock
  it in a contract file (e.g. `harness/STEERING.md`) and reference the
  contract.
- **Concrete implementation file paths beyond stable, contractual ones.**
  Files move. Reference contracts (`harness/ORACLE.md`), package paths
  (`harness/`), or component roots (`apps/classification/`). Avoid line
  numbers. Avoid pointers into PR-volatile script bodies.
- **Progress markers** ("DONE", "in progress"). Use the registry status field.

## What ADRs MUST contain

- **Status** — Proposed | Accepted | Deprecated | Superseded by ADR-NNN.
- **Date** the decision was made.
- **Deciders** — operator(s) who locked it.
- **Context** — the *forces* at play (constraints, trade-offs already in
  evidence, demonstrated failure modes). Not what's happening this week.
- **Decision** — what was decided, in declarative voice. The lock.
- **Consequences** — what this enables, what it gives up. Both sides.
- **Trade-offs** (optional but recommended) — explicit named sacrifices and
  why each is acceptable.
- **References** — other ADRs by number, source documents, external prior art.

## Where mutable content lives

| Mutable thing | Lives in |
|---|---|
| Current state, "today the harness has X wired" | `HARNESS.md` (per component, regenerable inventory) |
| Forward work, "we should also do Y" | [doc/todo/registry.yaml](../todo/registry.yaml) |
| Implementation specifics that may change | Component README, contract docs (e.g. `harness/STEERING.md`) |
| Decision rationale that depends on volatile facts | Use a research note under `doc/research/` and link to it from the ADR |
| Open questions during decision-making | `doc/research/<adr-id>-open-questions.md` (resolved before the ADR is Accepted) |

## ADR lifecycle

1. **Proposed.** New ADR, status: Proposed. Open question; review wanted.
   Mutable while in this state.
2. **Accepted.** Operator commits. Status: Accepted. **Body becomes
   immutable** from this point forward. Date stamped.
3. **Deprecated.** Decision is no longer in force but no replacement exists
   yet. Single-line annotation under Status: "Deprecated YYYY-MM-DD —
   <one-line reason>." Body still immutable.
4. **Superseded.** New ADR replaces this one. Single-line annotation under
   Status: "Superseded by ADR-NNN on YYYY-MM-DD." Body still immutable.

A bug or missed nuance discovered after Accepted is *not* a license to edit.
Either the decision still stands and the limitation is captured in
`LIMITATIONS.md` per component, or it doesn't and a new superseding ADR is
written.

## Prose conventions

ADRs are read by people who do not know the system. The Context and
Consequences sections in particular are the entry points for that reader.
The conventions below are not style preferences — they determine whether
the ADR is informative or noise.

- **Cold-reader test.** Every claim must be groundable for a fresh reader
  who lacks project context. Translate jargon shortcuts on first use: not
  "per-strategy module-level constants", but "constants like `_TANH_SCALE`
  declared once per strategy module".
- **Name what happened.** Prefer mechanism over summary. "Agent encoded
  the same wrong assumption" is summary. "Agent ran the implementation,
  captured the output, and stored that output as the test's expected
  band" is what happened. Use the latter.
- **No filler observations.** Do not note that two things have different
  surfaces, that something is interesting, or that a property is true.
  Either the sentence carries information (a name, a number, a mechanism)
  or it does not belong.
- **No exhaustiveness claims.** When listing causes or trade-offs, say
  "what we caught" or "what we identified", not "the causes" or "the
  trade-offs". The list is empirical, not a proof.
- **Definitions over slogans.** When defining a term, give the operational
  form (what it does, in mechanism). "The architectural answer", "the
  cornerstone", "the foundation" are slogans, not definitions. Canonical
  form survives re-reading.
- **Project vocabulary only.** Do not import generic software-engineering
  terms ("PR review", "stakeholder sign-off", "production rollout") if
  the project's workflow does not use them. Use the project's actual
  workflow vocabulary or describe the action concretely.
- **Tooling-agnostic at the scope of the ADR.** A project-wide ADR
  must phrase rules in terms of *roles* (oracle, test runner, type
  checker, fitness rule, contract gate), not specific tools. "pytest",
  "xUnit", "mypy", "ruff" are acceptable only inside rows or
  paragraphs explicitly scoped to a component that uses them. A rule
  that bakes in one component's stack will misfire when a second
  component with a different stack arrives.
- **The filesystem is the roster.** Do not enumerate items that
  already live as files or directory entries — skill names under
  `.claude/skills/`, hook scripts, settings files, registry IDs. Link
  to the directory or contract file; the inventory belongs there, not
  in the ADR. Restating it creates two sources to drift apart.
- **Stay in lane per section.** Context describes forces. Decision locks
  the response. Consequences flow from the decision. Trade-offs name what
  was sacrificed. Do not put prescriptions in Context, do not put forces
  in Decision, do not put work plans in Consequences.
- **Concision.** An ADR records a decision, not its narrative. Cut any
  sentence that does not name a constraint, a mechanism, a number, or an
  interface. Reference other documents (component ADRs, contract files in
  `harness/`, the registry) by link rather than restating their content.
  If a section can be condensed to a pointer plus a one-line rule, do
  that. A reader who already knows the project should be able to review
  an ADR end-to-end in under five minutes.

## Anti-patterns

- **Editing in place because "it's just a small clarification."** No. If the
  clarification matters, it changes meaning; write a superseding ADR. If it
  doesn't matter, leave it.
- **Adding "Update YYYY-MM-DD: …" sections to old ADRs.** Same problem.
  Write a new ADR.
- **Folding multiple decisions into one ADR.** Each decision should be
  separately superseable. If you can't supersede one without disturbing
  another, you have two ADRs masquerading as one.
- **ADRs as work-tracking documents.** The "Sequencing" section is a
  recurring offender. If the decision implies work, that work goes in the
  registry; the ADR locks the decision, not the schedule.

## Skill

The [`/adr-author`](../../.claude/skills/adr-author/SKILL.md) skill
applies this convention during ADR drafting and review. It will refuse to
add disallowed content and will route forward work to the registry.