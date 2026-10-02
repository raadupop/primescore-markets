# ADR-0008: Directional volatility scenario refuted; classifier is descriptive

**Status:** Proposed

**Date:** 2026-09-27
**Deciders:** Radu Pop (review pending)

## Context

CLS-006 defines the signal-implied volatility as the market-observed level multiplied by one plus
the composite score times a sensitivity factor. The dislocation is therefore the observed level
times the score times the factor: it carries no information independent of the observed level.
The SRS sign convention reads a level above its rolling median as "vol expansion". In practice
only MARKET_DATA signals were classified, so each reference index was scored against itself.

An outcome test over the 15,849 decisions recorded under configuration version 2 (research note
`doc/research/edge-test-2026-09-27.md`, reproducible with `scripts/edge_test.py`) found the
signalled direction correct 34 to 41 percent of the time at 5 to 21 trading days on the 3,841
signalled days (one state per market per date), and below 50 percent at every horizon in both
halves of the history. Volatility indices mean-revert from the stretched states the
classifier detects. The ten validation events were selected because volatility spiked, so a
detector of elevated volatility fires on all of them after the fact; they could not refute the
model.

## Decision

The MARKET_DATA-only directional scenario is refuted. CLS-001 severity, CLS-002 composite and
CLS-006 scenario are descriptive statistics of how unusual an observation is against its
reference history. They are not forecasts and are not presented as inputs to any trading or
research decision.

Any predictive claim about a market quantity is a separate hypothesis. It is validated before it
is built, under a pre-registered protocol: hypothesis, data, metric, horizon, pass and kill
thresholds committed before results are read; parameters chosen on 2011 to 2018 only; one run on
2019 onward; every attempted rule counted and thresholds corrected for the count; directional
claims measured on tradable instruments, not spot indices; design and judgment by separate
reviewers, one instructed to refute; negative results recorded with the same detail as positive
ones. The protocol text lives in `doc/research/recovery-plan-2026-09-27.md` §2.

The SRS sign convention and CLS-006 remain as written for reproducibility of recorded decisions.
A predictive layer, if one passes validation, is a new module with its own requirement, not a
change to the sign convention.

The decision gate becomes the reference level's distance into either tail of its own history,
`min(p, 1 − p)` of the dislocation's regime percentile, compared with a configurable
`level_percentile_tail` condition (default 0.05, operator `<=` or `<`, at least 252 prior closes,
percentile computed in either regime mode). The dislocation and the always-true cooldown
placeholder are no longer evaluated; `composite_score` becomes optional. A DEPLOY records an
extreme state (`extreme_low` or `extreme_high`) and asserts no direction; the stored `scenario`
field carries the state label. Stored versions without the tail condition gain it as a new,
audited configuration version on start; the composite gate is dropped and every other threshold kept.

Every decision meets an outcome: the Analytics module computes, on read from the ledger, the
change from the reference close on each decision's trading day to the close exactly 1, 5, 10 and
21 NYSE trading days later, for DEPLOY days against all days and per state, on the instrument each
decision placed, from the live journal or from a stored replay. Decisions with pre-ADR-0008 labels
are counted separately and never as extreme states. The contract adds
`GET /analytics/outcomes`, the decision's `state` and `level_percentile`, and the dislocation's
`regime` and `regime_percentile` (version 1.2.0).

Four follow-up hypotheses were pre-registered and each run once on 2019 onward
(`doc/research/preregistration-2026-09-27.md`): VX futures carry (KILL), catalyst volatility crush
(INCONCLUSIVE), variance risk premium by state (KILL) and move magnitude by state (KILL in sample).
None supports a predictive rule.

## Consequences

The product's honesty labels were accurate; the presentation removes the scenario and the
"Conditions met" result from any position of implied action and keeps them as descriptive
evidence. Over 2016 to 2026 the state gate fires on 13.5 percent of VIX days and 9.8 percent of OVX
days (the refuted gate: 55 and 42 percent), which satisfies DEC-002's idle discipline by construction. The original event-driven thesis
(implied volatility mispriced ahead of scheduled catalysts) remains untested, since no catalyst, consensus or option-price data was classified;
it is neither confirmed nor refuted by this record. Engine, ledger, replay and harness remain
valid as an auditable research platform. The validation-event report is no longer evidence of
model behaviour; it measures recognition of past spikes.

## Controls

- Evaluator unit tests pin the four conditions, the state labels at each tail boundary, and that the
  dislocation and cooldown are not evaluated.
- The settings test pins the automatic, audited upgrade from the composite gate to the state gate.
- HTTP acceptance tests pin hand-counted percentiles on the Volmageddon fixture (1259 / 1260 on
  5 February 2018, 1082 / 1259 on 2 February, 1255 / 1260 on 6 February), the replay with a looser
  tail, and that `GET /analytics/outcomes` counts the same DEPLOY days as `GET /decisions` and never
  measures a horizon past the last close.
- `outcomes-report` reproduces the record from the ledger on the command line.

## Trade-offs

Recorded decisions keep the refuted semantics so that history replays exactly; the cost is that
readers must consult this ADR to interpret them. The sensitivity factor and dislocation threshold
stay configurable although they drive nothing predictive; removing them would break the contract.

## References

ADR-0004, ADR-0005, ADR-0007. SRS §3 "Sign convention", "IV Dislocation"; CLS-006; ANA-001.
`doc/research/edge-test-2026-09-27.md`, `doc/research/recovery-plan-2026-09-27.md`.
