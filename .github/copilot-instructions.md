<!--
  Pointer file — canonical agent context is AGENTS.md at the repo root.
  GitHub Copilot automatically reads this path for repo-wide instructions.
  Do not add content here; edit AGENTS.md instead so every agent
  (Claude Code, Codex, Cursor, Copilot) sees the same guidance.
-->

# PrimeScore AI — Agent Instructions

The canonical project context lives in [`AGENTS.md`](../AGENTS.md) at the
repository root. Service-local context for the Python classification service
lives in [`apps/classification/AGENTS.md`](../apps/classification/AGENTS.md).

Read those files first. They cover:

- Repository structure and the six architecture iterations
- Document hierarchy (SRS at `doc/srs/PrimeScore-SRS.md`, `PrimeScore-API-v1.yaml`, per-component `AGENTS.md`)
- Contract-first convention — `PrimeScore-API-v1.yaml` is the single source of truth
  for the .NET external API; `apps/classification/doc/openapi.yaml` is the
  contract for the Python classification service
- Black-box acceptance test discipline (EVO-001)
- Agent context is a controlled variable across iterations (ACX-001, ACX-002)

## For Copilot specifically

- Follow the conventions in `AGENTS.md`. Do not introduce patterns that
  conflict with them without explicit human approval.
- Prefer editing existing files over creating new ones.
- The Python classification service (`apps/classification/`) is constant
  infrastructure across all six .NET iterations. Changes there must not
  affect architecture measurements.
- Acceptance and architecture tests under `apps/classification/tests/` are
  fitness functions, not feature tests. Do not add bug-shaped tests; enable
  rule families in `pyproject.toml` instead.
