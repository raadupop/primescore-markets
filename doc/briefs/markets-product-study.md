# PrimeScore Markets — product study: what to test before selling, and what to build to test it

Author role: product engineer and salesperson. Date: 27 September 2026. Input: the repository as
implemented through M6. Purpose: define the customer, the hypotheses that decide whether this can
be sold, the tests in order, the pass and kill criteria, and the prototype that must exist to run
those tests. Pricing is not decided here; it is the last thing tested. Engineering work derived
from this study is in `doc/briefs/markets-v2-build-brief.md`.

**Blocker, 2 October 2026:** no market figure may be shown to a customer or prospect until Cboe
consents or another licensed source is in place ([ADR-0015](../adr/0015-cboe-data-personal-research-use-until-consent.md), TODO-024); every customer test below waits on it.

---

## 1. What we have, in one paragraph

The engine takes daily VIX and OVX closes from FRED, scores each against five years of history
(signed severity, confidence), combines signals per market, applies an uncalibrated multiplier to
produce a "scenario", checks configured conditions and records **Conditions met** or **Wait** in a
hash-chained, point-in-time ledger, replayable for any past date. One operator reads it on a
local dashboard. It never says what to do, arrives a day after the close, covers two indices, has
no track record, no accounts, no hosting, no alerts. The parts worth money are the classifier,
the ledger and the replay: they make a verifiable record possible, which is what competitors
selling volatility opinions cannot offer.

## 2. The customer we are looking for

**Primary: the self-directed volatility trader.**

| Attribute | Definition used for screening |
| --- | --- |
| Trades | VIX options or futures, VIX ETPs (VXX, UVXY, SVIX), SPX or SPY options around scheduled events, or crude oil options |
| Experience | One to ten years; has lost money holding through a catalyst at least once |
| Capital | 10,000 to 250,000 USD in the options account |
| Pays today | At least one data, analytics or newsletter subscription (SpotGamma, Unusual Whales, Market Chameleon, ORATS, TradingView Premium or similar) |
| Lives online | r/options, r/VolatilityTrading, r/thetagang, options Discords, X (fintwit), Telegram trading groups |
| Job to be done | "Before a catalyst, tell me whether volatility is cheap or expensive right now, what happened in the past when it looked like this, and which structure fits, so I can decide in minutes instead of hours." |
| Not our customer | Buy-and-hold investors, day-trading scalpers, anyone who wants to be told exactly what to buy, anyone under 10,000 USD |

**Secondary, conversations only for now: small funds, prop desks and family offices** that would
buy the point-in-time signal history and a daily feed over API. They will not sign without a
record and documentation, so they are interviewed now and sold to later.

## 3. Hypotheses, in the order they must be tested

| # | Hypothesis | If false |
| --- | --- | --- |
| H1 Edge | After historical **Conditions met** days, the reference index moved differently from ordinary days, by enough for a long-volatility structure to matter | We cannot sell signals honestly. Pivot: sell the auditable research workflow to desks, and regime awareness plus education to traders |
| H2 Problem | The primary customer struggles with timing long-volatility entries around catalysts and already pays for help | No market. Stop or change customer |
| H3 Value | Shown the regime state, the historical outcomes and the verifiable record, this customer signs up for a daily brief | The message or the format is wrong. Iterate the message before touching price |
| H4 Payment | Some of those who signed up pay a founding price for a private pilot | Free product only, or desks only |
| H5 Habit | Pilot members open the brief most days and are still there after 30 days | Retention product problem: fix the brief before scaling |

Pricing, tiers and channels are tested at H4 and H5, not before.

## 4. The tests

### T1: edge test (H1), on existing data, this week

**Follow-up, 28 September 2026: no predictive rule found.** Four pre-registered hypotheses were run
once on 2019 onward: futures carry, variance premium and move magnitude were killed; the catalyst
crush was inconclusive and is now a forward test (TODO-010). The engine records volatility states
without direction and publishes an outcomes record (`doc/research/redesign-2026-09-27.md`). The
retail signal campaign stays on hold; a campaign built on the auditable record itself remains open.

**Preliminary result, 27 September 2026: fails as designed.** On 3,841 signalled days (from 15,849 recorded decisions) the
signalled direction is right 34 to 41 percent of the time at 5 to 21 days, below half in both halves of the
history. Volatility mean-reverts from the stretched states the classifier detects. Details and
the follow-up tests T1b to T1d: `doc/research/edge-test-2026-09-27.md`. The retail signal
campaign (T3) does not start until one of T1b to T1d passes on the held-out period.

- Backfill and classify the full VIX and OVX history available (FRED from 2011; decisions are
  computable once 1,260 prior closes exist, roughly from 2016), so that a decision exists for
  every trading day.
- For every day, record the forward change of the reference index at 1, 5, 10 and 21 NYSE
  trading days, using only later observations.
