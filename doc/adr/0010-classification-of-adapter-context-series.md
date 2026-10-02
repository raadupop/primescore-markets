# ADR-0010: Classification of adapter-recorded series

**Status:** Accepted

**Date:** 2026-09-29
**Deciders:** Radu Pop (operator); v3 coding agent

## Context

Every recorded signal is sent to the classifier. A market-data symbol outside the indicator
registry (`infra/registry.yaml`) comes back as a degraded zero-score assessment that is recorded as
available; API callers rely on that behaviour (SRS CLS-009). The Cboe adapter (ADR-0009) records
context series that are not ranked indicators (VIX9D, VIX3M, VIX6M, SPX), records the same closes
FRED recorded for VIX, VXN, RVX, OVX and GVZ on dates up to 2026-09-28, and records history back to
1990 for VIX, partly back-calculated by Cboe.

A composite counts an assessment as confirmed when another market-data assessment exists in its
window, so a second assessment of the same close would corroborate the first and add a decision.
The history classified so far starts in 2011. The research protocol (design §6) forbids using data
for a verdict before a registration names it; history classified and shown in the outcomes record
is history spent. The registry drives the Python classifier's ranking and bootstrap.

## Decision

A signal is **adapter-recorded** when its source identifier carries a reserved adapter prefix
(ADR-0009) and its provenance provider is not `api` (case-insensitive). API submissions keep their
behaviour, including rows submitted with a prefix before it was reserved.

A structured adapter-recorded signal passes three rules in order. The first that applies is
recorded as a final `AssessmentUnavailable` without calling the classifier:

1. Its New York date is before `Classification:AdapterHistoryFrom` (default 2011-01-01):
   `OutsideClassifiedHistory`. A value not in `yyyy-MM-dd` form stops start-up, so history meant to
   stay out is never classified by accident.
2. It is market data whose instrument is not in the indicator registry: `UnknownIndicator`.
3. Its series (instrument, category and variant; a macro series by indicator alone, since its
   variant names the reference period) already has an observation recorded earlier in the ledger
   for the same New York date: `DuplicateObservation`, naming the signal of record. Geopolitical
   signals are exempt.

These local outcomes are recorded even while the classifier circuit breaker is open, and they
neither count as classifier failures nor reset the failure count.

Rows before `AdapterHistoryFrom` are recorded in full and feed reference windows, which read
observation series, not assessments. Moving the date earlier requires a research registration
first, then an operator configuration change.

The indicator registry and the Python classifier are unchanged. VIX9D, VIX3M, VIX6M and SPX are
analytics inputs (term-structure ratios, cross-asset context), not ranked indicators.

## Consequences

- No assessment, composite or decision arises from pre-2011 adapter rows, unregistered context
  series or same-date duplicates; one close is assessed once and cannot corroborate itself.
- Existing `fred:` rows count as adapter-recorded, so a still-pending FRED row becomes
  `DuplicateObservation` when an earlier-recorded row exists for its date.
- Recorded assessments are never recomputed, so stored replays and validation runs are unchanged
  (ADR-0006). Computations that read observation series on demand see a longer history before 2016
  once Cboe history is recorded: new classifications' reference windows, composites' regime
  percentiles, the outcomes record, and replays or validation runs captured after the backfill,
  which can therefore differ from runs over the same dates captured before it.
- Two unavailability reasons are added and stored by name; the API contract does not enumerate
  them.

## References

ADR-0004, ADR-0006, ADR-0008, ADR-0009. SRS CLS-004, CLS-009. `doc/briefs/markets-v3-design.md` §6.
