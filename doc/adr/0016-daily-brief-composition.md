# ADR-0016: Daily brief: one dashboard page per market, composed from recorded and computed figures

**Status:** Accepted

**Date:** 2026-10-02
**Deciders:** Radu Pop (operator); coding agent

## Context

Slice 5 answers "what matters today?" on one page per market. Every part already exists as a
recorded decision or a figure computed on read: the volatility state (ADR-0008), the calendar and
ratio (ADR-0011, ADR-0012) and the event record (ADR-0013). Cboe-derived figures stay in personal
research use (ADR-0015), so the brief has no reader but the operator: no API, no channel, no store.
After a first calendar run hundreds of catalysts are recorded at once.

## Decision

**Scope.** One brief per market context (equity, oil), for the brief date: the New York date of
the context's latest decision. `GetDailyBrief(context)` in the Analytics module, computed on read;
nothing is stored. Dashboard page only.

**Parts and sources.**

1. **State.** The brief date's last decision: level, percentile, state. Days in state: consecutive
   NYSE trading days back from the brief date whose last decision carries the same state; a day
   without a decision, or with a pre-ADR-0008 label, ends the run. The previous state is the state
   of the day that ended it.
2. **Cross-asset grid.** VIX, VXN, RVX, VVIX, OVX and GVZ, less the market's own reference index,
   which the state line shows from its recorded decision (a window that still holds FRED-recorded
   closes, so a Cboe-only rank of it can differ by a fraction of a percentile): the latest Cboe
   close on or before the brief date, its percentile among up to 1,260 prior Cboe closes (ties count as at or below), and
   the state from the published rule (`VolatilityState`) with the default regime bounds (30th and
   70th percentiles) and tail (5%); fewer than 252 prior closes gives `unknown`.
3. **Releases ahead.** Scheduled catalysts whose event close falls in the next 10 NYSE trading days
   after the brief date, with the ADR-0012 ratio and percentile.
4. **Record for those families.** The event record's latest-12 summary per family (ADR-0013).
5. **Just passed.** Catalysts whose event close is the brief date, with their event-record row.
6. **New on the calendar.** Catalysts first recorded after the previous trading day's 16:00 close,
   scheduled after the brief date: at most 20 listed, the total stated.

Every line links to its source (decision, ledger entry, event record). A plain-text version on the
page serves the operator's notes. The page carries the ADR-0015 restriction.

## Consequences

- The operator reads one page instead of five, with each figure traceable to the page it came from.
- The brief cannot be shown to anyone else until TODO-024 is settled.
- A brief for a past date is not offered: point-in-time composition of the calendar and record
  would need their state as of that date.

## Trade-offs

Composing on read was chosen over storing each brief: nothing reads a stored brief yet, and storing
the published text becomes necessary only when briefs leave the dashboard.

## References

ADR-0008, ADR-0011, ADR-0012, ADR-0013, ADR-0015. SRS ANA-004. `doc/slices/05-daily-brief.md`.
