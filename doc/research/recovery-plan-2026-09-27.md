# Recovery plan after the failed edge test

Date: 27 September 2026. Input: `edge-test-2026-09-27.md`, `doc/srs/PrimeScore-SRS.md` §3 and
CLS-006, the recorded configuration version 2. This note says what went wrong, what the
mathematics did and did not promise, how the next tests are protected from being tuned into a
false positive, and the order of recovery.

## 1. What went wrong, precisely

1. **A descriptive statistic was given a predictive meaning.** ECDF rank, rolling median and
   signed deviation are correct mathematics: they say how unusual today's level is against five
   years of history. Nothing in that mathematics says which way the level moves next. SRS §3
   "Sign convention" declares that above-median means "vol expansion". For a mean-reverting
   series such as VIX, above-median is when the level usually falls. The formulas were proved to
   describe; they were assumed to predict.
2. **The dislocation contains no independent information.** CLS-006 defines
   `SignalImpliedIV = MarketObservedIV × (1 + CompositeScore × k)`, so
   `Dislocation = MarketObservedIV × CompositeScore × k`. The "signal-implied" value is the
   market value rescaled by the score. It cannot disagree with the market because it is made
   from the market.
3. **The event-driven thesis was never in the data.** In practice only MARKET_DATA was
   classified: VIX scored against VIX, OVX against OVX. Macro surprise has no consensus source,
   geopolitical and cross-asset are unimplemented. The original thesis, that implied volatility is
   mispriced ahead of scheduled catalysts, has not been tested. It is untested, not refuted.
4. **The validation targets could not fail.** The ten events were chosen because volatility
   spiked. A detector of high volatility fires on every one of them after the spike. Firing on
   the famous days measured recognition of the past, not prediction.
5. **The sequence put the cheapest test last.** Engine, ledger, replay, UI and harness were
   built before a one-day check on FRED data that would have shown the sign problem. Tests
   verified that the code implemented the specification. No test verified that the
   specification described the market.

None of this is a coding error. The repository's honesty labels ("uncalibrated multiplier, not an
independent forecast") were accurate. The research instrument worked and returned a negative
result. The failure would be to ignore it or to tune around it.

## 2. Rules that prevent tuning the result into existence

The concern is legitimate: a rule adjusted until it passes on the same data is not evidence.
Every further test follows these rules, written before the test runs.

- **Pre-registration.** Hypothesis, data, metric, horizon, pass threshold and kill threshold are
  written in a file and committed before any result is read.
- **One look at the held-out period.** Fit or choose on 2011 to 2018 only. Run on 2019 to 2026
  once. A second run on the held-out period is a new pre-registration.
- **Count every attempt.** Every rule tried is logged; pass thresholds are corrected for the
  number tried.
- **Tradable, not spot.** Any directional claim is measured on VIX futures or option prices, not
  on the spot index, which cannot be traded and does not include what the market already priced.
- **Separate roles.** The agent that designs a rule does not judge it. A second agent, instructed
  to refute, reviews design and result before anything is recorded as a pass.
- **Read-only evidence.** Test scripts open the ledger read-only and do not share code with the
  engine's decision path, so a change to the engine cannot change the test.
- **Publish either way.** Negative results are recorded with the same detail as positive ones.

## 3. Hypotheses still open, cheapest first

| # | Hypothesis | Data needed | Cost | Odds and reason |
| --- | --- | --- | --- | --- |
| R1 | Stretched volatility mean-reverts and this is tradable beyond what futures already price | Cboe VIX futures settlements, free | Days | Low. Well known and largely priced in the term structure. Worth one pre-registered test because it is free |
| R2 | Stretched states precede larger absolute moves, which long-volatility structures need | Index history, then option prices | Days on index; option data for the real test | Medium. The first look showed OVX signal days move more; premiums may already reflect it |
| R3 | The original thesis: implied volatility is mispriced ahead of scheduled catalysts | Catalyst calendar, consensus, VIX futures or option implied volatilities | Paid data, weeks | Unknown. Never tested. This is what the product was meant to be |
| R4 | No tradable edge; the asset is the auditable platform | None | None | Certain to be true if R1 to R3 fail; sells to desks that bring their own signals |

## 4. Order of recovery

1. **Record the negative result** as an ADR: the MARKET_DATA-only directional scenario is
   refuted; CLS-006 is descriptive; `docs/STATUS.md` and the public site stop presenting the
   scenario as an input to any decision. Duration: one day.
2. **Pre-register R1 and R2** in this directory with thresholds, then run them once on the
   held-out period under §2. Duration: days. Free.
3. **Decide on R3.** If R1 or R2 shows anything, R3 gets the data budget and the same protocol.
   If neither does, R3 is the only remaining product hypothesis, and the decision is whether to
   fund its data or stop.
4. **Rebuild the model only after a pass.** The classifier stays as a descriptive engine. A
   predictive layer, if any, is a new module with its own pre-registered validation, not a
   change to the sign convention.
5. **If nothing passes, choose R4 or stop.** R4 reuses ledger, replay, harness and dashboard
   without a signal claim. Stopping is a valid outcome of research.

## 5. What to do differently from here on

- A specification formula is a hypothesis until a held-out test passes. Write it as one.
- The first deliverable of any signal idea is the test, not the engine.
- Kill criteria are written before the build starts.
- Any positive result is reviewed by an adversary before it reaches a customer or a page.
