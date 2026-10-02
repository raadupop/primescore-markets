# PrimeScore Markets v3 — design: the catalyst record

Author: chief architect, 28 September 2026, revised after two adversarial reviews the same day.
For: the coding agent that implements it on the existing engine. Budget: about $0 a month for
infrastructure, at most tens of dollars a month for data and model calls, one operator. Research
behind every external claim: §10; items marked unverified could not be confirmed. Paths are
relative to the repository root.

## 1. Direction check

The direction statement ("event-driven market intelligence connecting macroeconomic, geopolitical
and market signals to volatility dislocations, options strategies and position risk") is sound and
unoccupied at the zero-budget end. Of the eleven products surveyed, each sells one of its legs:
scheduled events, the volatility term structure, options-implied moves, a daily written note. None
of the eleven publishes an auditable record of its own calls. The repository has so far built and
tested only the leg the statement values least, market signals ranked against themselves, and
refuted its directional reading (ADR-0008). The umbrella pitch's own "next proof" asked for exactly
that test; it is done.

| Leg of the statement | Status after v3 |
| --- | --- |
| Market signals | Served now: states, percentiles, outcomes record, replay, ledger |
| Macroeconomic signals | Served in v3: release calendar with canonical event IDs, first-release prints from public-domain originators, free consensus proxies, surprise scores |
| Volatility dislocations | Retired as defined (the level times a rank of itself). v3 records two quantities around each catalyst, the 9-day/30-day ratio before and the change after, each against its own weekday-matched baseline; neither is compared as a mispricing |
| Geopolitical signals | Served descriptively in v3: GDELT counts and the GPR daily index as labelled context; no language model until an evaluated classifier exists |
| Options strategies | Later, gated: needs a licensed option feed (a $40 a month tier exists); templates are measured, never recommended |
| Position risk | Deferred: needs positions, option prices and rules; nothing in v3 claims it |

## 2. What the market pays for, and the gap

- The habit product is a short written note, not a dashboard: SpotGamma's twice-daily Founder's
  Note ($89 to $299 a month), Tier1Alpha's Market Situation Report ($49.95), Menthor Q Premium
  ($129), Barchart's emailed screens ($29.95). [R1] [R2] [R3] [R4]
- The retail-proven event-record format exists: Market Chameleon's "implied move vs actual move
  over the last 12 earnings" ($69 to $99). No product found has built it for scheduled macro
  catalysts. [R5]
- None of the eleven publishes an auditable per-call record. SpotGamma's single aggregate
  calibration figure (SPX inside its implied one-day move on about 76 percent of days) is the
  category's whole disclosure. [R6] A hash-chained, pre-registered, per-event record is empty ground.
- Positioning converges on "augment the trader, never call direction" (Vigil Labs, SpotGamma,
  Clarion Labs). [R7] That is ADR-0008's stance already.
- Zero-cost delivery is the norm: Discord bots, TradingView alerts, email digests, MCP endpoints.
  Founding-member tiers are standard. Price band for a daily verifiable brief: $20 to $50 a month;
  the record as point-in-time data over API is the later desk product. [R8]
- Of the founder's named labs, only Clarion (narrative feed verified against data), Vigil
  (prediction markets as an input) and Meridian (Inspect, an open evaluation framework) yield
  anything; the rest are not finance companies. [R9] [R10] [R11]

### 2.1 Mapped from Clarion Labs

Clarion Labs (UK, one named founder, SEIS/EIS advance assurance, no funding or users disclosed)
runs one proprietary reasoning core, Sentix, under two surfaces: Krypta, a live consumer crypto
news app, and Vecta, an invitation-only institutional app that "reconciles institutional data
with market narratives". [R9] [R12] [R13] [R14] Their public material defines "AI-verified"
nowhere and publishes no metric, latency or accuracy figure. Mapping, item by item:

| Clarion element | What it is | What v3 takes | Where in this design |
| --- | --- | --- | --- |
| Sentix core under several surfaces | One reasoning layer licensed to many market-facing products | The engine and ledger are the core; the Catalyst Brief is the consumer surface, the record API the desk surface, and the same core later serves the other PrimeScore products | §3, §5.4 |
| Krypta breaking-news alerts | Push "the moment something important happens" | Event alerts, not only a daily brief: a level entering or leaving the extreme tail, a catalyst the day before, a print released with its surprise score. Each alert is a ledger entry with its hash | §3 item 6, §7 step 4 |
| Krypta "AI scoring" of news for sentiment and market relevance | Each item scored instantly; scoring method undisclosed | The same idea made testable: GDELT items per catalyst family get a recorded relevance score, and the score itself is measured against the realised move in the outcomes record. Only after an Inspect evaluation; never presented as verified before its tally | §7 later, gated |
| Krypta dynamic heatmaps | "Capital rotation and trend strength" across sectors | A cross-asset state grid: VIX, VXN, RVX, VVIX, OVX, GVZ, DGS10 and EURUSD percentiles in one view, coloured by state, showing where stress is rotating. Data already ingested or free from Cboe | §3 item 1, §7 step 3 |
| Krypta daily recap | Each day reduced to one summary | The brief's evening section "what changed today": state changes, catalysts passed, new consensus rows | §3 item 4 |
| Vecta "reconciles data with narratives" | Narrative and data shown together | A narrative check per catalyst: the dominant GDELT themes and tone before the event beside the recorded print, surprise and vol change after it. Descriptive, both sides linked to the ledger, no verdict on who was right | §3 item 5 |
| Krypta pricing | $7.99 a month, $69.99 to $95.99 a year; founding tiers "OG Yearly" $39.99 and "Early Phoenix" $49.99 | A consumer reference point well below the $20 to $50 band of volatility notes, and a founding-annual tier pattern for the 50-seat pilot | `doc/briefs/markets-product-study.md` T4 |
| "AI-verified" with no definition | Marketing claim | The opposite, as positioning: every sentence links to a hash, every score has a published tally, every test a registration | §2, §3 |
| Not taken | Native iOS and Android apps, io.net decentralised compute, Google Cloud, "sovereign reasoning layer" vocabulary | Static site, Telegram and email cost nothing and reach the same phone | §5.6 |

SentiX at sentix.org, a crypto media-sentiment tool that correlates media activity with price, is
a different company from Clarion's Sentix core as far as the public pages show (unverified).

## 3. The product

**Customer.** The self-directed volatility trader and the small research desk defined in
`doc/briefs/markets-product-study.md` §2.

**Daily artefact: the Catalyst Brief**, one per market (equity/VIX, oil/OVX). It is generated
when every input for a trading day is present in the ledger, which for Cboe indices is usually the
next morning (§4), and published with the data date and the publication time both shown. Every
sentence links to the ledger sequence and hash it was computed from. Sections, in order:

1. **State.** Reference level, percentile of its five-year history, state label, days in state,
   and a cross-asset grid of the same percentile for VIX, VXN, RVX, VVIX, OVX, GVZ, DGS10 and
   EURUSD, showing where stress sits and where it is moving.
2. **Catalysts ahead.** Next 10 trading days from the calendar, each with a canonical ID
   (`FOMC-2026-10-28`, `CPI-2026-10-14`, `WPSR-2026-10-01`), the current 9-day/30-day ratio
   (VIX9D/VIX − 1) shown against its distribution on weekday-matched non-event days, and, when a
   consensus proxy exists, the expected print with its source and capture time.
3. **Record for this catalyst type.** Market Chameleon's table, ported and made honest: the last
   12 events of the same family, with the ratio the day before, the event-day and five-day
   changes in VIX9D, VIX and VX30, medians with n. Rows before the live start of an index are
   labelled "reconstructed"; rows the research programme has already examined are labelled
   "previously examined"; nothing is labelled a hit or a miss.
4. **Yesterday's catalyst.** For each catalyst that just passed: ratio before, change after.
5. **Context and narrative check.** GDELT conflict-count percentile for the market's regions,
   the GPR daily index, and for each catalyst just passed the dominant GDELT themes and tone
   before it beside the recorded print and vol change after it, labelled descriptive. Disclosure
   block.
6. **Alerts between briefs.** Telegram posts when a level enters or leaves the extreme tail, the
   day before a catalyst, and when a print lands with its surprise score. Each alert is a ledger
   entry carrying its hash; alerts never contain a direction.

Forward-test tallies live on the research record page, described as hypotheses under test, with
no profit column anywhere.

**Never claimed.** A direction, a forecast, a trade, a return, a mispricing, or a verdict before
a registered sample size. "Extremely stretched" describes a rank; the ratio is a term-structure
slope; "after the last 12 CPI prints VIX9D fell a median Y" is a record.

**Distribution at $0.** Static record page and brief archive on Cloudflare Pages; Telegram
channel; Buttondown for email up to 100 subscribers (then its paid tier); a read-only JSON
endpoint per brief, exposed over MCP later. Free readers get each brief one trading day after
members.

## 4. Data plan

Every source is stamped point-in-time (observation time, not fetch time), carries provenance
(URL, fetch time, file hash), and stays inside the existing four contract categories. Licensed
raw series are stored privately and never republished; the record publishes derived states,
counts and outcomes only. Two licence findings change the existing engine: FRED's terms prohibit
storing FRED content in any database [D3], and Cboe prohibits automated extraction of its delayed
quote tables [D18]. v3 therefore moves prints to their public-domain originators and takes no
option chains until a licensed feed is bought.

| Source | Cost | Licence and redistribution | Access | Latency, history | Engine mapping |
| --- | --- | --- | --- | --- | --- |
| Cboe index history CSVs: VIX, VIX9D, VIX3M, VIX6M, VVIX, OVX, GVZ [D1] | $0 | Internal use; no redistribution of series | HTTPS CSV, no key | Files updated between about 18:00 ET and two days after the close (measured 2026-09-28: VIX3M 18:01 ET same day, VIX9D 21:51 ET, VIX about 57 hours later); VIX9D live from 2013-10-01, earlier rows back-calculated | MARKET_DATA, instrument per index, variant `IMPLIED_VOLATILITY` (tenor in variant for VIX9D, VIX3M, VIX6M), stamp 16:15 ET on the data date, source `cboe:`; polled, not scheduled |
| Cboe VX futures: daily settlement snapshot and per-contract history CSVs [D2] | $0 | Same Cboe terms | HTTPS CSV, no key | Snapshot after 16:00 ET carries no date column and holds placeholders until published: poll until values differ from the prior day, date it from the run. History per contract 2013 onward, archive to 2004 | MARKET_DATA instrument `VX`, variant = expiry; derived `VX30` flagged `derived` |
| BLS Public Data API v2 and release calendar (CPI, employment, claims via DOL) [D4] | $0, free key | Public domain, cite | REST (500 queries a day), ICS | 08:30 ET on release day; calendar a year ahead | Macro prints of record, first release stamped 08:30 ET; CALENDAR families `CPI`, `NFP`, `CLAIMS` |
| BEA API and schedule (GDP, PCE) [D5] | $0, free key | Public domain, cite; API terms accepted at signup | REST, ICS | 08:30 ET; year ahead | Macro prints of record for GDP and PCE; CALENDAR families `GDP`, `PCE` |
| FRED and ALFRED [D3] | $0 | Terms prohibit storing, caching or archiving FRED content in any database, and machine-learning use | REST, key | Next morning | Not stored. Read at run time only as a cross-check that raises a flag on disagreement; nothing FRED-sourced enters the ledger after v3 unless a written exemption is recorded in an ADR. The existing FRED rows stay as recorded history under that ADR |
| FOMC calendar and SEP median dot [D6] | $0 | Public domain | HTML twice a year; PDF | Year ahead; 14:00 ET | CALENDAR family `FOMC`; consensus row = prior SEP median for SEP meetings, parsed from the Fed's PDF, not FRED |
| EIA WPSR schedule and API v2 [D7] | $0, free key | Public domain, cite | HTML schedule, REST | Wednesdays 10:30 ET; stocks to 1982 | CALENDAR family `WPSR`; print = weekly crude stock change; consensus = trailing four-week mean, declared a proxy |
| OPEC and JMMC meeting dates [D8] | $0 | Facts; cite opec.org | Press-release HTML, poll weekly | Two months ahead; archive to 2016 | CALENDAR family `OPEC` |
| Cleveland Fed inflation nowcast [D9] | $0 | Cite with DOI; redistribution unverified | Daily page read; no API | Refreshed about 10:00 ET each business day; as-published history unverified | MACROECONOMIC consensus row for CPI and PCE: the seasonally adjusted month-over-month nowcast captured at 16:45 ET on the business day before release, with capture time |
| Atlanta Fed GDPNow workbook, NY Fed Nowcast, Philadelphia Fed SPF [D10] [D11] [D12] | $0 | Cite; redistribution unverified | XLSX downloads | GDPNow history to 2011; SPF to 1968, quarterly | Consensus rows for GDP once a GDP class exists (§7 step 9); SPF dispersion as an uncertainty state |
| Polymarket Gamma and CLOB APIs [D13] | $0 | Terms unverified (site certificate expired on 2026-09-28); publish derived probabilities only, with attribution, after terms are read | REST, no key | Live; history per market | Private daily 16:45 ET snapshot of FOMC and CPI markets; consensus and uncertainty inputs |
| GDELT 2.0 events [D15] | $0 | Unrestricted, redistributable, cite GDELT | HTTP zips every 15 minutes | 15 minutes; 2015 onward | GEOPOLITICAL instrument = region, variant = QuadClass and Goldstein bucket, daily counts; the one geopolitical stream the product may republish |
| GPR daily index (Caldara and Iacoviello) [D16] | $0 | Cite | XLS weekly | Weekly, revised; store vintages | GEOPOLITICAL instrument `GPR`, stamp = Monday publication |
| USGS earthquakes [D17] | $0 | Public domain | REST, no key | Minutes | GEOPOLITICAL exogenous shock and placebo generator |
| Cboe delayed option chains [D18] | n/a | Automated extraction prohibited; storage prohibited without written permission | n/a | n/a | Not used. Any use waits for written Cboe permission |
| ThetaData Value tier or ORATS Delayed API (later) [D20] [D21] | $40 a month, or $99 to $199 for one month | Single-user storage; confirm index-option coverage | REST | 2012 onward | The only option-chain path; bought after an option-based registration is written (TODO-013) and recorded in an ADR |
| Kalshi (later) [D14] | n/a | Terms read: personal, non-commercial; no scripts, compilation, archiving or software development without written consent | n/a | n/a | Not in v3. Listed only so it is not re-evaluated: written consent first |
| Dolthub community SPY chains [D19] | $0 | Licence unstated | SQL API | 2019 onward | Not in v3; research back-fill only if an option registration needs it, flagged unverified |
| Claude Haiku 4.5, batch (later) [D22] | About $4 to $8 a month at 500 headlines a day | n/a | API | Nightly batch | Geopolitical headline taxonomy on GDELT and ReliefWeb text only (never FRED content); only after an Inspect evaluation on a labelled set |

## 5. Architecture

Everything stays inside the modular monolith (ADR-0003) and the ledger. New modules follow the
seven-file shape and the structural rules; reference other modules through their Contracts only.

### 5.1 Ingestion

- Introduce `ISourceAdapter` (name, `DisabledReason`, `Schedule` expressed in America/New_York
  and converted per run, `PullAsync` returning counts and recorded ids) and refactor the FRED
  classes behind it. Per-source queue and run state; the `SourceScheduler`,
  `RequestSourcePullHandler` and `GetSourceStatusHandler` lose their FRED-only branches. A unit
  test covers a daylight-saving boundary. Reserve prefixes `cboe:`, `cboe-vx:`, `bls:`, `bea:`,
  `cal:`, `nowcast:`, `pm:`, `gdelt:`, `gpr:`, `usgs:` in `SignalValidator`.
- Adapters: `CboeIndexAdapter` (polls each CSV's Last-Modified and last row from 18:00 ET until
  the next 08:00 ET, ingests when the data date appears), `CboeVxAdapter` (settlement snapshot
  polled until values change; per-contract CSVs for history; derived `VX30` by linear
  interpolation between the two nearest monthlies, stamped `derived`), `BlsAdapter` and
  `BeaAdapter` (prints of record and calendars), `NowcastAdapter` (Cleveland page at 16:45 ET,
  GDPNow and NY Fed workbooks, SPF quarterly), `PolymarketAdapter`, `GdeltAdapter` (daily
  aggregate per region and class; raw zips not kept), `GprAdapter`, `UsgsAdapter`. The FRED
  adapter becomes read-only cross-check: it compares and flags, it does not record.
- Consensus rows are written through a new `IConsensusSource` contract in
  `PrimeScore.Modules.Classification.Contracts`, implemented by the Catalysts module and read by
  the classifier's MACRO route; the CSV `ConsensusBook` path is kept for hand-curated rows only.
  Each row: event id, source, basis (for CPI: seasonally adjusted month-over-month, two
  decimals), value, captured_at, URL. A capture after the print is refused.
- Test isolation: every adapter is disabled without an explicit `Enabled` flag; the acceptance
  harness strips the new `__` prefixes (`RunningEngine.IsEngineSetting`, `scripts/checks.py`,
  `tests/harness/test_checks.py`).

### 5.2 Catalysts module (new)

- Tables: `cat_events` (canonical id, family, scheduled_at, first_announced_at, actual_at,
  source_url, vintage), `cat_consensus` (event id, source, basis, value, captured_at, url).
- Ledger kinds: `CatalystScheduled`, `CatalystRescheduled`, `ConsensusCaptured`.
- Canonical IDs are the join key for decisions, outcomes and brief sentences. Rescheduling is a
  new entry, never an update; the never-rescheduled subset is a mandatory robustness split.
- Contracts: `GetCatalysts(from, to, family?)`, `GetCatalyst(id)`, `GetConsensus(id)`,
  `IConsensusSource`.

### 5.3 Analytics additions

- `GetCatalystOutcomes(family, instrument)` computed on read, like `GetForwardOutcomes`: per
  event, the ratio VIX9D/VIX − 1 on the day before, the event-day and five-day changes in VIX9D,
  VIX, VIX3M, VX30, OVX and SPX, medians, n, and labels (`reconstructed`, `previously examined`,
  `forward`). Baselines are weekday-matched placebo days at offsets of one, two and three weeks
  outside every event halo. Windows use NYSE trading days and the event's actual time; a change
  with a missing close is left out and counted.
- Forward-test registry: ledger kind `ForwardTestRegistered` (verbatim rule text, git commit hash
  of the pre-registration file, registration time, every pass and kill condition, required n,
  first eligible event = the first whose day-before settle falls after the entry's recording
  time) and `ForwardTestEvaluated` (one per event after the fact). A structural test fails if a
  registered rule's parameters change. The research record page shows the tally, the projected
  verdict date from the registered n and the observed event rate, and refuses a verdict early.
- `GetBrief(market, date)` assembles the brief from ledger facts only; the generator writes
  Markdown, HTML and JSON; every sentence carries `seq` and `hash`.

### 5.4 Contract 1.3.0 (additive)

`GET /catalysts`, `GET /catalysts/{id}`, `GET /analytics/catalyst-outcomes`,
`GET /forward-tests`, `GET /briefs/{market}/{date}`. DecisionRecord gains `catalyst_id` when a
decision falls inside an event window.

### 5.5 Dashboard and CLI

Pages: Catalysts (next 30 days with ratio and consensus), Catalyst record (the table), Forward
tests (research record), Brief preview. CLI verbs: `pull --source <name>`,
`brief --market equity --date <d>`, `publish` (writes the static site bundle),
`register-forward-test --file <json>`.

### 5.6 Running it for $0

- The engine runs on the operator's machine, which stays the authoritative host; the SQLite
  ledger is backed up nightly to Cloudflare R2 (10 GB free; a card on file is required to enable
  R2, and nothing is charged inside the limits). [I2] An Oracle Always Free instance may serve as
  a non-authoritative mirror restored from that backup; Oracle reclaims idle Always Free instances
  unless the tenancy is upgraded to pay-as-you-go, so the mirror is never the only copy. [I1]
  Step 4 includes a restore test.
- Runs, in America/New_York: 16:45 (VX settlement poll, Polymarket snapshot, Cleveland nowcast
  capture, GDELT day aggregate, Cboe index polling starts); 08:15 (BLS and BEA prints on release
  days, FRED cross-check, brief generation for the previous data date if every input is present,
  publish). A missing input leaves a visible "not yet published by Cboe" gap, never a back-fill.
- Publish: `wrangler pages deploy` uploads the static bundle to Cloudflare Pages (direct uploads
  do not consume the 500 monthly builds); the Telegram bot posts the brief and records the
  message id; Buttondown sends the email. [I3] [I4] [I5]
- GitHub Actions cron (2,000 free minutes a month on a private repository) is the fallback
  collector if the machine is off; it fetches raw files into R2 and the engine ingests from R2
  with the object key and hash as provenance. [I6]
- Failure of any step posts to the operator's Telegram.

## 6. Hypotheses and pre-registration

Rules of the protocol stay (ADR-0008): a power statement before registration; fit and evaluation
periods separated; the 2019 onward FOMC and CPI event days have already been examined once
(`doc/research/preregistration-2026-09-27.md`, catalyst design) and may not be used for a verdict
again by anyone who has seen those results; tradable instruments for directional claims; designer
and judge separated; negative results published. Verdicts come only at the registered n.

| # | Rule, frozen at registration | Data | Fit | Evaluation | Pass | Kill |
| --- | --- | --- | --- | --- | --- | --- |
| F1 Catalyst crush, forward (TODO-010) | The verbatim H1 of the 2026-09-27 registration: short VX30 from the settle before each FOMC and CPI release to the release-day settle, surviving-contract definition, 0.10 round trip, seed, all four pass and four kill conditions, cited by git commit hash | Cboe VX, BLS and Fed calendars | None | Events whose day-before settle falls after the registration entry; about 19 to 20 events a year, so n = 150 arrives about seven and a half years out; the page shows interim tallies and the projected verdict date | As registered on 2026-09-27 | As registered on 2026-09-27 |
| F2 Short-dated resolution, descriptive [E1] | Ratio VIX9D/VIX − 1 the day before and the event-day change in VIX9D on FOMC, CPI and NFP days against weekday-matched placebo days (same weekday, offsets of one, two and three weeks, outside all halos), reported per family and pooled | Cboe VIX9D (live from 2013-10-01), VIX, calendars | 2013-10 to 2018-12: placebo definition, halo, and the power statement (sd of event-day VIX9D changes, MDE for the median difference at the registered alpha with a three-family correction, the literature effect from [E1] in the same units), all recorded in the registration before any later data is read | Forward only from the registration date; 2019 to 2026 rows appear in the brief's record table labelled "previously examined", with no verdict | Median event-day VIX9D change below the weekday-matched placebo median, block-bootstrap interval excluding zero, at the registered n | Sign wrong, or interval covering zero at the registered n |
| F3 Surprise magnitude, descriptive [E2] | Absolute CPI surprise, first-release seasonally adjusted month-over-month headline CPI (BLS) minus the Cleveland seasonally adjusted month-over-month nowcast captured at 16:45 ET the business day before, against the absolute event-day change in VIX9D | BLS API, Cleveland nowcast, VIX9D | Only if as-published nowcast vintages can be obtained (Cleveland real-time file or archived page captures); regenerated history is not admissible. Otherwise no fit | Forward only from the registration date; one evaluation at exactly n = 60 (about five years of monthly prints), then a new registration | Spearman correlation above 0.2 with interval excluding zero at n = 60 | Correlation at or below zero at n = 60; 0 to 0.2 is inconclusive and says so |

Descriptive registrations (F2, F3) earn a sentence in the brief; they never earn a direction.

## 7. Build sequence

Superseded for order and acceptance by [doc/slices/](../slices/README.md) (2026-10-02): steps 1
and 2 are slice 2; the rest regroup into slices 3 to 8.

Each step ends demonstrable with `bash harness/check-suite.sh` green, an ADR for each decision, and
registry entries for deferred work. Dates assume one coding agent starting 2026-09-29. First
public value is the Catalysts page in week one and the first brief in week two, with the brief's
record table built on the Cboe indices and calendars only.

| Step | Delivers | Acceptance |
| --- | --- | --- |
| 1 (days 1 to 4) | `ISourceAdapter` with New York schedules; `CboeIndexAdapter` polling for VIX, VIX9D, VIX3M, VIX6M, VVIX, OVX, GVZ; registry entries; FRED adapter demoted to cross-check; ADR recording the FRED licence finding and the treatment of existing FRED rows | Unit tests on CSV parsing, stamping and the daylight-saving boundary; acceptance test: the VIX close for date D appears with `cboe:` provenance by the next 08:15 run, and no new FRED-sourced signal is recorded |
| 2 (days 3 to 7) | Catalysts module with BLS, BEA, FOMC, EIA and OPEC calendars; canonical IDs; Catalysts page listing the next 30 days with the current ratio against its weekday-matched baseline | Calendar vintage test (reschedule creates a new entry); page shows `FOMC-2026-10-28` with its ratio from ledger data |
| 3 (days 6 to 10) | `BlsAdapter` and `BeaAdapter` prints of record (CPI, claims, NFP, GDP, PCE) stamped at release time; `GetCatalystOutcomes` and the Catalyst record page for FOMC, CPI, NFP and WPSR on VIX9D, VIX and OVX with `reconstructed` and `previously examined` labels; cross-asset state grid on the overview | Hand-counted values for two events in unit tests; a 2019 to 2026 FOMC row carries the "previously examined" label; the grid shows each instrument's percentile from the same data date |
| 4 (days 9 to 14) | Brief generator with the evening "what changed today" section, Telegram channel with state-change and catalyst alerts (ledger kind `AlertSent`), static publish to Cloudflare Pages, record page, nightly R2 backup with a restore test; first public brief | Every brief sentence and alert resolves to a ledger sequence and hash; an alert fires once per state change (idempotent on event id and channel); a missing Cboe file shows as a gap; restore from R2 reproduces the ledger head hash; site tests cover the new pages |
| 5 (days 13 to 20) | `CboeVxAdapter` (snapshot polling and per-contract history) with derived `VX30`; forward-test registry; F1 registered verbatim from the 2026-09-27 file with its commit hash; F2 registered with its power statement computed on 2013-10 to 2018-12; research record page | Structural test fails on a changed registered parameter; the page shows the projected verdict dates; the first FOMC after registration appears in the F1 tally |
| 6 (days 18 to 26) | `IConsensusSource`; `NowcastAdapter` capturing the Cleveland nowcast at 16:45 ET with basis recorded; MACRO route live for CPI and PCE; F3 registered forward-only unless as-published vintages were obtained | Consensus row present for the next CPI with capture time before the print and the same basis as the print; surprise recorded on the morning of the print; a capture after the print is refused by a test |
| 7 (days 24 to 30) | `PolymarketAdapter` private snapshots after its terms are read and recorded in an ADR; `GdeltAdapter` daily counts plus top themes and tone per catalyst window, `GprAdapter`, `UsgsAdapter`; context and narrative-check section in the brief; a labelled placebo test registered | Counts percentile and themes shown with GDELT attribution and source links; no language model in the pipeline; nothing raw served (acceptance test asserts 404 on raw paths) |
| 8 (days 28 to 34) | GDP class in `infra/registry.yaml` with an explicit surprise definition against GDPNow; SPF dispersion state; MACRO route extended to GDP | Registry tests updated; a GDP advance print gets a consensus row and a surprise |
| Later, gated | Licensed option feed (ThetaData or ORATS) behind an ADR and an option-based registration; Haiku headline taxonomy and per-item relevance score after an Inspect evaluation on 200 hand-labelled headlines, with each score recorded and tallied against the realised move before any brief shows it; measured structure templates after a year of licensed chain history; MCP endpoint; Buttondown paid tier past 100 subscribers; Kalshi only with written consent | Each behind its own ADR and registration |

## 8. Cost and licence constraints

| Item | Monthly USD | Note |
| --- | --- | --- |
| Compute and hosting | 0 | Operator's machine; Cloudflare Pages and R2 free tiers (card on file for R2); optional Oracle mirror |
| Data | 0 | All v3 sources in §4 marked $0 |
| Email | 0 to 9 | Buttondown free to 100 subscribers |
| Telegram | 0 | |
| LLM classification (later) | 4 to 8 | Haiku batch, only after evaluation |
| Option feed (later) | 40 a month, or 99 to 199 once | Only after an option registration |

Constraints: Cboe raw series are never republished or downloadable and Cboe delayed quote tables
are never extracted; FRED content is not stored after v3 without a written exemption; Kalshi is
not used without written consent; Polymarket is not used until its terms are read; GDELT is the
only geopolitical stream that may be republished; every published figure carries attribution and
the brief carries the disclosure block from `doc/research/recommendation-rules-and-disclosures.md`.

## 9. Risks and what we never do

- **Publication lag.** Cboe files arrive hours to days late; the brief waits for its inputs and
  shows gaps rather than estimates. Members get the brief when the data exists, not "same evening".
- **Look-ahead through calendars and consensus.** Calendar vintages and capture times are stored;
  every test reports the never-rescheduled subset; a consensus captured after the print is
  refused; regenerated nowcast history is inadmissible for fitting.
- **Reuse of examined data.** The 2019 onward FOMC and CPI days are labelled and excluded from
  verdicts; new tests are forward-only.
- **Weekday confounds.** NFP falls on Fridays and FOMC on Wednesdays; every baseline is
  weekday-matched.
- **Small n.** Every registration carries a power statement; below the required n it is a tally.
- **Licence exposure.** Raw licensed data lives on the operator's disk or in a private bucket; a
  test asserts no endpoint serves it; scrapers that a site prohibits are not written.
- **Single operator.** Unattended runs, failure alerts, backups with a restore test; a missed
  brief is visible.
- **Reading as advice.** Identical content for all readers, no sizing, disclosure block, the
  legal shape recorded in the disclosures note.
- **Never.** A directional forecast; a trade or position; a return claim; a mispricing claim;
  real-time; a Kalshi-derived number; buying data before a registration needs it; a verdict
  before its n.

## 10. References

Research agents and adversarial reviewers verified these on 2026-09-28.

- [R1] <https://spotgamma.com/subscribe-to-spotgamma/>
- [R2] <https://accounts.hedgeye.com/products/market_situation_report/972!973>
- [R3] <https://menthorq.com/pricing/>
- [R4] <https://help.barchart.com/support/solutions/articles/242752-what-are-the-benefits-and-cost-of-a-premier-subscription->
- [R5] <https://marketchameleon.com/Premium>
- [R6] <https://support.spotgamma.com/hc/en-us/articles/15297901147923-SpotGamma-Implied-1-Day-Move>
- [R7] <https://www.forbes.com/sites/charliefink/2025/08/20/vigil-labs-ai-raises-57-million-to-build-bionic-traders/>
- [R8] <https://unusualwhales.com/pricing>
- [R9] <https://clarionlabs.org/>
- [R10] <https://meridianlabs.ai/>
- [R11] <https://www.businesswire.com/news/home/20260506853624/en/Tessera-Labs-Raises-$60M-in-Funding-Led-by-Andreessen-Horowitz-to-Transform-ERP-Modernization>
- [R12] <https://clarionlabs.org/krypta-case-study.html>
- [R13] <https://clarionlabs.org/investors.html>
- [R14] <https://apps.apple.com/us/app/krypta-smart-crypto-news/id6758259686>
- [D1] <https://www.cboe.com/tradable-products/vix/vix-historical-data/> and <https://www.cboe.com/us_disclaimers/> ; VIX9D launch <https://ir.cboe.com/news/news-details/2013/CBOE-Introduces-Short-Term-Volatility-Index-10-01-2013/default.aspx>
- [D2] <https://www.cboe.com/us/futures/market_statistics/settlement/> and <https://www.cboe.com/us/futures/market_statistics/historical_data/>
- [D3] <https://fred.stlouisfed.org/legal/>
- [D4] <https://www.bls.gov/schedule/news_release/cpi.htm> and <https://www.bls.gov/developers/>
- [D5] <https://www.bea.gov/news/schedule> and <https://apps.bea.gov/API/signup/>
- [D6] <https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm>
- [D7] <https://www.eia.gov/petroleum/supply/weekly/schedule.php>
- [D8] <https://www.opec.org/pr-detail/1574596-5-april-2026.html>
- [D9] <https://www.clevelandfed.org/indicators-and-data/inflation-nowcasting>
- [D10] <https://www.atlantafed.org/research-and-data/data/gdpnow>
- [D11] <https://www.newyorkfed.org/research/policy/nowcast>
- [D12] <https://www.philadelphiafed.org/surveys-and-data/real-time-data-research/survey-of-professional-forecasters>
- [D13] <https://docs.polymarket.com/api-reference/markets/get-prices-history>
- [D14] <https://kalshi-public-docs.s3.amazonaws.com/kalshi-data-terms-of-service.pdf>
- [D15] <https://www.gdeltproject.org/about.html>
- [D16] <https://www.matteoiacoviello.com/gpr.htm>
- [D17] <https://earthquake.usgs.gov/fdsnws/event/1/>
- [D18] <https://www.cboe.com/delayed_quotes/> (extraction prohibition) and <https://www.cboe.com/us_disclaimers/>
- [D19] <https://www.dolthub.com/repositories/post-no-preference/options>
- [D20] <https://www.thetadata.net/pricing>
- [D21] <https://orats.com/data-api>
- [D22] <https://platform.claude.com/docs/en/about-claude/pricing>
- [E1] <https://www.skidmore.edu/economics/documents/KurovWolfeGilbert-TheDisappearingPre-FOMC-Announce-Drift-200914.pdf> , <https://www.federalreserve.gov/econres/ifdp/files/ifdp1376.pdf> and the VIX weekday pattern <https://www.cxoadvisory.com/calendar-effects/vix-day-of-the-week-effects/>
- [E2] <https://www.nber.org/system/files/working_papers/w34702/w34702.pdf> and <https://www.clevelandfed.org/publications/economic-commentary/2023/ec-202306-real-time-assessment-inflation-nowcasting-cleveland-fed>
- [I1] <https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm>
- [I2] <https://developers.cloudflare.com/r2/pricing/>
- [I3] <https://developers.cloudflare.com/pages/platform/limits/>
- [I4] <https://core.telegram.org/bots/api>
- [I5] <https://buttondown.com/pricing>
- [I6] <https://docs.github.com/en/billing/managing-billing-for-your-products/about-billing-for-github-actions>
