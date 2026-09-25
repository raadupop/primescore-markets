# ADR-0005: Deploy and idle decisions as implemented in Markets v1

- **Status:** Proposed
- **Date:** 2026-09-25
- **Deciders:** Radu Pop (acceptance pending)
- **Relates to:** [ADR-0003](0003-engine-modular-monolith-over-hash-chained-ledger.md), [ADR-0004](0004-composite-and-dislocation-resolutions.md), [SRS revision v2.3.4 proposal](../research/srs-revision-v2.3.4-proposal.md)

## Context

[SRS DEC-001](../srs/PrimeScore-SRS.md#dec-001-must) requires a deploy decision only when all
configurable conditions hold and every condition's evaluated value recorded, but names no
condition. [DEC-003](../srs/PrimeScore-SRS.md#dec-003-must) requires top contributing signals,
the dislocation value and dissenting signals in every decision, without saying which signals
qualify. The contract's `DecisionRecord` has no context, no free-text explanation and a single
`decided_at`, while the engine records decisions for two contexts and, after a backfill, for
dates years before the recording time. Urgency tiers and approvals (DEC-004) need position
construction, which is Milestone B.

## Decision

1. **When.** One decision is recorded for every dislocation, that is for every new assessment
   of a context member (ADR-0004 §7), under the triggering signal's correlation id and the
   configuration version in force. DEPLOY requires every condition to hold; otherwise IDLE.
   Neither outcome opens a position in v1.
2. **Conditions** (brief §9.6), each recorded with operator, required value, actual value,
   pass or fail, and the actual value in words: the dislocation's magnitude against the
   context threshold; `|composite| ≥ 0.5`; contributing categories `≥ 1`; certainty of the
   top contributing signal `≥ 0.5`; NYSE trading days since the newest contributing
   observation `≤ 2`; and no active cooldown, recorded as satisfied until RSK-001 exists.
   Every value except the cooldown is configuration, labelled "uncalibrated default"; the
   dislocation threshold stays per context in the dislocation settings.
3. **Explanation.** Top contributing signals are the confirmed assessments on the composite's
   side of zero, strongest `|severity × certainty|` first, at most three; dissenting signals
   are every confirmed assessment on the other side. A composite of 0 has neither. A text
   explanation naming the failed conditions, or the scenario of a DEPLOY, is kept in the
   ledger entry and shown in the UI, since the contract has no field for it.
4. **Sign** (brief §9.7). A positive dislocation is the vol-expansion scenario and a
   negative one the vol-compression scenario; a zero dislocation is neither.
5. **Time.** `decided_at` is the observation time the decision refers to, so `GET /decisions`
   with `from` and `to` answers "what would the rules have decided on these dates" (SIG-004).
   The recording time stays on the ledger entry and in the audit trail, whose `from` and `to`
   bound the recording time.
6. **Audit.** The audit trail is the decisions' ledger entries, so the hash chain makes a
   changed entry detectable (AUD-002). v1 records DEPLOY and IDLE events only.

## Consequences

- A decision is reproducible from the ledger: its inputs are the composite and dislocation
  entries under its correlation id, plus the assessment entries of every confirmed
  assessment that composite lists (each under its own signal's correlation id), and its
  configured conditions are copied into it.
- The API cannot tell which context a decision belongs to; the UI, the ledger and the audit
  input snapshot can. Adding the field is a contract change for the operator to accept.
- After a backfill every historical close has a decision, which is what replay and the
  validation report need; the audit trail then lists them by recording time.

## Trade-offs

- Deciding on every member assessment records several decisions per trading day (one per
  classified member signal). A once-per-day decision would need a scheduling rule the
  SRS does not give; the History screen shows the day's last one.
- Ranking top signals by conviction ignores category weights, so a strong signal of a
  lightly weighted category can lead the list. The weighted contributions stay visible on
  the composite.

## References

- Build brief §9.6 and §9.7 (the operator's working document; not published in this repository).
- [SRS revision v2.3.4 proposal](../research/srs-revision-v2.3.4-proposal.md) §4 states the
  default conditions as proposed SRS text.
