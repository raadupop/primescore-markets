# ADR-0005: Caller-supplied reference window

**Status:** Proposed
**Date:** 2026-09-25
**Deciders:** Radu Pop (acceptance pending)

## Context

The classifier ranks each observation against a per-symbol history held in process
memory. Each classification appends to that history, and a restart discards it
([session note, 2026-04-26](../../../../doc/session-notes/2026-04-26-classifier-architecture-three-questions.md)).
Point-in-time replay (SRS ANA-001) needs, for every past signal, the history as it
stood at that signal's observation time. Only the engine's ledger holds that
history; the process window reflects whatever was classified last. The replay demo
works around this by clearing and reseeding the process window before each request,
which serializes callers and couples them to process internals.

## Decision

`POST /classify` accepts an optional `reference_window`: the values the process
window would hold for the symbol (prior levels for MARKET_DATA, prior surprise
magnitudes for MACROECONOMIC) and the time of the newest one. When present, the
strategy computes on a transient copy of that window with the same formulas,
length limit and guards; process windows are neither read nor changed. Such a
request is served while the service is still bootstrapping, because readiness
describes the process windows it does not use. Without `reference_window`,
behaviour is unchanged.

Unknown symbols keep the CLS-009 response with or without a window. The contract
records the field in [openapi.yaml](../openapi.yaml) 0.2.0.

## Consequences

The engine owns history and passes it with each request, so replay is point in
time and concurrent callers no longer interfere through shared state. Acceptance
controls show that a supplied window reproduces every market anchor response byte
for byte, that process state is unchanged afterwards, and that a request without a
window still reads and extends process state.

Requests grow by up to `N_L` numbers (1,260 for daily vol indices). The classifier
cannot check that a supplied history is genuine or point in time; that is the
caller's responsibility.

## Trade-offs

- **Two sources of history** (process windows and caller windows) remain until
  the process windows are retired. Accepted to keep the standalone service and the
  demo working unchanged.
- **Larger requests** in exchange for stateless, replayable classification; at
  daily cadence the payload size is immaterial.

## References

- [ADR-0002](0002-ecdf-severity-and-backtest-harness.md): ECDF severity and the registry.
- [ADR-0004](0004-signed-severity-and-fit-rejection.md): signed severity and fit rejection.
- [Engine ADR-0003](../../../../doc/adr/0003-engine-modular-monolith-over-hash-chained-ledger.md): the engine ledger that supplies the windows.
