# ToDo registry

Forward-work tracker for PrimeScore AI / DeltaFeed.

[`registry.yaml`](registry.yaml) holds the open tasks. Open work lives
here, not in ADRs (per [adr-discipline.md](../conventions/adr-discipline.md))
and not in `HARNESS.md` (which holds current-state inventory, not pending
work). This document explains the schema and the rules of use.

## Why a registry, not ADRs

Per [doc/conventions/adr-discipline.md](../conventions/adr-discipline.md),
ADRs are immutable records of architectural decisions. They MUST NOT contain
forward-work content (TODO markers, "not yet configured", sequencing plans,
"where the project stands"). Such content rots inside an immutable document
and confuses readers about what is decided versus what is pending.

The registry is where forward work lives. It is mutable by design.

## Schema

```yaml
schema_version: 1
todos:
  - id: TODO-NNN              # required, monotonically assigned
    title: <one-line title>   # required, imperative voice
    status: open              # required: open | in_progress | done | dropped
    priority: medium          # optional: high | medium | low
    owner: <name>             # optional, defaults to operator
    source:                   # required: where this TODO originated
      kind: <kind>            #   adr | srs | harness-inventory | research |
                              #   bug | design-followup | external
      ref: <reference>        #   ADR-NNNN | CLS-NNN | file path | URL
      section: <optional>     #   subsection within the ref, free text
    related_adr: [ADR-NNNN]   # optional list
    related_test: <pytest id> # optional
    created: YYYY-MM-DD       # required
    updated: YYYY-MM-DD       # required, bumped on any field change
    blocked_by: [TODO-NNN]    # optional list of dependency IDs
    notes: >                  # optional free text, multi-line
      Longer description, rationale, links to research notes, etc.
```

## Status lifecycle

- **open** — not started.
- **in_progress** — actively being worked.
- **done** — completed. Entry retained for audit; do not delete.
- **dropped** — explicitly decided not to do. Add a `notes:` line explaining
  why.

Done and dropped entries are retained for the project's lifetime — they are
the audit trail of what got built and what got declined. If the registry
grows uncomfortable, archive old entries to `registry-archive-YYYY.yaml`
rather than deleting.

## ID assignment

IDs are monotonically assigned (TODO-001, TODO-002, …) and are stable for
the life of the entry. Never reuse an ID, never renumber on archive.

When adding a new entry: read the highest existing `id`, increment by one.

## Source kinds

The `source.kind` field anchors the TODO to its provenance:

| Kind | Meaning | Example ref |
|---|---|---|
| `adr` | Comes from an ADR's deferred work | `ADR-0001`, section `Sequencing step 3` |
| `srs` | Comes from an SRS requirement | `CLS-001` |
| `harness-inventory` | Update to a HARNESS.md instantiation | `apps/classification/HARNESS.md` |
| `research` | Comes from a research note | `doc/research/<note>.md` |
| `bug` | A reproducible defect | issue ID or short description |
| `design-followup` | Optional follow-up to a design discussion | file path or chat reference |
| `external` | Comes from outside the repo (operator request, etc.) | free text |

If a TODO arises from an ADR's deferred work, the `related_adr` list MUST
include that ADR. This lets ADR readers find the live work without the ADR
itself having to track it.

## MCP integration (future)

The schema is intentionally structured so that an MCP server can serve it
to a task-management tool (Linear, Asana, GitHub Issues, etc.). The fields
map roughly:

| Registry field | Generic task tool field |
|---|---|
| `id` | external ID |
| `title` | title |
| `status` | status (with mapping table) |
| `priority` | priority |
| `owner` | assignee |
| `source.ref` | description prefix / link |
| `notes` | description body |
| `blocked_by` | blocked-by relations |
| `related_adr` | tags or linked items |

When the MCP integration is built, it will be a separate component; the
registry remains the source of truth.

## Editing rules

- **Add entries freely.** Open work belongs here, not in ADRs.
- **Update `updated:` on any field change.**
- **Never edit an entry that is `done` or `dropped`.** If you discover the
  decision was wrong, open a new TODO that supersedes (note in `notes:`).
- **Don't put implementation details in `notes:` that should be in a
  contract or HARNESS.md.** The TODO names *what* and *why*, not *how*.
- **Resolve `blocked_by` chains explicitly.** Don't let entries linger as
  blocked when the blocker is `done`.

## Skill

The [`/adr-author`](../../.claude/skills/adr-author/SKILL.md) skill knows
about this registry and routes ADR-disallowed forward-work content here.
