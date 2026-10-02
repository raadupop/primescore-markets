# Customer test kit: PrimeScore Markets

Prepared 27 September 2026. Sources: `doc/briefs/markets-product-study.md`,
`sites/markets/index.html`, `doc/research/edge-test-2026-09-27.md`. An AI agent drafts,
schedules and aggregates; the founder verifies, talks to people and presses send.

**Status after T1.** The edge test failed: on 3,841 signalled days (from 15,849 recorded decisions) the signalled direction
was right 34 to 41 percent of the time; volatility mean-reverts from the flagged states. The
interviews (§2 to §4) test the problem, not the signal, and proceed unchanged. The campaign (§5)
is **on hold until T1b, T1c or T1d passes**, and nothing in it claims an edge. The verifiable
record is presented as transparency: results are published whether good or bad.

## 1. Where the primary customer is

Reddit blocks automated rule fetches, so subreddit rules are unverified: the founder reads each
sidebar before the first post. Verified rows: checked 27 September 2026.

| Venue | Who is there | Rule on self-promotion | Verified | Approach |
| --- | --- | --- | --- | --- |
| r/options (about 1.4M) | Retail options traders | Expected: no solicitation, no newsletter or Discord links | Unverified | Value comments on catalyst-loss threads, no links; DM after a reply exchange |
| r/VolatilityTrading | Small, VIX-specific | Unknown | Unverified | Ask mods; value post (§3); later a mod-approved AMA on the failed test |
| r/thetagang | Premium sellers, hurt in February 2018 | Expected: no self-promotion | Unverified | Comment on "held through the event" threads; short-vol value post |
| r/PMTraders | Portfolio-margin traders, capital above 100k | Expected: strict, flair-verified | Unverified | Weekly discussion thread only; ask mods before any post |
| r/algotrading | Systematic traders, some desk staff | Expected: no vendor promotion | Unverified | Technical post on point-in-time ledgers, no link; recruit in comments |
| Elite Trader forum | Experienced retail, small pros | Ads and solicitations prohibited unless a paying sponsor or vendor (monthly fee, real name) | Verified (terms page) | Post in VIX threads as a member; message after participation; vendor status only if T3 resumes |
| Wilmott forums | Quants, desks | Contact the admin before plugging software; topics removed at discretion | Verified (terms) | Desk track: email the admin, then a technical thread |
| X: @bennpeifert, @jam_croissant, @SqueezeMetrics, @spotgamma, @KrisAbdelmessih; $VIX $VXX $UVXY $SVIX | Fintwit vol traders and vendors | Spam policy; DMs reach only followers or open-DM accounts unless the sender has Premium; 500 DMs a day unverified | Verified (X limits page, guides) | Substantive replies under vol-trade posts; DM only after a public exchange; threads (§5) when T3 resumes |
| Moontower (Substack 23k+, Discord about 1,000) | Vol-literate readers of Kris Abdelmessih | Substack comments open to subscribers; Discord rules not checked | Partly verified | Comment on vol posts; in Discord, ask a mod before an interview request |
| Smashing Volatility (volatilitywiz.substack.com) | VIX futures and ETP readers | Comments open to subscribers | Verified (about page) | Comment; the author is a candidate interviewee and referrer |
| SpotGamma Discord | Paying subscribers (pass the "pays today" screen) | Subscribers only; code of conduct published | Verified (support pages) | Only if the founder subscribes; no promotion; DM after conversation |
| Stocktwits $VIX, $UVXY streams | Retail, noisy | No ad or sign-up-gated links; promotion at most 10 percent of posts; first 50 posts restricted; no Discord or Telegram promotion | Verified (best practices) | Share the record page as a direct article with a useful note; reply to VIX posts |
| Telegram | Dominated by Deriv "Volatility 75" synthetic-index signal channels: not our customer | Per-group admin rules | Unverified | Delivery channel, not recruiting; ask admins only in groups an interviewee names |
| LinkedIn | Desk contacts, family offices | Platform anti-spam; no group rules checked | Unverified | Connection note naming the shared contact, then the email in §3 |

## 2. Screener

Sent by form link before any call. Points in brackets.

| # | Question | Options and points |
| --- | --- | --- |
| 1 | What do you trade most often? | VIX options or futures (2) · VIX ETPs (VXX, UVXY, SVIX) (2) · SPX or SPY options around scheduled events (2) · Crude oil options (2) · Shares or single-stock options only (0) · I do not trade options (0) |
| 2 | How long have you traded options? | Under 1 year (0) · 1 to 3 years (2) · 4 to 10 years (2) · Over 10 years (1) |
| 3 | Have you held a position through a scheduled event (CPI, FOMC, earnings, OPEC) and lost money on it? | More than once (2) · Once (2) · No (0) · Prefer not to say (0) |
| 4 | Size of your options account? | Under 10,000 USD (0) · 10,000 to 50,000 (2) · 50,000 to 250,000 (2) · Over 250,000 (1) |
| 5 | Which do you pay for today? (any) | SpotGamma, Unusual Whales, Market Chameleon, ORATS, TradingView Premium, another paid newsletter or Discord (2) · None (0) |

