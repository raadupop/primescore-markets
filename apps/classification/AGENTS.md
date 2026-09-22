# PrimeScore.Classification

Python HTTP classifier. The standalone service owns `POST /classify` and `GET /health`; the planned .NET engine will call it over HTTP. This component is constant across the six planned architecture iterations.

## Sources

- [OpenAPI](doc/openapi.yaml): request/response contract.
- [SRS](../../doc/srs/PrimeScore-SRS.md): CLS-001, CLS-003, CLS-009 and downstream requirements.
- [Harness](HARNESS.md): executable checks, hooks and known coverage gaps.
- [ADRs](doc/adr/) and [limitations](LIMITATIONS.md): decisions and statistical/operational limits.
- [Registry](../../infra/registry.yaml): indicator classes and bootstrap providers.
- [Naming](../../doc/conventions/python-naming.md): new names describe their output.

## Boundaries

`MARKET_DATA` and `MACROECONOMIC` structured routes execute. Cross-asset and both geopolitical routes are stubs returning HTTP 501. LLM calls and RAG retrieval are planned; installed dependencies do not constitute an integration.

The classifier returns signed severity, certainty dimensions and reasoning. Aggregation, independent forecasts, dislocation detection, storage, portfolio decisions and positions belong to the planned engine. The local demo adapter computes an explicitly simplified scenario outside that engine.

History lives in memory and the registry loads once at startup. FRED bootstrap is partial; replay uses explicitly seeded prior observations. Keep online provider access separate from deterministic acceptance tests. Readiness must reflect supplied data, not merely completion of startup.

## Validation

Run the repository gate (`bash harness/check-suite.sh`) with the intended Python environment active before claiming code complete. Acceptance assertions use public HTTP responses; fixture setup may inject state. Do not derive expected bands from implementation output or alter source observations to make tests pass.

When the fixture hook requests `/trader` and `/statistician`, surface that reminder to the operator. Follow the root [agent runtime rules](../../AGENTS.md) for Case A failures and Case B oracle escapes.
