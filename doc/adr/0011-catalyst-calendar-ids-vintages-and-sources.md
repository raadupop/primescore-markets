# ADR-0011: Catalyst calendar: canonical ids, schedule vintages and sources

**Status:** Accepted

**Date:** 2026-09-29
**Deciders:** Radu Pop (operator); v3 coding agent

## Context

Design v3 (§3, §5.2) makes scheduled macro and market events ("catalysts") the join key for
decisions, outcomes and brief sentences. Tests over them must report the subset of events that were
never rescheduled (§9, look-ahead through calendars), so the engine must know what was announced
and when, not only when an event happened. Releases do move: the 2013 and 2025 US government
shutdowns moved CPI and the Employment Situation (NFP) by up to 48 days.

The originators publish schedules in different shapes, checked on 2026-09-29. The Federal Reserve
lists FOMC meetings per year on one page, with SEP meetings starred and next year's dates marked
tentative, and keeps a historical page per year; the 2016 onward statements state 14:00 New York,
the 2013–2015 ones only "for immediate release". BLS publishes one page per release (reference
month, date, time) and yearly archived schedules; its terms reserve the right to block robots
without contact information, and requests without a contact in the user agent were refused. BLS
also offers a calendar feed without the reference month. BEA publishes a calendar feed with UTC
instants and the reference period in each summary (2025-01 onward). EIA states the WPSR standard
(Wednesday 10:30 New York after the data week) and tables only the exceptions. DOL's claims
schedule page refused every automated client; DOL states that claims are published Thursdays at
08:30 New York, the day before when Thursday is a federal holiday. opec.org serves its listings
behind a browser challenge. Fed, BLS, BEA, EIA and DOL content is public domain.

Archived schedules show the dates releases actually happened, not the dates first announced.

## Decision

**Module.** A Catalysts module owns the calendar: its own table of schedule vintages, two ledger
kinds (`CatalystScheduled`, `CatalystRescheduled`, stage `catalysts`) and a projection. Its
contracts are records and enums (`GetCatalysts(from, to, family?)`, `GetCatalyst(id)` with every
vintage, `ImportCatalysts`). Its calendar sources implement the Ingestion `ISourceAdapter`
(ADR-0009) and record only Catalysts ledger kinds.

**Families and ids.** Families are FOMC, CPI, NFP, CLAIMS, GDP (every GDP estimate release), PCE
(Personal Income and Outlays), WPSR and OPEC (OPEC and JMMC meetings). The canonical id is
`FAMILY-YYYY-MM-DD` from the New York date of the first recorded schedule, for example
`FOMC-2026-10-28` (the second day of a two-day meeting). It never changes, including after a
reschedule. One catalyst per family per New York date; a new event that needs an id already held
by a moved event takes the suffix `-2`, `-3`.

**Vintages.** Every change of a matched catalyst's instant, time-announced flag, status (scheduled,
cancelled), SEP flag or tentative flag appends a new vintage; nothing is updated in place and
titles are ignored. Seeing the same schedule again writes nothing. A row matches a catalyst by the
same family and source key (reference period, data week or curated key); else by the same family
and New York date; else, for FOMC only, the nearest current meeting of the same source not seen at
its date, within 21 days.

