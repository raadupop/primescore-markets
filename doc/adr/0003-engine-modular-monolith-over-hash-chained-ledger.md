# ADR-0003: Engine as a modular monolith over a hash-chained ledger

- **Status:** Proposed
- **Date:** 2026-09-25
- **Deciders:** Radu Pop (acceptance pending)
- **Relates to:** [ADR-0001](0001-agent-harness-architecture.md), [ADR-0002](0002-complete-repeatable-validation-gates.md)

## Context

PrimeScore Markets v1 must ingest daily observations, classify them through the
Python classifier, aggregate them per volatility context and record the deploy or
idle decision the rules would have made. Four requirements share one shape: signals
persist append-only with point-in-time queries ([SRS SIG-004](../srs/PrimeScore-SRS.md#sig-004-must)),
the decision trail is append-only and tamper-evident (AUD-001, AUD-002), replay
reproduces past decisions without look-ahead (ANA-001), and every computed record
names the configuration version it used (NFR-003). Each is a query over an ordered
stream of facts: observations, assessments, composites, dislocations, decisions.

[SRS §7](../srs/PrimeScore-SRS.md#7-iteration-plan) plans six engine iterations for
the architecture study formerly called DeltaFeed. v1 is a product, not a study run.
The forces that apply to it:

- Aggregation formulas, windows, thresholds and regimes are configuration and will
  change; replay must override them without touching stored versions.
- Coding agents write most of the code. SRS §7.5 predicts that coarse module
  boundaries enforced by project references leak less than Clean Architecture layer
  rules, which §7.3 predicts are fragile under agent edits.
- Positions, exits and risk (Milestone B) need an options price source that does not
  exist yet; they must attach later without changes to the intelligence modules.
- One operator runs everything; a second deployable or a message broker adds
  operations without a matching need.

Alternatives weighed: Transaction Script degrades as configurable formulas and replay
semantics multiply. Vertical Slice alone repeats ledger, correlation and
point-in-time handling per slice, the duplication SRS CT-03 predicts. Clean
Architecture across the whole application adds layers seven modules do not need.
Event sourcing of every aggregate adds ceremony to configuration and positions
without a replay requirement for them. Service extraction addresses team and scaling
limits this project does not have.

## Decision

The engine is one deployable process hosting the HTTP API, the web UI and the
schedulers. It is a modular monolith in the SRS Iteration 5 shape: the seven SRS §4
functional areas are modules; each implementation exposes only its registration
entry point; modules communicate only through contracts assemblies that reference
nothing but the shared kernel. Positions, exits and risk exist as contracts only;
their endpoints answer 501 with the reason until Milestone B.

State lives in an append-only ledger in the engine's single embedded database (for
this component: one SQLite file in write-ahead-log mode). Every read model is a
projection that each module maintains synchronously inside the ledger append's
transaction; each projected row carries the ledger sequence that produced it.
Modules never read the ledger itself. Commands return acknowledgements only;
queries never write.

Storage rejects updates and deletes of ledger entries. Each entry's SHA-256 covers
the previous entry's hash and every stored field, not only the kind and payload, so
editing any field breaks verification. A verification command recomputes the chain
end to end and also checks that the head it last verified still exists unchanged.

These dependency rules are executable: the engine's structural test suite encodes
them one test per rule and fails the shared gate
([ADR-0002](0002-complete-repeatable-validation-gates.md)). The HTTP surface is
generated from the [external contract](../PrimeScore-API-v1.yaml); the black-box
acceptance suite compiles against that generated contract only.

The architecture study is deferred, not abandoned. The v1 acceptance suite becomes
the study's constant suite (EVO-001a). Study iterations will live in their own
directory or branch, with agent context restricted per ACX-001 so that v1's code is
not visible to them.

## Consequences

Point-in-time queries, audit, replay and log correlation use one mechanism: an
ordered, hashed entry and the projections derived from it. Every projected row can
be traced to the entry, configuration version and correlation identifier behind it.
Milestone B modules attach by consuming published decision contracts.

Appends are serialized through one writer. That suffices at daily cadence; intraday
feeds would require re-evaluating the write path. The ledger grows with every
observation and has no compaction.

Tamper evidence is detection within the file, bounded by the last verification.
Edits, removals and reordering at or below the head recorded by the last
verification are detected. Entries appended after it can be truncated, or
rewritten with recomputed hashes, without detection; so can a whole chain rewritten
before any verification ran. Verifying regularly narrows that window; anchoring
chain heads outside the database is not part of this decision.

v1 is not a study data point. It is built with full-repository agent context, so
its development history does not enter the six-iteration comparison.

## Trade-offs

- **Synchronous projections** give read-after-write consistency without
  reconciliation and lengthen each write transaction. Accepted because appends are
  small and the cadence is daily.
- **Contracts-only module surfaces** force the host to map module contracts to API
  types, duplicating shapes. Accepted because the mapping keeps API types out of
  modules and makes the boundary checkable.
- **One process** gives up independent scaling and fault isolation of the decision
  logic, which SRS Iteration 6 exists to study.

## References

- [SRS v2.3.3](../srs/PrimeScore-SRS.md): §4, §7.4, §7.5, SIG-004, AUD-001, AUD-002, ANA-001, NFR-003, EVO-001, ACX-001
- [Engine API contract](../PrimeScore-API-v1.yaml)
- [ADR-0001](0001-agent-harness-architecture.md): harness layers; [ADR-0002](0002-complete-repeatable-validation-gates.md): gate completeness
- Engine component context: [apps/engine/AGENTS.md](../../apps/engine/AGENTS.md)
