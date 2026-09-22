# ADR-0002: Require complete, repeatable validation at engineering entry points

- **Status:** Proposed
- **Date:** 2026-09-22
- **Deciders:** Radu Pop (acceptance pending)
- **Supersedes:** [ADR-0001](0001-agent-harness-architecture.md), oracle-script purity guarantee only
- **Relates to:** [Classification ADR-0003](../../apps/classification/doc/adr/0003-test-oracle-architecture.md)

## Context

An audit found an engine schema requiring an undefined property outside the
suite's validation scope. Declared dependency boundaries were not executed;
missing fitness tools could skip their checks. A fitness wrapper treated empty
diagnostics as no violations even when its tool exited with a configuration
error. Change detection ignored new
files and edits outside the classification component. An agent-runtime re-entry
flag could bypass validation after a blocked Stop.

The classification test process also loaded provider credentials from local
configuration and performed live bootstrap. Test-runner timing and ignored
caches contradicted the oracle's promised identical output and absence of
side effects. A successful invocation could therefore conceal omitted controls
or depend on information beyond the intended input snapshot.

## Decision

A successful engineering gate means every declared mandatory control ran and
passed under its stated input conditions. Local commands, agent Stop hooks and
CI share the same gate. Missing dependencies and invalid contracts fail it.
Tool wrappers require successful process exit; empty diagnostics alone cannot
establish success.
Expected skips identify their excluded behavior in the report; they do not
establish that behavior's validity. Change detection includes staged, unstaged
and untracked nonignored repository inputs.

The gate isolates normal checks from provider credentials, live bootstrap,
operator-specific runtime settings and external test-selection flags.
Credential-dependent integration checks require
separate explicit invocation. Repeatability applies to validation verdicts for
the same code, dependencies and supported runtime. Diagnostic timing and ignored
tool caches may vary; tracked source files and steering state remain outside
the oracle's write authority. The interface is defined in
[the validation contract](../../harness/ORACLE.md).

Stop re-entry invokes the gate. Failed test identifiers and named control stages
feed the bounded loop in [the steering contract](../../harness/STEERING.md).
Escalation reports unresolved validation and cannot be represented as a pass.
Regression controls exercise omitted dependencies, change detection, provider
isolation, failed tool configuration and the real steering interface against
a fixed test oracle.

## Consequences

Gate success becomes evidence that declared controls executed. It does not
establish coverage of unimplemented behavior, predictive calibration or market
outcomes. Live-provider reliability requires separate evidence.

Every nonignored checkout edit can trigger validation, including documentation
edits. The gate needs the complete development toolchain. Cross-platform runs
can expose differences in the supported runtime rather than suppressing them.

## Trade-offs

Broader invocation and dependency preflight add local execution cost. This cost
is accepted because an omitted check can otherwise appear indistinguishable
from a passing check. Allowing ignored caches and timing variation gives up
byte-identical diagnostics while preserving the pass/fail evidence the steering
loop consumes.

## References

- [ADR-0001: agent harness architecture](0001-agent-harness-architecture.md)
- [Validation contract](../../harness/ORACLE.md)
- [Steering contract](../../harness/STEERING.md)
- [Harness regression controls](../../tests/harness/)
- [Component harness inventory](../../apps/classification/HARNESS.md)