**Source kinds and precedence.** Each observation has a kind, in ascending precedence: Rule
(derived by rule), Curated (operator CSV), Archive (originator's historical page), Listing
(originator's current schedule). A differing row appends a vintage only when its kind is at least
the current vintage's, or when an operator import is forced; otherwise the run gets a flag naming
the kept vintage. A listed catalyst that disappears from its source's coverage, or whose date the
source replaces with a placeholder (TBD, blank), is left unchanged and flagged; the operator
decides.

**Change classes.** A vintage carries the reason it was appended, in this order: `correction` (the
current vintage was rule-derived and a higher kind replaces it), `status`, `moved` (the New York date
changed, or an announced time changed), `time_announced` (a time added on the same date), `detail`
(SEP or tentative only). Only `moved` and `status` count as reschedules.

**Never rescheduled** is nullable: false when any vintage is `moved` or `status`; otherwise null
when vintage 1 was back-filled; otherwise true. A catalyst is back-filled when its scheduled
instant is not after the moment the engine first recorded it. `first_announced_at` is the recording
time of vintage 1, never a claimed earlier announcement.

**Sources.** Each adapter has its own `Sources:<Name>` section, is disabled unless `Enabled` is
true, sends the configured user agent, and records source URL, fetch time and the fetched file's
SHA-256. Names differ from the print adapters of design step 3.

| Adapter | Source | Kind |
| --- | --- | --- |
| `FedCalendar` | Fed current FOMC calendar; yearly historical pages from 2013 as back-fill. Decision at 14:00 New York on the meeting's last day, every year | Listing, Archive |
| `BlsCalendar` | BLS per-release pages for CPI and the Employment Situation (not the feed: without the reference month a moved release cannot be matched); yearly archives from 2013 as back-fill. Disabled until the user agent names a contact | Listing, Archive |
| `BeaCalendar` | BEA calendar feed (wider coverage than its web page, UTC instants) | Listing |
| `EiaCalendar` | EIA WPSR schedule: every data week from the first to the last week in EIA's exception table; weeks without a table row get the stated standard, labelled `standard-weekday`; nothing past the table's last week | Listing |
| `ClaimsCalendar` | Derived by rule from DOL's stated practice: Thursday 08:30 New York, the Wednesday before when Thursday is a federal holiday; labelled `rule`, source a rule identifier rather than a fetched page, no file hash | Rule |
| `OpecCalendar` | Operator-curated CSV with a source URL, retrieval time and verifier per row | Curated |

**Back-fill.** History comes from originators' archive pages where they exist (FOMC 2013 onward,
CPI and NFP 2013 onward) and otherwise from the same curated CSV format through the CLI verb
`import-catalysts`, whose rows may give an originally scheduled date; such a row records vintage 1
at the original date and vintage 2 at the final date. A curated file is validated whole: one invalid
row records nothing. `--force` lets an import replace a higher kind and is recorded as forced.

**Not in this decision.** Consensus rows (`cat_consensus`, `ConsensusCaptured`) are decided together
with their writer; no table is created without one. Decisions carry no `catalyst_id` under this
decision.

## Consequences

- Decisions, outcomes and briefs can join on an id that survives reschedules; the vintages show
  every move with its source and ledger hash.
- A daily rule run cannot undo an originator's or operator's date, and an archive cannot undo the
  originator's current listing. A lower-kind source that disagrees raises the same flag on every run.
- Back-filled catalysts show the dates releases happened. Their never-rescheduled value is unknown
  unless an import records the original date, so a robustness split has three groups (true, false,
  unknown), not two.
- Claims dates are asserted by rule, not read from DOL, and OPEC dates depend on curation. Both are
  labelled, and a later originator listing replaces a rule vintage as a correction.
- A layout change at an originator fails that page loudly (nothing recorded from it) instead of
  withdrawing catalysts.
- FOMC 2013–2015 times rest on the 14:00 assumption.

## Trade-offs

The id is fixed at first announcement instead of following the current date: a moved event keeps an
id naming a date on which nothing happens, in exchange for joins that never break.

## References

ADR-0003, ADR-0009, ADR-0012. `doc/briefs/markets-v3-design.md` §3, §4, §5.2, §5.4, §9.

- Fed: <https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm>, historical pages
  `https://www.federalreserve.gov/monetarypolicy/fomchistorical{year}.htm`
- BLS: <https://www.bls.gov/schedule/news_release/cpi.htm>, <https://www.bls.gov/schedule/news_release/empsit.htm>,
  archives <https://www.bls.gov/bls/archived_sched.htm>; terms <https://www.bls.gov/bls/blsterms.htm>
- BEA: <https://www.bea.gov/news/schedule>, feed <https://www.bea.gov/news/schedule/ics/online-calendar-subscription.ics>
- EIA: <https://www.eia.gov/petroleum/supply/weekly/schedule.php>
- DOL (basis of the claims rule): <https://oui.doleta.gov/unemploy/claims.asp>
- OPEC: <https://www.opec.org/press-releases.html>
