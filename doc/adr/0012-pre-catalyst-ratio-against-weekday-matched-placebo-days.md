# ADR-0012: Pre-catalyst 9-day/30-day ratio against weekday-matched placebo days

**Status:** Accepted

**Date:** 2026-09-29
**Deciders:** Radu Pop (operator); v3 coding agent

## Context

Design v3 shows, for each upcoming catalyst (ADR-0011), the 9-day/30-day ratio VIX9D ÷ VIX − 1,
the slope of the short end of the S&P 500 volatility term structure, against its distribution on
weekday-matched non-event days (§3 item 2). Registration F2 (§6) later tests the same quantity on
FOMC, CPI and NFP days against placebo days at the same weekday, one, two and three weeks earlier,
outside all halos, and fixes its placebo definition and halo on the 2013-10 to 2018-12 fit window.

Forces identified:

- VIX has a day-of-week pattern [E1], and NFP falls on Fridays and FOMC decisions on Wednesdays, so
  an unmatched baseline confounds weekday with event.
- The ratio is read on the last date with both closes, which can be days or weeks before the
  catalyst. VIX9D's horizon is nine calendar days, so the gap between reading and event matters.
- VIX9D is live from 2013-10-01; earlier rows are back-calculated (ADR-0009).
- For VIX, FRED-recorded rows are the series of record through 2026-09-28 (ADR-0009), and whether
  published figures may rest on them is open.
- CLAIMS and WPSR are released every Thursday and Wednesday.
- Cboe series are for internal use (ADR-0009): levels must not be served where the ratio is.

## Decision

**Placement.** The ratio and its baseline are computed on read in the Analytics module
(`GetCatalystRatios`), from the Catalysts contracts (the current calendar) and the Ingestion
contracts (the series). Nothing is stored. The Catalysts contracts stay a calendar.

**Cboe only.** Only VIX and VIX9D closes recorded under source prefix `cboe:` are read.
`GetObservationSeries` gains an optional source prefix (additive; absent means every source, the
previous behaviour). The derived figure therefore never rests on a FRED window.

**Definition.**

1. The ratio R(d) = VIX9D(d) ÷ VIX(d) − 1 exists for an NYSE trading day d on or after 2013-10-01
   with both closes; holiday-session prints are ignored, a missing close stays missing.
2. For catalyst E on New York date D: the as-of date a is the latest trading day before D with R(a);
   the value is R(a); the offset c = D − a in calendar days.
3. Past events: current, scheduled events of E's family dated on or before a, other than D, on D's
   weekday.
4. Placebo days p = D′ − 7w for w = 1, 2, 3 and each past event D′; each is read at q = p − c
   calendar days, so q has a's weekday and VIX9D's calendar horizon from q covers the same weekdays
   as from a. A candidate whose p or q is not an NYSE trading day is dropped and counted; one whose
   q precedes 2013-10-01 is dropped.
5. Halo: the trading days from one before to one after each scheduled FOMC, CPI, NFP, GDP, PCE or
   OPEC event. A candidate is excluded (and counted) when p **or** q lies in a halo: q must be
   event-free too, because the value is read there.
6. Baseline: R(q) of the remaining candidates; a missing R(q) is counted, never filled. n is the
   count; the percentile is the share of baseline values at or below R(a), shown only when n ≥ 10.
7. CLAIMS and WPSR get the value and no baseline: every same weekday is one of their event days.
8. Halo families with no event on or before the earliest considered read date are reported as
   unscreened, so the halo's incompleteness is visible.

The API carries the as-of date, value, percentile, n, missing closes and the no-baseline reason;
the other counts appear in the module contract and on the Catalysts page. No index level is served.

**Display baseline.** This is the baseline of the Catalysts page, the API and the brief's record
table. F2's registration either adopts it by reference or sets its own definition on its fit
window, and records which.

**Deviation from design §5.3/F2 ("outside every halo").** CLAIMS and WPSR carry no halo: a halo
around every Wednesday and Thursday would remove whole weekdays from every baseline.

## Consequences

- The rank compares like with like: same weekday, same reading offset, no scheduled event of a halo
  family on the placebo or read day.
- With the one-week offset and a reading the day before (c = 1), q = D′ − 8 days, so VIX9D's
  nine-day horizon from q still reaches the family's own past event D′: those placebo values are
  not event-free. The design's offsets are kept and the limit is stated.
- The baseline uses the calendar as known now (latest vintages), not as known at each read date.
  Where a family's calendar starts later than the read dates (back-fill gaps), its halo is
  incomplete and reported as such.
- Until Cboe history is recorded the ratio has no value; FRED's VIX rows are never used for it.
- A read in the weeks before an event (c large) is ranked against baselines read equally far
  ahead, so small n is common early in a family's history; below 10 the rank is withheld.

## Trade-offs

The calendar-day offset keeps weekdays and VIX9D's horizon aligned at the cost of dropping reads
that land on holidays; a trading-day offset would keep them but shift read dates across weekdays.

## References

ADR-0009, ADR-0011. `doc/briefs/markets-v3-design.md` §3, §5.3, §6 (F2), §9.

- [E1] <https://www.skidmore.edu/economics/documents/KurovWolfeGilbert-TheDisappearingPre-FOMC-Announce-Drift-200914.pdf>,
  <https://www.federalreserve.gov/econres/ifdp/files/ifdp1376.pdf>, VIX weekday pattern
  <https://www.cxoadvisory.com/calendar-effects/vix-day-of-the-week-effects/>
