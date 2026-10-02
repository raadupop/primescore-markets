# ADR-0009: Cboe index history as source of record; FRED demoted to a read-only cross-check

**Status:** Accepted
Licence statement superseded by [ADR-0015](0015-cboe-data-personal-research-use-until-consent.md).

**Date:** 2026-09-29
**Deciders:** Radu Pop (operator); v3 coding agent

## Context

Until this decision the engine recorded every market close, the cross-asset basket (SP500, DGS10,
DCOILWTICO, DEXUSEU) and the macro prints (CPI, initial claims) from FRED, from 2011 onward, as
ledger entries. FRED's legal terms prohibit storing, caching or archiving FRED content in any
database and its use for machine learning [D3]. The ledger is append-only and hash-chained
(ADR-0003): a recorded row cannot be removed without breaking verification of every later entry,
and recorded assessments, composites and decisions were computed from those rows.

Cboe publishes one daily history CSV per index at no cost and without a key, for internal use with
no redistribution of the series [D1]. Files exist for VIX, VIX9D, VIX3M, VIX6M, VVIX, OVX, GVZ,
VXN, RVX and SPX; EVZ has none. Cboe updates them between about 18:00 New York time and two days
after the close. Some rows precede an index's live dissemination and were back-calculated by Cboe.
Live starts checked on Cboe primary pages on 2026-09-29: VIX9D 2013-10-01 and VXMT (now VIX6M)
2013-11-27, both from Cboe press releases; the current VIX methodology dates from 2003, day not
stated; VVIX has no dated launch; the VIX3M, OVX, GVZ, VXN, RVX and SPX files start after their
launch. The SPX series belongs to S&P Dow Jones Indices, redistributed by Cboe. Cboe prohibits
automated extraction of its delayed quote tables [D18].

More sources follow (calendars, prints from their originators, GDELT), some owned by other
modules, so source handling cannot stay FRED-specific.

## Decision

**FRED records nothing.** The FRED source becomes a read-only cross-check. Once a day at 08:15 New
York time it fetches, for each Cboe index that FRED republishes, FRED's values for the dates of
the most recent Cboe-recorded closes, holds them in memory for that run only, and compares. A
difference beyond a configured tolerance, or a FRED date the Cboe series lacks, becomes a flag on
the source's run record. A flag names the index, the FRED series id and the date, never FRED's
value. Run records are operational state, not ledger facts. There is no disk cache. Error text
carries at most FRED's own error message, never a response body or the key.

**Existing FRED rows stay as recorded history under this ADR.** They are never deleted and never
republished. The series read rule is unchanged: per series and New York date the earliest-recorded
observation wins, so FRED rows stay the series of record for 2011 to 2026-09-28 and continue as
inputs to derived computations (classifier reference windows, dislocation history, outcome rows).
Cboe rows for the same dates are recorded beside them (ADR-0010 keeps them from being assessed
twice). Whether published derived figures may rest on FRED-sourced windows is not decided here; it
is decided before the first public output. Any exemption, precedence change or removal needs a
later ADR.

**Cboe index history files are the source of record** for the indices above, under source prefix
`cboe:` and provider `Cboe`. The series are for internal use and are never redistributed; the same
applies to SPX. Delayed quote tables are never extracted. Raw levels are visible only to the
authenticated operator: the dashboard pages and the READ/ADMIN API, whose existing fields carry
levels (`market_observed_iv` on dislocations and in replays, index-point changes in the outcomes
record). READ keys are for internal use. No public surface serves raw series; published output
carries derived figures only.

Each close is stamped on its data date, never at fetch time: the volatility indices at 16:15 New
York, when the SPX options behind VIX stop trading; SPX at 16:00, the equity close and the
stamp of the FRED `SP500` closes it continues. Dates before 1987 follow the US daylight-saving rule
of their year. The 30-day volatility indices use variant `IMPLIED_VOLATILITY`, the same as their FRED
history; VIX9D, VIX3M and VIX6M add the tenor (`IMPLIED_VOLATILITY:9D`, `:3M`, `:6M`), the
convention an API submission with a tenor gets. SPX is a cross-asset `basket_observation` under
instrument `SPX`, separate from FRED's `SP500`. Provenance carries the file URL, fetch time, the
fetched file's SHA-256 and a `reconstructed` marker: true before a verified live start, false from
a verified live start or where the file starts after launch, and absent where the start is
unverified. Unknown is never recorded as live.

**Source adapters.** Every source implements `ISourceAdapter`, published in the Ingestion
module's contracts so other modules can register adapters. An adapter has a name, a disabled reason
(null when enabled; invalid settings also disable it), a run-on-startup switch, a schedule, its
series, and a pull that returns counts, recorded signal ids, per-item errors, flags and a note.
Schedules are data in New York wall time (daily runs and polling windows that may cross midnight,
with every-day, weekday or NYSE-trading-day filters), converted to UTC for each run so daylight
saving is handled. Adapters are singletons with singleton dependencies. Recorded ids are Ingestion
signal ids only; an adapter in another module records its own ledger kinds, and recording signals
from another module needs its own command under its own ADR. Each adapter is configured under
`Sources:<Name>` and is disabled unless `Enabled` is true. The source prefixes `fred:`, `cboe:`,
`cboe-vx:`, `bls:`, `bea:`, `cal:`, `nowcast:`, `pm:`, `gdelt:`, `gpr:` and `usgs:` are reserved
for adapters; API submissions using them are rejected.

## Consequences

- No FRED content enters the ledger from 2026-09-29. The ledger still holds the FRED content
  recorded before, which FRED's terms do not permit; removing it would break the hash chain and
  every record built on it. The exposure is bounded to no new content, no republication and
  operator-only access.
- The basket and the macro prints stop updating until their originators are ingested, and EVZ has
  no source. VVIX gains a history (from 2006) that FRED never had.
- VIX arrives up to about two days after the close, so VIX-driven records appear later; they stay
  stamped at observation time, so point-in-time logic is unaffected.
- A `reconstructed` marker in the ledger cannot be corrected, so the adapter must not be enabled
  on the live ledger while a live-start date is still unsettled.
- Disagreement between Cboe and FRED becomes visible without FRED's value being kept.
- New sources plug into one scheduler, one status page and one run record without source-specific
  branches.

## Trade-offs

The FRED history stays in use for derived computations instead of being replaced by Cboe rows: it
keeps every recorded assessment and replay reproducible, at the cost of a licence exposure that
this ADR bounds but does not remove.

## References

ADR-0003, ADR-0006, ADR-0010. `doc/briefs/markets-v3-design.md` §4, §5.1, §8, §9.

- [D1] <https://www.cboe.com/tradable-products/vix/vix-historical-data/>, <https://www.cboe.com/us_disclaimers/>,
  VIX9D launch <https://ir.cboe.com/news/news-details/2013/CBOE-Introduces-Short-Term-Volatility-Index-10-01-2013/default.aspx>;
  VXMT launch: Cboe press release "CBOE Introduces The CBOE Mid-Term Volatility Index", 2013-11-27, on ir.cboe.com
- [D3] <https://fred.stlouisfed.org/legal/>
- [D18] <https://www.cboe.com/delayed_quotes/>