- Compare **Conditions met** days with all days and with days in the same regime state.
- Pass, per market, all three: at least 30 signal days; median absolute 5-day change on signal
  days at least 1.5 times the baseline median; hit rate (sign of change matches the combined
  score's sign) at least 60 percent with a bootstrap interval that excludes the baseline rate.
- The `/statistician` skill in this repository reviews the test design before the numbers are
  read. Results, sample sizes and the exact query are published on the record page whatever they
  show. Owner: engineer. Duration: days.

### T2: problem interviews (H2), in parallel, three weeks

- Ten primary customers who pass the §2 screen, three desk contacts. Sources: the communities in
  §2, existing contacts, direct messages to people who post about volatility trades.
- Script: how you trade volatility today; your last catalyst trade and what it cost; what you pay
  for now and why; what you would need to see on a record to trust it; what you would want in a
  daily brief; where you want it delivered.
- Pass: at least six of ten describe the timing problem without being led, and at least five pay
  for something today. Record every call. Owner: founder.

### T3: campaign test (H3), four weeks, starts when T1 passes

- Asset: the public record page from T1, a sample daily brief, and the landing page on
  `sites/markets/` with one call to action: get the daily brief.
- Channels: posts in the §2 communities where rules allow, X threads showing one historical
  event walked through the record, direct outreach to T2 interviewees and their referrals.
- Measure: visitors, sign-ups, opens of the first brief.
- Pass: at least 100 sign-ups and a visitor-to-sign-up rate of at least 3 percent. Kill: under
  30 sign-ups after four weeks and two message iterations.

### T4: founding pilot (H4), starts at 100 sign-ups

- Offer a private pilot to the list: daily brief plus setup alerts plus a private channel, at a
  founding price chosen after T2, locked for twelve months, capped at 50 members. Payment by a
  Stripe payment link; access granted by hand.
- Pass: at least 10 paying members within two weeks of the offer.

### T5: habit (H5), 30 days into the pilot

- Measure open rate of the brief and members still active at day 30.
- Pass: median member opens on at least 60 percent of trading days and at least 80 percent are
  still members at day 30.

Decision gates: T1 fail redirects the product to desks. T2 fail stops the retail track. T3 fail
iterates the message twice, then stops. T4 and T5 decide whether the full build in the build
brief §Part B proceeds.

## 5. The prototype: what exists, what must be designed and built

The prototype is the smallest system that lets T1, T3, T4 and T5 run. It is delivered by email
and Telegram, not by a hosted dashboard. Multi-tenancy, billing integration, options prices and
an API are not in it.

| Needed for the tests | Exists today | Must be designed and built |
| --- | --- | --- |
| Full-history decisions for VIX and OVX | Ingestion, classifier, decisions, replay | Backfill run and a check that every trading day has a decision |
| Forward outcomes and the edge statistics | Ledger, point-in-time queries | Outcomes computation, baseline comparison, bootstrap; a command that reproduces the figures |
| Public record page | Nothing public | Static page generated daily from the ledger and published with the product site; every figure with its sample size and query |
| Regime state in plain words | Severity and confidence | Thresholds mapping to Compressed, Normal, Stretched, Extreme; one-line reason text; versioned configuration |
| Daily brief | Nothing | Generated text per market: state, score, change since yesterday, decision, historical outcomes in this state, next known catalyst, link to evidence, disclosure block. Sent after FRED publishes the close, before the US open. Timing is itself measured in T3 |
| Delivery | Nothing | Telegram channel (one-way broadcast) and an email list; each send recorded in the ledger with the brief's hash |
| Sign-up | Nothing | Email capture on the landing page; list export for the pilot |
| Setup alerts for the pilot | Decisions | One named setup: regime change or **Conditions met**, with a checklist and one structure template (long straddle) illustrated for a stated risk budget, labelled illustrative |
| Verifiability | Hash chain, verify command | Public page explaining how a member checks a brief's hash against the ledger |
| Metrics | Nothing | Sign-ups, opens, channel members, per day, in a sheet or the ledger; no third-party trackers beyond the email provider's |
| Disclosures | Honesty copy in the UI | Disclosure block written with the lawyer: general information, not personal advice, full-premium loss possible, past results, methodology, timestamp |

Same-day data from a paid vendor, options chains, a hosted multi-tenant dashboard, Stripe
subscriptions and the API are deferred to Part B of the build brief and start only after T4.

## 6. The campaign, defined

- **Message:** "A volatility regime brief you can audit. Every call is timestamped and
  hash-chained before the market opens; here is what happened after every one of them."
- **Proof:** the record page and one worked historical event (5 February 2018, VIX 37.32).
- **Ask:** one email address, one click to the Telegram channel.
- **Where:** the communities in §2, X, direct outreach. Not paid ads until T4 passes.
- **When:** the day T1 passes and the record page is live. Four weeks.
- **Who:** the founder writes and posts; the engineer keeps the brief arriving every trading day
  without exception, because a missed day during the campaign is the test failing.

## 7. Risks in the test phase

| Risk | Effect | Handling |
| --- | --- | --- |
| T1 sample too small for OVX | Cannot pass per market | Run the campaign on VIX only; state it |
| FRED publishes after the US open on some days | Brief arrives late | Measure send time versus open; if late more than 20 percent of days, budget a same-day vendor in Part B |
| Redistributing index values | Licence exposure | The brief states levels as reported by FRED with attribution; no real-time values |
| Recommendation rules (EU and US) | The brief read as advice | Identical content for all; no personal sizing; disclosure block; one lawyer consultation before T3 |
| Founder time | Interviews and posts slip | T2 and T3 have owners and dates; the engineer does not do them |

## 8. Decisions taken now

1. T1 runs immediately; the engineer starts today from `doc/briefs/markets-v2-build-brief.md`
   Part A.
2. Primary customer is the self-directed volatility trader in §2. Desks are interviewed, not built
   for, until T4.
3. Prototype delivery is Telegram and email, no hosted dashboard, no billing integration.
4. No paid data until T4 passes; FRED timing is measured, not assumed.
5. Pricing is decided after T2 and before T4, not now.
6. One lawyer consultation is booked to land before T3 starts.
