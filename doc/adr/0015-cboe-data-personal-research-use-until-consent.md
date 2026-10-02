# ADR-0015: Cboe data stays in personal research use until Cboe consents

**Status:** Accepted

**Date:** 2026-10-02
**Deciders:** Radu Pop (operator); coding agent

## Context

ADR-0009 made Cboe's daily index history files the source of record and described them as free
"for internal use with no redistribution". Cboe's website terms, read on 2026-10-02, are narrower:
they allow viewing and downloading "one copy of the Materials for your personal non-commercial
use"; storing them "in an electronic retrieval system", creating "a derivative work (for example,
a financial product, service or index)", using them "to verify or correct other data", publishing
and distributing all need "Cboe's prior written consent"
(`doc/research/data-licence-cboe-2026-10-02.md`). The SPX values belong to S&P Dow Jones Indices.

Every market figure since slice 1 (states, percentiles, ratios, priced moves, outcome counts,
position scenarios) rests on these files. FRED's terms already ruled out FRED (ADR-0009). The
product is meant to be sold.

## Decision

Until Cboe's written consent or a licence covering storage and derived output is recorded in a
later ADR:

- The engine is the operator's personal research tool. No figure derived from Cboe files is
  published, sold, shown to a customer or prospect, or served to anyone but the operator.
- READ and ADMIN tokens are issued to the operator only.
- The daily brief is a dashboard page; nothing sends it anywhere.
- The FRED cross-check stays disabled: comparing FRED's closes with Cboe's uses Cboe data "to verify
  or correct other data".
- Pages that show Cboe-derived figures carry this restriction where a reader could copy them.

This supersedes ADR-0009's statement of the licence terms; its other decisions stand.

## Consequences

- No customer test, pilot or public brief can show market figures before consent; the product
  study's campaign steps wait on it.
- Building and measuring continues locally; the decision costs no engine change.
- If Cboe refuses or prices it out, the measures move to licensed option prices (TODO-013) or
  another licensed source, and the ledger keeps its Cboe rows as personal research history.

## Trade-offs

Continuing to build on data that cannot yet be sold was chosen over pausing: the measurements and
code carry over to a licensed source, and a consent request needs a concrete description of the
derived figures, which the built product provides.

## References

ADR-0009, ADR-0012, ADR-0013, ADR-0014. `doc/research/data-licence-cboe-2026-10-02.md`.

- Cboe terms: <https://www.cboe.com/terms>, <https://www.cboe.com/us_disclaimers/>,
  <https://www.cboe.com/use-of-content>
