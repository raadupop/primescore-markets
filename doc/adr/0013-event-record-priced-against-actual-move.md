# ADR-0013: Event record: the priced 9-day move against the actual move, from Cboe indices

**Status:** Accepted

**Date:** 2026-10-02
**Deciders:** Radu Pop (operator); coding agent

## Context

Slice 3 answers, per catalyst family: what did options price before each event, and what
happened? Forces:

- No option prices are licensed. Cboe prohibits extracting its delayed quotes (ADR-0009); a feed is
  bought only after an option-based registration (TODO-013). The free series are Cboe's index
  closes: VIX9D (9 calendar days), VIX (30 days), SPX and OVX (30-day oil).
- VIX9D is a variance measure across strikes, skew included, so an at-the-money option prices a
  smaller move than VIX9D implies.
- A 9-day window after one event often contains others (CPI and FOMC weeks overlap), and weekly
  families (claims, petroleum) overlap themselves.
- VIX9D is live from 2013-10-01; earlier rows are back-calculated. Derived figures rest on Cboe rows
  only (ADR-0012), never on FRED windows (TODO-018).
- The research programme already looked at FOMC and CPI days: the pre-registered futures test of
  2026-09-27 (2011 onward) and an ad hoc count of CPI priced against actual moves since 2013-10.
  Those rows are not fresh evidence.
- No short-dated oil index exists; OVX covers 30 days and there is no free crude price series.

## Decision

**Placement.** `GetCatalystOutcomes(family)` in the Analytics module computes the record on read
from the Catalysts contracts (current schedules) and Cboe-recorded series. Nothing is stored. API
`GET /analytics/catalyst-outcomes?family=` (contract 1.4.0, additive), READ role.

**Definitions** for a scheduled (not cancelled) catalyst E on New York date D at instant T:

1. Read date a: the NYSE trading day immediately before D. Its VIX9D, VIX and SPX closes must exist
   (OVX for WPSR and OPEC); a must be on or after 2013-10-01.
2. Event close c: the first NYSE trading day on or after D whose 16:00 New York close is after T.
3. Window end w: the latest NYSE trading day on or before a + 9 calendar days, VIX9D's horizon.
4. Priced move P = VIX9D(a) ÷ 100 × √(9/365): one standard deviation of the S&P 500 over the window.
5. Actual move A = SPX(w) ÷ SPX(a) − 1. Inside the priced range when |A| ≤ P.
6. Straddle cost estimate K = √(2/π) × P, the expected absolute move for that standard deviation and
   the approximate price of a 9-day at-the-money straddle, as a share of the index, if options were
   priced at VIX9D. A move with |A| < K would have lost the straddle buyer money. Because VIX9D
   includes skew, a real straddle costs less: the count is an upper bound on buyer losses.
7. Event-day move M = SPX(c) ÷ SPX(a) − 1. Volatility change ΔV = VIX9D(c) − VIX9D(a) in index
   points; for WPSR and OPEC ΔV = OVX(c) − OVX(a), and P, A, K and M are not computed.
8. The ratio before is the ADR-0012 value read on a.

**Rows and counts.** Only events with c on or before the latest close are rows. A row whose w is
after the latest SPX close is "window open": its 9-day figures are absent. Events whose a precedes
2013-10-01, and events missing a needed close, are counted, never filled.

**Labels.** Each row states the number of scheduled FOMC, CPI, NFP, GDP, PCE or OPEC events other
than E in (a, a + 9 days], and its never-rescheduled value (ADR-0011). FOMC and CPI rows dated on or
before 2026-09-27 are labelled "previously examined".

**Summaries.** For all complete rows and for the latest 12: n, the median P, the median |A|, the
count inside the priced range, the count below K, the median |M|, the median ΔV and the count where
volatility fell. Counts are shown with n; nothing is called a hit or a miss, and no interval is
computed, because windows overlap. Beside the counts the dashboard shows the rates fair pricing would give for normally
distributed moves: 68% inside the range and 58% below K, since a fair straddle loses more often
than it wins.

**Levels.** P reveals the VIX9D level, so the record is served only where ADR-0009 allows levels:
the operator's dashboard and READ or ADMIN tokens. Whether any of it may be published is decided
before the first public output.

## Consequences

- The question "did options overprice this kind of event?" gets a per-event table any reader can
  recompute from Cboe's files and the release calendar.
- Index-based estimates stand in for option prices until a feed is licensed; the straddle count is biased
  toward buyer losses and says so.
- A 9-day window measures more than the event; the other-events count shows how much.
- Oil events get only the change in 30-day oil volatility.

## Trade-offs

A 9-day window matched to VIX9D's horizon was chosen over an event-day-only comparison, which would
need an event-specific implied move that only option prices can give.

## References

ADR-0009, ADR-0011, ADR-0012. `doc/slices/03-event-record.md`. SRS CAT-004.
`doc/research/preregistration-2026-09-27.md` (design 2).
