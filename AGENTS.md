# PrimeScore AI

PrimeScore AI is the venture brand. Its first product, PrimeScore Markets, will classify events and compare volatility estimates with observed implied volatility. This repository contains the Python classifier and its agent harness: instructions, tool hooks and code checks around coding-agent edits. The planned .NET application will aggregate classifications and evaluate positions; see [README.md](README.md) for implemented scope.

The architecture research programme formerly called DeltaFeed studies coding agents under deterministic constraints. Its six engine iterations are planned; there are no comparative results yet.

## Repository Structure

- `doc/` — SRS (Markdown-native, see `doc/srs/`), INVEX-API-v1.yaml, project-wide ADRs
- `apps/classification/` — Python classification service (constant across all iterations)
- `apps/demo/` — local historical replay dashboard; separate from the engine iterations
- `sites/` — static brand and product pages
- `docs/` — status, demonstration and deployment runbooks
- .NET iteration projects will be added starting at Iteration 1

## Document Hierarchy

| Document | Role |
| --- | --- |
| [SRS](doc/srs/INVEX-SRS.md) | Requirements — what the system must do |
| [INVEX-API-v1.yaml](doc/INVEX-API-v1.yaml) | External interface contract — message content, format, schemas |
| [doc/adr/](doc/adr/) | Project-wide architectural decisions. Start with [ADR-0001](doc/adr/0001-agent-harness-architecture.md) — the agent harness architecture (five layers — context, cognitive tools, permissions, feedback oracles, decision durability). |
| AGENTS.md (per component) | Agent context. `CLAUDE.md` exists at each level as a pointer stub so Claude Code's auto-discovery still resolves. |
| Per-component `doc/adr/` and `HARNESS.md` | Component-specific architectural decisions and the regenerable per-component harness inventory. |

The SRS references the OpenAPI spec, not the other way around. Component-specific ADRs (e.g. classification's [ADR-0003](apps/classification/doc/adr/0003-test-oracle-architecture.md)) instantiate the project-wide harness layers at each component; they do not re-decide the harness shape.

## Architecture Iterations

1. Transaction Script (baseline) — all Must requirements
2. Vertical Slice Architecture (refactor from 1)
3. Clean Architecture with Rich Domain Model (rewrite)
4. Clean Architecture + Event Sourcing (refactor from 3)
5. Modular Monolith (sourced from Iteration 3)
6. Service Extraction (Decision Engine)

## Python Classification Service

Constant external dependency across all six .NET iterations. Lives in `apps/classification/`. Has its own `AGENTS.md`. The planned .NET app will call `POST /classify` over HTTP. The Python service does not affect architecture measurements — it's constant infrastructure.

## Conventions

- Contract-first: INVEX-API-v1.yaml is the single source of truth for the .NET API
- API acceptance tests are strictly black-box (EVO-001)
- Structural tests are iteration-specific
- The agent context (this file + per-component `AGENTS.md`) is a controlled variable (ACX-001, ACX-002)
- Python naming: see [doc/conventions/python-naming.md](doc/conventions/python-naming.md). Functions and modules describe their *output*, not the discriminator that dispatched to them.
