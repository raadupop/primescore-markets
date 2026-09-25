# ADR-0004: Composite and dislocation as implemented in Markets v1

- **Status:** Proposed
- **Date:** 2026-09-25
- **Deciders:** Radu Pop (acceptance pending)
- **Relates to:** [ADR-0003](0003-engine-modular-monolith-over-hash-chained-ledger.md), [SRS revision v2.3.4 proposal](../research/srs-revision-v2.3.4-proposal.md)

## Context

[SRS CLS-002](../srs/PrimeScore-SRS.md#cls-002-must) and [CLS-006](../srs/PrimeScore-SRS.md#cls-006-must)
as written in v2.3.3 cannot be implemented literally:

- A stale but present category discounts only the denominator, so the composite can leave
  `[−1, 1]` (a single category at conviction 0.9 with `d_c = 0.5` yields 1.8).
- Selecting the one assessment with the largest `|severity × certainty|` cannot let two
  opposing signals cancel, which the CLS-002 verification requires.
- "Expected" categories are named but never defined per context, and the corroboration
  windows (5 s intraday, 1800 s macro) have no value for daily closes, which are the only
  market data v1 has.
- A sensitivity factor above 1 with a composite near −1 makes the signal-implied IV
  negative, and "current volatility level" has no stated measure.

The engine records daily observations from 2011 onward in two contexts: equity (reference
VIX) and oil (reference OVX). Every parameter below is configuration: versioned, seeded
labelled "uncalibrated default", and overridable in replay (NFR-003, ANA-001).

## Decision

1. **Composite range.** `CompositeScore = Σ_{c∈P} w_c · d_c(Δt_c) · s_c / Σ_{c∈P} w_c`, where
   `P` is the context's expected categories with at least one confirmed assessment. The
   discount multiplies the category's conviction instead of dividing the total, so the
   range is `[−1, 1]` by construction and staleness only reduces magnitude.
2. **Net conviction.** `s_c = max⁺_c − max⁻_c`: the largest positive `severity × certainty`
   among the category's confirmed assessments minus the largest negative magnitude (each 0
   when none). Opposing near-equal signals cancel; weaker same-direction company does not
   dilute the strongest signal.
3. **Contexts and absence.** A context names its reference instrument, the categories it
   expects and the instruments that belong to each. Only expected categories and member
   instruments enter its composite. An expected category without a confirmed assessment is
   left out of both sums and listed as absent with its reason (no classified signal,
   awaiting consensus, classifier route not in v1).
4. **Windows at daily cadence.** A category's window is 2 trading days for market data and
   cross-asset flows (the evaluation day and the previous trading day, on the New York
   calendar) and 1800 s for macro releases. An assessment is confirmed when another
   assessment of the same category from a different signal is in the window, or when
   `|severity| ≥ p_bypass` (default 0.999). A CLS-004 fallback contributes but never
   confirms another assessment, since it repeats an earlier answer. An assessment with
   certainty 0 (unknown indicator, insufficient history) carries no conviction and
   neither contributes nor confirms. Assessments outside the window take no part;
   nothing is kept pending.
5. **Staleness.** `Δt_c` is the time by which the category's newest assessed observation is
   overdue against its reporting interval (1 trading day for daily categories, 1800 s for
   macro). `d_c = 1` while not overdue, then follows the SRS schedule (0.95 below 5 min,
   0.7 below 30 min, 0.5 beyond).
6. **Dislocation.** `SignalImpliedIV = IV × (1 + CompositeScore × k)` with `k ∈ (0, 1]`,
   enforced by configuration validation, so the signal-implied IV is never negative. The
   regime is the ECDF percentile of the reference level among its own previous `N_L`
   observations: low below 0.30, high above 0.70, otherwise normal; defaults
   `low = 1.0, normal = 0.75, high = 0.5`. When level boundaries are configured through the
   contract's `regime_boundaries`, the regime is read from the level instead. The
   threshold compares the dislocation's magnitude, so a vol-compression dislocation can
   breach it.
7. **Trigger.** A composite and its dislocation are recorded for every new assessment of a
   context member, at that signal's observation time and under its correlation id, in
   observation order. A later arrival for an earlier date adds a record at that date and
   leaves later records unchanged.

## Consequences

- Composite, dislocation and their inputs are reproducible from the ledger alone, and
  every record names the configuration version it used.
- The contract's static `source_dropout_penalty` is honoured as a flat schedule when a
  weighting scheme supplies it; the SRS example sensitivity map (1.5 / 1.0 / 0.6) is
  rejected as invalid.
- Macro prints contribute only within 30 minutes of release, so a daily decision computed
  at the close does not see the morning's print. This matches the SRS window and is
  visible in the absent-category reasons.
- Staleness never reaches a composite in normal daily operation (the window already
  bounds age); it bites when a source stops delivering while its last closes are still in
  the window.

## Trade-offs

- The percentile regime ignores absolute level; a quiet regime lasting years reads as
  "normal". Level boundaries remain available through configuration.
- The exponential form `IV × exp(CompositeScore × k)` avoids any bound on `k` and is noted
  as the calibration-era alternative; v1 keeps the SRS arithmetic.
- Treating the previous trading day's close as corroboration makes every single-instrument
  daily series self-confirming. The bypass matters only for the first close after a gap.

## References

- Build brief §9.1–9.7 (operator's working document).
- [SRS revision v2.3.4 proposal](../research/srs-revision-v2.3.4-proposal.md) collects these
  resolutions as proposed SRS text.
- `doc/session-notes/2026-04-13-sensitivity-factor-risk-analysis.md` for realistic
  dislocation sizes.
