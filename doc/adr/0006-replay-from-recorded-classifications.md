# ADR-0006: Replay from recorded classifications

**Status:** Proposed  
**Date:** 2026-09-25  
**Deciders:** Radu Pop (review pending)

## Context

ANA-001 requires historical decisions under temporary parameters. Classification stores the
score, certainty and source observation; configuration controls the composite and decision,
not the classifier's statistical formulas. A replay must not add simulated decisions to the
live decision history or change the configuration used by ingestion.

## Decision

Capture the ledger sequence and hash before each run. Read only observations and assessments
recorded at or before that sequence, and use the configuration version recorded by then.
For each eligible assessment in the requested range, recalculate the composite, dislocation
and decision using observations at or before its observation time. Earlier observations supply
the reference history and corroboration window; later observations never enter the calculation.

Reuse the recorded classifications, including their unavailability and fallback states.
Replay does not call the classifier again or discover newly supplied consensus. Temporary
overrides pass the configuration validator and are recorded with the complete effective
settings. An overridden run carries the calibration label `in-sample`.

Append one `ReplayRun` containing the input boundary, settings and decision timeline. Replay
identifiers and aggregates belong to that record, not to the live decision projection.
The Analytics module owns its projection; computation remains inside Classification and
Decision behind their published query contracts. Canonical settings snapshots cross those
contracts without introducing references between contract assemblies.

## Consequences

The same statistical classifications can be compared under different parameters without
changing their history. A failed or unsupported classification remains missing during replay.
Results describe the recorded data and classifier version; they cannot establish what an
unrecorded source, a different classifier or revised consensus would have produced.

Validation evaluates the equity/VIX proxy against the SRS target dates. Unsupported
geopolitical and macro routes are disclosed beside the result. Matching a target does not
measure returns, false-positive rates or independent predictive performance.

## References

- [ANA-001 and validation set](../srs/PrimeScore-SRS.md)
- [Ledger and module boundaries](0003-engine-modular-monolith-over-hash-chained-ledger.md)
- [Composite formulas](0004-composite-and-dislocation-resolutions.md)
- [Decision time](0005-decisions-conditions-explanations-and-time.md)