Qualifies: 8 or more of 10 with no zero on Q1 or Q4. Score 7: reserve list. Over 250,000 USD
with a desk affiliation: desk track. Optional, unscored: "Your last catalyst trade in one sentence",
used to order the calls.

## 3. Outreach, ready to paste

### DM to a trader who posted about a volatility trade

> Read your post on the [UVXY / VIX call] trade. The part about deciding the night before the
> print is the problem I am researching. I build a research tool that scores VIX and OVX closes
> against five years of history and records each reading in a hash-chained ledger before the open.
> It is not advice and has no track record; the first back-test is public and it did not pass. I am
> talking to ten traders about how they time volatility entries around catalysts. Twenty minutes,
> your call, recorded only with your consent. I will send you the summary. Any afternoon this week
> or next?

### Community post (value first, then the ask)

> **5 February 2018, walked through a public record.** Friday 2 February the VIX closed 17.31.
> Monday it closed 37.32, the largest one-day rise on record; XIV lost most of its value and was
> terminated. A classifier scoring that close against 1,260 prior closes records severity +0.9992.
> A hash-chained ledger would have fixed that reading before Tuesday's open; nobody could edit it
> later. The same ledger would show the next closes: 29.98, 27.73, 33.46, 29.06, 25.61. The reading
> described the state well and the direction badly: the stretched state reverted within a week, as
> stretched states usually have. It would not tell you what to buy. I am studying how traders decide
> around catalysts like this and want twenty-minute calls with ten of you. General information, not
> advice. Comment or message me.

### Email to a desk contact

> Subject: Point-in-time volatility record, 20 minutes
>
> [Name], [shared contact] suggested I write. I run PrimeScore Markets, a research engine that
> classifies VIX and OVX closes against their five-year history and writes every decision to a
> hash-chained, replayable ledger; any past date can be reproduced as it was known then. Our first
> edge test failed on direction, and I can send the numbers and the script that reproduces them. I am
> interviewing three desks about how they would evaluate a signal history like this, what
> documentation they need, and where a daily feed would fit. Twenty minutes by phone. No sale: the
> product is general information, not advice, and nothing is offered for purchase today. Would
> [two dates] work?

## 4. The 20-minute interview

Record with consent. The founder asks; the agent transcribes and fills the template. No product
talk before minute 12.

| Min | Question | Follow-ups |
| --- | --- | --- |
| 0–2 | Consent to record. "I am here to learn how you trade, not to sell." | — |
| 2–5 | How do you trade volatility today? | Which instruments? How often? What do you look at the night before a catalyst? |
| 5–9 | Tell me about your last catalyst trade. What did it cost? | What did you know at entry? What would have changed the decision? How long did deciding take? |
| 9–12 | What do you pay for now, and why that one? | What did you cancel, and why? What would make you cancel this one? |
| 12–15 | Shown a record of past calls, what would you need to see to trust it? | Would you check a hash? Sample sizes? Losing periods? Whose word would you take? |
| 15–18 | If a brief arrived each morning, what would be in it? What is one line too many? | Before the open? Levels or states? Structures or none? |
| 18–20 | Where would you want it: email, Telegram, X, a tool you already use? Who else should I talk to? | One referral by name. Permission to send the summary. |

**Pass signals to listen for.** (1) They describe the timing problem in their own words before
minute 12, without being led. (2) They pay for something today and can say what it is for.
(3) They name a concrete check they would run on the record (losing streaks, sample size, hash
verification, out-of-sample period) rather than asking about returns.

### Note template (one page, filled during the call)

```text
ID: T2-__   Date:        Screener: __/10   Segment: trader | desk
Instruments / frequency:
Night-before routine:
Last catalyst trade, cost, what they knew at entry:
Time to decide:
Pays today (name, price, purpose):        Cancelled before (why):
Trust requirement on the record (their words):
Brief: must contain / must not contain:
Delivery channel:                         Referral:
Signals: timing unprompted Y/N | pays today Y/N | concrete check Y/N
Verbatim quotes:
1.
2.
3.
Surprise of the call:
Next step / consent to summary:
```

The agent aggregates the ten templates: signals per interviewee, paid tools and channels by count,
trust requirements clustered, brief contents ranked. T2 passes at six of ten on signal 1 and five
of ten on signal 2.

## 5. Campaign copy for T3: ON HOLD

Hold until a follow-up test (T1b tradability, T1c alert frequency or T1d magnitude) passes on the
held-out period and is on the record page. Every piece states that the product is general
information, not personal advice, and none promises returns.

### Landing-page hero

- Headline: A volatility regime brief you can audit.
- Subhead: Each trading day, VIX and OVX closes are scored against five years of history,
  timestamped and hash-chained before the US open. The record is public, including the days the
  reading was wrong. General information, not personal advice.
- Call to action: Get the daily brief (one email address).

### Three X-thread outlines

