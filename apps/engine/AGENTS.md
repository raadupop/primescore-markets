# PrimeScore Markets engine

.NET 10 modular monolith over an append-only, hash-chained ledger ([ADR-0003](../../doc/adr/0003-engine-modular-monolith-over-hash-chained-ledger.md)). One process hosts the API ([contract](../../doc/PrimeScore-API-v1.yaml)), the Blazor UI and the schedulers. Runs in simulation mode only.

## Layout

| Path | Role |
| --- | --- |
| `src/Api.Contracts` | DTOs, abstract controllers and typed client, regenerated from the contract on every build into `obj/`. `SignalInput` is hand-written: the generator collapses its `oneOf` payload. |
| `src/SharedKernel` | Clock, ids, canonical JSON, CQRS and event interfaces, registry reader (`infra/registry.yaml`). |
| `src/Ledger` | Ledger, projections, verification, pipeline log; owns the SQLite file. |
| `src/Modules/<Area>` | Implementation (only `<Area>Module` is public) and `.Contracts`. Positions, Exits, Risk: contracts only. |
| `src/Host` | Composition root, API controllers, UI, auth, CLI (`verify-ledger`, `new-api-token`). |
| `tests/PrimeScore.Structural` | Brief §5 dependency rules, one test per rule. |
| `tests/PrimeScore.Acceptance.Api` | Black-box HTTP suite (EVO-001a) against real engine and classifier processes. |

## Rules

- A red structural rule is an architectural violation. Fix the code, not the rule.
- Contract first: edit the YAML, rebuild, then implement. Contract changes beyond the build brief need operator approval.
- Acceptance tests use HTTP and generated contract types only. They never reference engine assemblies or read the database.
- Expected values in formula tests are calculated by hand from the SRS or ADR formula, never copied from program output.
- State changes go through `ILedger.AppendAsync`. Modules read their own projections, never the ledger table.
- Secrets live only in user-secrets or environment variables (SEC-002). Tokens are stored as SHA-256 hashes.

## Commands

From the repository root: `bash harness/check-suite.sh` is the single gate; `bash harness/check-engine.sh` runs the engine stage alone. Migrations: `dotnet ef migrations add <Name> --project <project> --startup-project <project>` from `apps/engine` (tool manifest in `.config/`). FRED key: `Fred:ApiKey` in user-secrets (Development) or `Fred__ApiKey`. Data limits: [LIMITATIONS.md](LIMITATIONS.md). Setup and operation: `docs/ENGINE.md` (milestone M6).