| Thread | Posts, in order |
| --- | --- |
| 5 Feb 2018, VIX | 1 Friday 17.31, Monday 37.32: the record's most stretched VIX reading (+0.9992). 2 What the ledger entry holds: input, source (FRED VIXCLS), score, hash, time. 3 What it does not hold: a trade. 4 The next five closes, and why stretched readings have tended to revert. 5 How to verify the hash yourself. 6 Disclosure: general information, not advice; the first edge test failed and is published. |
| 5 Oct 2017, VIX | 1 Close 9.19, the lowest ever at the time; severity −0.8849, Compressed. 2 Compressed does not mean "buy volatility now": four months passed before February. 3 What a daily brief would have said each day, honestly: state unchanged. 4 Why a record of boring days matters: it shows how long states last. 5 Disclosure as above. |
| 16 Sep 2019, OVX | 1 Abqaiq attack Saturday; Monday WTI up about 14 percent, OVX 35.48 to 48.58, severity +0.8452. 2 Event days earn a record its trust: the entry is written before anyone knows what follows. 3 The closes that followed: 44.89, 40.44, 41.15, 38.99. 4 OVX signal days have shown larger moves either way (median 2.84 against 2.14 points), not a predictable direction. 5 Disclosure as above. |

### Reddit value post (text only; link only with mod approval)

> **What a hash-chained volatility record looks like after a bad back-test.** We scored every VIX
> and OVX close since 2011 against its five-year history and wrote 15,849 decisions to a
> point-in-time ledger. Tested, the direction the score implied was right 34 to 41 percent of the
> time. Stretched volatility mean-reverts, which the term structure already prices.
> We published it because a record is only worth something if it cannot be curated. The next tests
> are alert frequency and absolute move size, fitted on 2011 to 2018 and measured on 2019 to 2026.
> If you want the daily regime brief when a test passes: it is general information, not advice, and
> it will never tell you what to buy. Questions about method welcome.

### First three daily-brief examples

Illustrative: reconstructed for past dates, not forecasts; the live ledger entry governs.

| Field | 5 Feb 2018 · VIX | 5 Oct 2017 · VIX | 16 Sep 2019 · OVX |
| --- | --- | --- | --- |
| Close, change | 37.32, +20.01 from 17.31 | 9.19, −0.44 from 9.63 | 48.58, +13.10 from 35.48 |
| State, signed severity | Extreme expansion, +0.9992 | Compressed, −0.8849 | Extreme expansion, +0.8452 |
| Decision recorded | Conditions met (fired on 55 percent of VIX days: a state marker, not an alert) | Wait | Conditions met (42 percent of OVX days) |
| Historical behaviour in this state | Stretched VIX readings have more often fallen than risen over the next 5 and 21 days (back-test hit rate 0.38 and 0.35 for the implied direction). Not a forecast. | Compressed states have persisted for months; the record shows how long, not when they end. Not a forecast. | OVX signal days have preceded larger absolute moves (median 2.84 against 2.14 points at 5 days) in either direction. Not a forecast. |
| Next known catalyst | US CPI, 14 Feb 2018 | US payrolls, 6 Oct 2017 | FOMC decision and EIA weekly report, 18 Sep 2019 |
| Evidence | Entry hash, FRED VIXCLS, record page | Same | Entry hash, FRED OVXCLS, record page |

Disclosure block on every brief: "General information, not personal advice. Identical for all
readers; no sizing. Buying options can lose the full premium. Levels as reported by FRED, with
attribution. Past states do not predict future states. Methodology and the failed edge test are on
the record page. Sent [time UTC]."

## 6. Four-week operating rhythm

T2 runs now; T3 is preparation only until a follow-up test passes. Founder time is capped at
three hours a week.

| Week | Mon | Tue | Wed | Thu | Fri |
| --- | --- | --- | --- | --- | --- |
| 1 Recruit | Founder confirms subreddit rules (30 min); agent finalises drafts | Community post (§3) where mods allow | Agent lists 15 recent vol-trade posters; founder sends 10 DMs (30 min) | 2 interviews | Agent sends screener to replies; weekly aggregate |
| 2 Interview | Desk emails to 3 contacts (founder, 15 min) | 2 interviews | X replies from agent drafts (founder approves, 15 min) | 2 interviews | Agent fills templates, flags referrals |
| 3 Interview | Referral DMs (founder, 20 min) | 2 interviews | Desk call | 2 interviews | Agent aggregates 8 to 10 templates; founder reads (30 min) |
| 4 Decide | Founder reads the aggregate against the T2 pass line (45 min) | Agent revises T3 copy in the interviewees' own words | Engineer reports T1b, T1c, T1d | Founder decides: pricing conversation, iterate the message, or stop the retail track | Summary to all interviewees (agent drafts, founder sends) |

**Founder, personally, each week (under 3 hours):** the calls (four at 20 minutes), DMs and
emails sent under their own name, approval of anything published, the weekly aggregate.

**Agent, each week:** monitor venues for vol-trade posts and list candidates; draft every message in
the site's tone; schedule approved posts; transcribe calls; fill and aggregate templates; track
replies, screener scores and interviews in one sheet; keep T3 assets current but unpublished.
