# Pre-registration: four held-out tests of the volatility state

Date of registration: 27 September 2026. Written before any 2019+ outcome was computed for any
design below. This section is frozen at registration; results are appended below it by the test
runners and never edited into it.

## Protocol

This file follows `doc/research/recovery-plan-2026-09-27.md` section 2 and answers the diagnosis
in that note and in `doc/research/edge-test-2026-09-27.md` (a descriptive statistic given a
predictive meaning; a dislocation made from the market value it claims to disagree with; only
MARKET_DATA classified, so VIX was scored against itself; validation events that could not fail).

- **Fit window:** parameters are chosen on 2011-01-01 to 2018-12-31 only.
- **Holdout:** exactly ONE run on 2019-01-01 onward, at the frozen parameters. A second look at
  2019+ under any design is a new pre-registration with a new holdout.
- **Attempts counted:** every grid cell is an attempt; pass thresholds are Bonferroni-corrected
  for the number tried, as stated inside each design. Runners log every quantity computed on
  2019+ data, including any later discarded.
- **Tradable, not spot:** directional claims are measured on VX futures settlements (or the
  model-free variance premium against realized SPX volatility), never on the spot index alone.
- **Roles separated:** the agent that designed a rule does not run it and does not judge it. Two
  adversaries review design and result before anything is recorded as a pass.
- **Read-only evidence:** the ledger copy is read-only; engine and classifier code are untouched.
- **Publish either way:** negative and inconclusive results are recorded with the same tables and
  detail as a pass.
- **Sibling correction:** if several designs are judged on the same 2019+ holdout, the judges
  divide alpha further by the number of sibling tests where a design says so.

## Attempt count

| Design | Free parameters | Grid | Combinations evaluated | Bonferroni m used |
| --- | --- | --- | --- | --- |
| 1 vx-roll-carry-5d | theta | {0.125, 0.150, 0.175, 0.200, 0.250} | 5 | 5 |
| 2 catalyst-vx30-crush | j, k | {0,1,2,3} x {1,2,3,5}; legs separable | 8 marginal (16 joint) | 24 (6 hypotheses x 4) |
| 3 vrp-stretched-state-compressed-premium | q | {0.75, 0.80, 0.85} | 3 | 3 |
| 4 r2-magnitude-vx30-state-vs-realized-baseline | d, side (x 2 horizons, x 2 markets) | {0.35,0.40,0.45} x {high,low,both} x {5,21} x {VIX,OVX} | 36 | 18 per market (36 sensitivity) |
| **Total** | | | **52 evaluated (60 counting Design 2's joint grid)** | **68** |

Total parameter combinations across all designs: 52 (5 + 8 + 3 + 36). Counting Design 2's 4 x 4
joint grid instead of its 4 + 4 marginal evaluations gives 60. The sum of the correction
denominators the designs commit to is 68 (5 + 24 + 3 + 36).

## Disclosure of prior looks

An earlier aggregate look at full-period (2011-2026) spot index outcomes exists:
`doc/research/edge-test-2026-09-27.md`. It measured spot-direction hit rates of the shipped signed
score at 5 and 21 days, and, in item 4, the median absolute 5-day OVX move on signal days (2.84
points) against all days (2.14). That look used the whole sample, including 2019-2026, on spot
indices.

- It **weakly contaminates Design 4** (magnitude): the OVX absolute-move finding is the same
  question family (state predicts larger absolute moves), on a different target (spot points, not
  log VX30 relative to a realized baseline) and without a fit/holdout split. Design 4's judges
  should treat the grid as chosen with that aggregate knowledge in hand and weigh the 36-attempt
  correction (alpha 0.00139).
- Designs 1, 2 and 3 use different target families (futures roll P&L, event-window VX30 change,
  the log variance premium against realized SPX volatility); the earlier look is disclosed for
  them but does not count toward their m.

Each design also discloses its own predictor-only looks at the holdout (signal, gate and state
frequencies; no outcomes) in its Fit and Breaks-if sections.

Results will be appended below this section by the test runners without editing this section.

---

## Design 1: vx-roll-carry-5d (basis)

### Hypothesis

When the front VX monthly future's daily roll yield, roll_t = (settle of the held contract minus
spot VIX) divided by trading days to its expiry, is in the steep tail (|roll_t| >= theta), a
five-trading-day position against the basis (short the future in steep contango, long it in steep
backwardation) earns positive net P&L after costs in 2019-2026, because spot VIX on average moves
less toward the future than the curve prices over the next five days. This is a risk-premium
harvest conditioned on steepness (Simon-Campasano 2014, Johnson 2017), not a mispricing claim;
the product state it would justify is "carry regime", not "vol expansion".

Secondary, pre-registered null: the classifier's level-rank regime (high_vol = VIX one-sided
percentile > 0.70 against the prior 1,260 closes) adds nothing when used to block short entries
(Aragon-Mehra-Wahal 2018). Design finding recorded now: in 2011-2018 the regime gate binds on 1 of
57 short signals at theta = 0.15, so the basis sign already carries the regime; the classifier
state is redundant with the term structure as a filter.

### Target

Five-trading-day settle-to-settle change, in VIX points, of one specific VX monthly futures
contract (the contract held at entry; never a spot index, never a rolled or interpolated series
inside a trade), signed by bet direction and net of 0.10 points round-trip cost (0.05 half-spread
per side). One point = USD 1,000 per contract. Trades are non-overlapping, so each trade is a
distinct bet.

### Features

- roll_t = (S_t(C_t) - VIX_t) / TD_t, where C_t is the monthly contract held on date t: F1 if
  f1_days > 14 calendar days, else F2 (vx_curve.csv f1_days, f1_settle, f2_settle; settles
  confirmed from vx_futures.csv by (trade_date, expiry), contract_type M only).
- TD_t = number of VX trading dates strictly after t and strictly before C_t's expiry (from the
  vx_curve.csv trade-date calendar).
- VIX_t = ledger VIX close (vix.csv), cross-checked against Cboe VIX_History.csv.
- Sign of roll_t (contango +, backwardation -) sets the bet direction: short if positive, long if
  negative.
- Secondary only: equity regime from decisions.csv (regime column, cls_dislocations) on date t;
  high_vol = VIX one-sided percentile > 0.70 against prior 1,260 closes. Used solely for the
  pre-registered gated comparison, not in the primary rule.
- Secondary, descriptive only: VX30 (vx_curve.csv) 21-trading-day forward change conditional on
  the signal; basis F1 - VIX and slope F2 - F1 on signal dates, for the report.

### Rule

Calendar: VX trading dates in vx_curve.csv that also have a VIX close (holiday-session VIX rows
with no VX settlement are excluded).

At the close of date t compute roll_t. Signal: SHORT if roll_t >= theta; LONG if
roll_t <= -theta; otherwise none.

Execution: enter at the settlement of t+1 (one trading day after the signal, to remove the VIX
16:15 close versus VX 16:00 settlement timing gap and any FRED one-day lag) in contract C_{t+1},
the held contract on the entry date; exit at the settlement of t+6 (five VX trading days after
entry) in the SAME contract, no roll inside the trade (C_{t+1} always has >= 9 trading days to
expiry, so this is always feasible). Trade P&L = dir x (S_{t+6}(C) - S_{t+1}(C)) - 0.10, with
dir = -1 for SHORT and +1 for LONG. Non-overlapping: while a trade is open no new signal is taken;
the next signal check is at the close of the exit day t+6. One contract per trade, no compounding,
no sizing.

FREE PARAMETER 1 (the only one): theta in VIX points per trading day, grid
{0.125, 0.150, 0.175, 0.200, 0.250} (5 attempts). Fixed by pre-registration, not free: hold h = 5
trading days (Simon-Campasano 2014), entry lag 1 day, held-contract rule f1_days > 14, cost 0.10
per round trip, no regime gate, symmetric theta for both legs.

Fit-window eligibility (predictor-only, computed on 2011-2018): a cell is eligible only if
days-in-market <= 30 percent of fit trading days (DEC-002's own idle criterion) and
n_fit_trades >= 30. Designer's predictor-only probe: theta = 0.125 gives 32.1 percent exposure and
129 fit trades (ineligible on exposure); 0.150: 19.9 percent, 80 trades; 0.175: 11.9 percent, 48;
0.200: 7.7 percent, 31; 0.250: 2.5 percent, 10 trades (ineligible on n). Expected eligible set:
{0.150, 0.175, 0.200}.

### Fit

Fit window: signal dates 2011-01-03 to 2018-12-31 with exit dates <= 2018-12-31 (trades that
would exit in 2019 are dropped, not carried). For each of the 5 grid values compute: n trades,
short/long split, days-in-market share, mean net P&L per trade, its trade-level standard deviation
and t-statistic (mean / (sd / sqrt(n))), hit rate, median, worst trade. Apply the eligibility
filter (exposure <= 0.30, n >= 30). Among eligible cells select the theta with the highest
fit-window t-statistic of mean net P&L per trade; on a tie choose the lower theta (more trades).

Record the selected theta, the fit table for all 5 cells, and SHA-256 hashes of vx_curve.csv,
vx_futures.csv, vix.csv and decisions.csv with a timestamp BEFORE any 2019+ outcome is computed.
Then ONE run of the selected theta on signal dates 2019-01-01 to the last date with a valid exit
(<= 2026-09-22, the ledger VIX end).

Attempt count: 5 grid cells (one free parameter); the fixed choices (h = 5, lag = 1,
f1_days > 14, cost 0.10, no gate) are pre-registered from prior art and were not varied. The
secondary variants (gated, lag 0, cost 0.20, per leg, 21-day VX30 change, all 5 cells on the
holdout, always-short benchmark, unconditional contango-short, per-year, ex-Mar-2020 and
ex-Aug-2024) are computed AFTER the primary result is recorded, are reported in an appendix, and
cannot produce a pass; any of them used to claim a pass requires a new pre-registration and a new
holdout. If several sibling pre-registrations (other angles from this workflow) are judged on the
same 2019+ holdout, the judge divides alpha further by the number of sibling tests (family-wise
0.05 / (5 x k)).

### Holdout metrics

Primary:

- Mean net P&L per trade in VIX points (bet-signed, after 0.10 cost) on the selected theta,
  2019-01-01 onward, with n trades.
- One-sided 99 percent lower confidence bound of the mean from (a) trade-level iid bootstrap,
  10,000 resamples, and (b) circular block bootstrap of the daily strategy P&L series
  (position x daily settle change, zero when flat) with 63-trading-day blocks, 10,000 resamples,
  mean per trade = total P&L / n.
- Mean net P&L per trade excluding the single best trade (point estimate).
- Placebo: 1,000 circular shifts of the holdout signal series by a uniformly random 63 to 252
  trading days, same execution rules; percentile of the real mean within the placebo
  distribution.
- Hit rate with exact binomial 95 percent interval; median net P&L; Sortino ratio on trade P&L
  (downside deviation, MAR = 0); worst trade; largest 63-day drawdown of cumulative P&L.
- Minimum detectable effect stated from the realized per-trade sd: MDE = 2.326 x sd / sqrt(n);
  with n about 80 and sd about 2 points this is about 0.5 points (USD 500 per contract per
  trade), roughly Simon-Campasano's in-sample short mean, so power against a decayed effect is
  low and must be stated.

Secondary (appendix, no pass consequence):

- Short leg and long leg separately.
- Gated variant (no short entry when regime = high_vol) and the paired difference gated minus
  ungated with a bootstrap interval.
- Lag-0 entry; cost 0.20 round trip.
- Always-short-front-contract 5-day P&L on all days as benchmark; unconditional contango-short
  (theta = 0, basis > 0).
- All 5 grid cells on the holdout; per calendar year; excluding 2020-02-24 to 2020-04-30 and
  excluding 2024-08-01 to 2024-08-16.
- VX30 21-trading-day forward change on signal dates versus all dates (descriptive, the 21-day
  view requested).
- Sign-run episodes and exposure share in the holdout, so the result is stated per independent
  trade, not per day.

### Pass

Correction: Bonferroni over the 5 grid cells, one-sided alpha = 0.05 / 5 = 0.01 (z = 2.326); the
one-sided direction is fixed in advance (positive net P&L in the bet direction).

PASS requires ALL of:

1. n holdout trades >= 30 (otherwise inconclusive by design, not a fail and not a pass);
2. the one-sided 99 percent lower bound of mean net P&L per trade is > 0 under BOTH the
   trade-level bootstrap and the 63-day block bootstrap (equivalently one-sided p < 0.01 in
   both);
3. mean net P&L per trade excluding the single best trade is > 0;
4. the real mean is at or above the 99th percentile of the 1,000 placebo-shift means.

Economic floor implied by (2) at the expected n of about 80 and sd of about 2 points: mean >=
roughly 0.5 points per trade, about USD 500 per contract per 5-day trade, or about USD 8,000 per
contract per year at 20 percent exposure, before margin and slippage beyond 0.05 per side. If
sibling pre-registrations are judged on the same holdout, alpha for (2) becomes 0.05 / (5 x k)
and the placebo percentile 100 x (1 - 0.05 / (5 x k)).

### Kill

KILL (rule retired, no redesign of this hypothesis on the 2019+ data) if any of:

- (a) holdout mean net P&L per trade <= 0.10 points (the rule does not cover one more round-trip
  spread);
- (b) mean excluding the single best trade <= 0;
- (c) the real mean is at or below the 50th percentile of the placebo distribution.

INCONCLUSIVE (not deployable, counts as a negative result for the product, a new hypothesis needs
a new pre-registration) if neither PASS nor KILL, for example a positive point estimate with a 99
percent lower bound below zero, or n < 30. There is no second run on 2019+ with another theta,
horizon, lag, cost or gate; the appendix variants cannot rescue a KILL or an INCONCLUSIVE.

### Data

Scratchpad root: `C:\Users\Radu\AppData\Local\Temp\claude\d--Work-primescore-markets\1c100b72-59e9-448b-a615-aa9b145c355c\scratchpad`.

- `data\vx_curve.csv` (trade-date calendar, f1_expiry, f1_days, f1_settle, f2_expiry, f2_settle,
  vx30, vx30_method).
- `data\vx_futures.csv` (monthly rows only; settle by (trade_date, expiry) for the held contract
  on entry and exit dates; source column to flag legacy 2011-May 2013 rows).
- `data\vix.csv` (ledger VIX close); cross-check against
  https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv and report any date
  where the two differ by more than 0.01.
- `data\decisions.csv` (context = equity, regime column) for the secondary gated comparison only.
- `data\README.md` and `vx_build_report.json` (roll convention, dropped zero-settle rows, the
  2013-05-28 legacy/CDN mismatch, the 32 holiday VIX dates without VX settlement).
- Designer's predictor-only probes for reproduction of the grid rationale: `design_probe.py` and
  `design_probe2.py` (no P&L computed).
- Not needed: SPX, OVX, FOMC and CPI dates, option prices.

### Prior art

- Simon and Campasano, "The VIX Futures Basis: Evidence and Trading Strategies", Journal of
  Derivatives 21(3), 2014 (https://jod.pm-research.com/content/21/3/54.abstract): daily roll =
  (front future - VIX) / business days to settlement; short when roll > +0.10, long when < -0.10,
  front contract with >= 10 business days to settlement, 5-day hold, 2006-2011: mean USD 539 per
  short trade and USD 908 per long trade, significant, reported with Sortino because P&L is
  non-normal; the basis predicts futures returns, not spot VIX changes.
- Quantpedia replication (https://quantpedia.com/strategies/exploiting-term-structure-of-vix-futures):
  19.7 percent p.a. in-sample, "OOS back-test shows slightly negative performance ... alpha is
  deteriorating", which is the main reason to expect a fail and why theta is re-fitted on
  2011-2018 rather than taken as 0.10 (which would also breach the 30 percent exposure cap: it
  fires on 37 percent of 2011-2018 days).
- Johnson, "Risk Premia and the VIX Term Structure", JFQA 52(6), 2017
  (https://www.travislakejohnson.com/pdfs/Johnson%20VIXTS%202017%20(JFQA).pdf): the slope of the
  VIX term structure predicts excess returns of VIX futures at all maturities; expectations
  hypothesis rejected; slope-alone beats the unrestricted model out of sample in 33 of 36
  regressions.
- Cheng, "The VIX Premium", RFS 32(1), 2019
  (https://utoronto.scholaris.ca/bitstreams/682f3c99-5d7d-4dee-b4b4-dafa83785523/download): the
  ex-ante premium in front VIX futures falls or turns negative when risk measures rise; a
  premium-sign filter raised the short-only Sharpe from 0.57 to 0.87 (2004-2015). The filter that
  has evidence is on the estimated premium, not on a level rank, which is why the classifier's
  regime is a secondary comparison rather than part of the primary rule.
- Aragon, Mehra and Wahal, NBER w24575, 2018
  (https://www.nber.org/system/files/working_papers/w24575/revisions/w24575.rev0.pdf): the VIX
  level and demeaned level have no predictive power for VIX futures returns 2004-2017 (all t < 2,
  adjusted R2 < 1 percent), the basis for the secondary null that the classifier's level rank
  adds nothing.
- Eraker and Wu (2006-2013), Cheng (2004-2015) and Hu and Jacobs (JFQA, sample to 2022,
  https://jfqa.org/wp-content/uploads/2026/02/Expected__Realized_Returns_on_Volatility.pdf):
  unconditional long front-month VIX futures earn -3.3 to -3.7 percent per month, i.e. the
  contango carry is a known, openly priced insurance premium whose payoff distribution includes
  February 2018 and March 2020.
- Nossman and Wilhelmsson, J. Alternative Investments 12(2), 2009: futures forecast the direction
  of spot well once a risk premium is removed, consistent with the rule being a premium harvest
  rather than a spot forecast.
- Internal: `doc/research/edge-test-2026-09-27.md` (spot hit rates 35-41 percent for the
  level-rank sign) and `doc/research/recovery-plan-2026-09-27.md` section 2 (protocol this design
  follows).

### Breaks if

- **Look-ahead and timing.** VIX's cash close is 16:15 ET while VX daily settlement is at 16:00
  ET, and the ledger VIX (FRED VIXCLS) can lag Cboe by a day; the one-day entry lag is the
  mitigation, and a lag-0 result that is materially better than lag-1 would indicate the signal
  is partly timing leakage rather than carry.
- **Roll effects.** The held-contract rule (f1_days > 14) and the same-contract hold avoid
  intra-trade rolls, but an error in f1_days, in the expiry dates (seven holiday-shifted
  Tuesdays), or in the trading-day count TD_t changes roll_t near expiry where the denominator is
  small; a settle taken from the wrong contract on exit would silently add the roll spread to
  P&L, so settles must be pulled by (trade_date, expiry) from vx_futures.csv, not from the
  curve's F1/F2 columns.
- **Data errors.** 2011 to May 2013 settles come from Internet Archive copies of legacy Cboe
  files with 0.05 increments and one known bad value (2013-05-28 Feb-2014 contract); dropped
  zero-settle rows can create gaps that shift the exit date; the 32 holiday-session VIX rows have
  no VX settlement and must not enter the calendar; a VIX cross-check against Cboe's
  VIX_History.csv is required.
- **Costs and capacity.** 0.05 per side is a calm-market spread; in March 2020 and August 2024
  front VX spreads were several ticks and settlement prices were not executable at the settle, so
  a pass that depends on trades entered or exited in those weeks is fragile (hence the
  ex-Mar-2020 and ex-Aug-2024 appendix rows and the exclude-best-trade condition).
- **One event decides.** With about 80 trades over 7.7 years and heavy-tailed P&L, a single
  10-point adverse move on a short (2020-02-24 to 03-18, 2024-08-05, 2025-04-04) or a single
  large win can move the mean by more than the MDE; the block bootstrap and placebo partly
  address this but cannot create power that is not there.
- **Structural change.** Post-2015 ETP flows (Asensio; Nielsen-Posselt) and the 2018 XIV
  collapse changed who supplies contango, so a 2011-2018 fit can select a theta that fires in a
  different regime mix in 2019-2026 (the classifier's high_vol regime co-occurs with steep
  contango on most 2019+ short signals but on almost none in 2011-2018, per the predictor-only
  probe; this is disclosed because it is the one predictor-side property of the holdout the
  designer saw).
- **Designer contamination.** The designer computed no 2019+ outcome, but did count 2019+ signal
  and gate frequencies to confirm n >= 30 was feasible; the judges should treat that as the only
  look taken.
- **Interpretation.** A pass shows a harvestable, selectivity-conditioned carry premium, not
  mispricing and not a forecast of spot VIX; a claim beyond that (for example that the
  classifier's severity sign predicts anything) is not supported by this test and must not be
  attached to it.

---

## Design 2: catalyst-vx30-crush (catalyst)

### Hypothesis

The product's original thesis in its cheapest honest form: scheduled macro catalysts (FOMC
decisions, CPI releases) resolve uncertainty that the front of the VIX futures curve carries into
the event.

- Primary (H1): the 30-day constant-maturity VIX future (VX30) falls from the close before a
  scheduled FOMC/CPI event to the close j trading days after it by MORE than on locally matched
  non-event windows (an event-day crush that a short F1/F2 position captures).
- Secondary, same family: (H2) VX30 rises from k days before the event to the close before it
  more than on matched windows (pre-event build); (H3) magnitude: |change| over the post window
  is larger than on matched windows (what a long-volatility structure needs); (H4) the
  classifier's descriptive regime at entry (high_vol vs not, cfg v2 percentile buckets 0.3/0.7,
  not refitted) modulates the crush, with a larger crush in points from high_vol entries;
  (H5/H6) H1 on FOMC alone and CPI alone.

Published evidence says the spot VIX crush is real but faded after 2015 and the front future
moves about half as much, so the prior odds of H1 passing at the corrected threshold are
LOW-TO-MODERATE and the test is expected to be underpowered for a literature-sized effect (stated
below). The test is designed so that a negative result is informative: if H1, H2 and H3 all fail,
the catalyst thesis is refuted for everything that can be traded with VX futures and free data,
and what remains of R3 requires option prices.

### Target

Primary: change in VX30 (constant 30-day-maturity VIX futures level, linear interpolation in
calendar days between adjacent monthly VX settlements, from vx_curve.csv) in index points from the
settlement on trading day t-1 to the settlement on t+j, where t is a scheduled FOMC decision day
or a verified CPI release day on the VX trading calendar. VX30's change approximates the P&L of a
duration-weighted long F1/F2 futures position (both tradable) and avoids in-window contract
changes, which matter here because 36 of 125 FOMC decision days ARE VX expiry days and 71 of 125
have an expiry inside [t-5, t+3].

Strictly tradable check reported alongside every VX30 number: settle change of the single monthly
contract that is nearest to expiry among those expiring strictly more than 7 calendar days after
the window's last day (so no roll inside the window), in points, plus the same net of a
0.10-point round-trip cost (two crossings of the 0.05 tick).

Pre-window target (H2): VX30 change from settle t-k to settle t-1. Magnitude target (H3): |VX30
change| over the post window. Both FOMC and CPI prints occur before the VX daily settlement
(14:00 ET and 08:30 ET respectively), so the t settlement contains the event and the t-1
settlement does not.

### Features

- Event calendar: scheduled FOMC decision dates (fomc_dates.csv, kind = meeting,
  status = verified_statement; unscheduled meetings, conference calls and notation votes
  EXCLUDED) and CPI release dates (cpi_dates.csv, best_release_date, verification containing
  verified_bls_and_fred; October 2025 unverified row excluded). Event day must be a VX trading day
  (vx_curve.csv row exists); otherwise dropped and listed (2017-04-14, 2020-04-10). Same-day
  FOMC+CPI counts once, tagged both.
- VX30 from vx_curve.csv (vx30, vx30_method); F1/F2/F3 settles and expiries from vx_futures.csv
  monthly contracts (contract_type = M) for the strictly tradable check; vx30_method flag
  retained so extrapolated post-roll dates can be reported separately.
- Classifier state at the entry close (t-1 for the post leg, t-k for the pre leg): the engine's
  descriptive regime bucket (decisions.csv regime for context = equity, i.e. one-sided
  regime_percentile of VIX against its prior 1,260 closes, low_vol < 0.30 / normal /
  high_vol > 0.70 as fixed in cfg_versions v2). Used as a feature only, no refit of its
  thresholds. Holdout counts at t-1: high_vol 53, normal 65, low_vol 34.
- Descriptive covariates recorded per event, no pass claim attached: basis at entry
  (f1_minus_vix from vx_curve.csv), calendar days to F1 expiry at entry, event type, year,
  whether the CPI release date differed from the originally scheduled date
  (earlier_scheduled_dates non-empty: 4 cases).
- Placebo comparison set: for each real event, non-event windows of identical length shifted by
  an offset of 6 to 15 trading days in either direction, excluding offsets whose window touches
  [event-3, event+3] of any scheduled FOMC/CPI event. This is the "unconditional days" baseline
  matched on local volatility level and contango drift.

### Rule

Two free parameters, both window lengths, nothing else.

- Post leg (H1, H3, H4, H5, H6): SHORT VX30 (in practice short duration-weighted F1/F2, or short
  the surviving contract) at the settlement on trading day t-1 before a scheduled FOMC or CPI
  event, COVER at the settlement on t+j, j in the grid {0, 1, 2, 3} (j = 0 means cover on the
  event day).
- Pre leg (H2): LONG VX30 at the settlement on t-k, SELL at the settlement on t-1, k in the grid
  {1, 2, 3, 5}.

Statistic for each leg: excess = mean(event-window change) - mean(placebo-window change over the
offset set), in points. Regime buckets, the placebo offset range (6-15 trading days), the
7-calendar-day contract survival rule and the cost figure (0.10 points round trip) are fixed here
and are not tuned. No threshold on VIX level, basis or score is fitted anywhere.

### Fit

Fit window 2011-01-01 to 2018-12-31 only: 64 FOMC + 93 CPI events on VX trading days, 151
distinct event days (6 same-day pairs). For each j in {0,1,2,3} compute the pooled excess
post-window VX30 change and its placebo z (excess divided by the standard deviation of the
placebo-mean distribution, 10,000 random offset draws); choose j* = the j with the most negative
z. For each k in {1,2,3,5} compute the pooled excess pre-window change; choose k* = the k with the
most positive z. That is 8 in-sample evaluations (the legs are separable, so the 4 x 4 joint grid
collapses to 4 + 4 marginal evaluations; the correction below still uses 4 grid points per
hypothesis). All 8 in-sample values (excess, z, n) are written into the pre-registration file
together with j* and k* BEFORE any 2019+ number is computed.

Nothing else is fitted: regime buckets come from cfg v2, the placebo offset range, contract rule
and cost floor are fixed in this design. The 2019-01-01 onward period is touched exactly once,
with j* and k* frozen; a second look is a new pre-registration. The agent that runs the fit and
holdout is not this designer, and the two adversaries judge; the runner records every computation
it makes on the holdout, including any it later discards.

Holdout event counts known in advance without looking at outcomes: 61 FOMC, 91 CPI, 149 distinct
pooled event days (2020-04-10 CPI falls on Good Friday with no VX settlement and is dropped;
2019-12-11, 2020-06-10, 2024-06-12 are FOMC+CPI same-day).

Minimum detectable effect, stated now: with n = 149, holdout sd of a 2-day VX30 change assumed
1.5 points (fit-window sd is 1.20; 2020 will widen it), SE about 0.12 points; at the corrected
one-sided alpha 0.0021 with 80 percent power the detectable excess is about 0.45 points (about
2.5 percent of a VX30 near 18). The literature FOMC effect on the front future (about 1.4 percent,
roughly 0.25 points) would be detected with roughly 35 percent power. A literature-sized effect is
therefore EXPECTED to land in the inconclusive zone, and that is recorded before the run so it
cannot be reframed afterwards.

### Holdout metrics

- Per hypothesis (H1 pooled post, H2 pooled pre, H3 pooled magnitude, H4 regime contrast, H5
  FOMC post, H6 CPI post): n distinct event days; mean and median VX30 change in points; placebo
  mean; excess; one-sided placebo p from 10,000 offset draws; 95 percent interval from 10,000
  bootstrap resamples of events; hit rate (share of events with the hypothesised sign) against
  placebo hit rate.
- H1 strictly tradable version: mean and median change of the surviving-contract F1
  (7-calendar-day rule), gross and net of 0.10 points round-trip cost; Sortino-style ratio
  (mean / downside deviation) rather than Sharpe because the distribution is non-normal; worst
  single event; per-year table 2019-2026; breakdown by calendar days to expiry at entry (<= 10,
  11-20, > 20).
- Robustness, all reported: H1 excluding the single largest |change| event and excluding the top
  3; H1 with vx30_method = extrapolation dates excluded; H1 in log points (log VX30 change) as a
  heteroskedasticity check; H1 restricted to events whose CPI release date was never rescheduled.
- H3 magnitude: median and mean |VX30 change| over the post window on events vs placebo, ratio,
  one-sided placebo p; same for the pre window with k*; and the median |F1 change| for the
  surviving contract.
- H4 state: excess post-window change by regime bucket at t-1 (high_vol n about 53, normal about
  65, low_vol about 34), difference high_vol minus non-high_vol with a label-permutation p
  (10,000 permutations), and the same in log points. Reported even if no bucket passes.
- Descriptive only, no pass claim: excess by basis sign at entry (f1_minus_vix > 0 vs <= 0),
  excess in 2019-2021 vs 2022-2026 (post-pandemic CPI regime), and the count of sign-run
  episodes (events are already discrete, so this is the number of years with a negative pooled
  mean, out of 8).
- Attempt log: the runner lists every quantity computed on 2019+ data, including robustness
  variants, so the adversaries can verify m.

### Pass

Correction: Bonferroni over m = 24 = 6 holdout hypotheses (H1-H6) x 4 grid points per hypothesis
family, one-sided alpha_adj = 0.05 / 24 = 0.00208 (normal z 2.86). The grid is resolved on the
fit window, so the holdout p-value for the frozen j*, k* would be valid at m = 6 (alpha 0.0083);
the stricter m = 24 is adopted deliberately because the founder's rule counts every rule tried,
and it is stated here so it cannot be relaxed after the run. Robustness variants are not separate
hypotheses and cannot produce a pass on their own.

H1 PASSES only if all four hold on the 149 pooled events:

- (a) excess VX30 change <= -0.15 points;
- (b) one-sided placebo p <= 0.00208;
- (c) the raw (not excess) mean change of the strictly tradable surviving-contract F1 <= -0.20
  points, i.e. positive after the 0.10-point round-trip cost with a further 0.10 margin;
- (d) the sign of the excess is preserved after removing the 3 events with the largest |change|.

H2 passes on the mirror conditions (excess >= +0.15, p <= 0.00208, raw tradable mean >= +0.20,
sign preserved without top-3). H3 passes if the median |VX30 change| on events is >= 1.25 x the
placebo median with placebo p <= 0.00208 (magnitude is not tradable with free data, so a pass
here is labelled "state predicts movement", not edge). H4 passes only if H1 has not been killed
and the high_vol minus non-high_vol excess difference has permutation p <= 0.00208 with n >= 30
in the high_vol bucket. H5/H6 pass on the H1 conditions applied to their subsample with n >= 30
(61 and 91 expected). Any subgroup with n < 30 is inconclusive by design and no claim is made.

A pass on any hypothesis is still reviewed by both adversaries and is recorded as "passed one
held-out test at n = 149", not as a validated product signal.

### Kill

H1 is KILLED (recorded as refuted at this n) if any one holds on the pooled holdout:

- (a) excess VX30 change >= 0 (wrong sign);
- (b) one-sided placebo p > 0.10;
- (c) the excess changes sign when the single largest |change| event is removed;
- (d) the raw tradable F1 mean change > -0.10 points (does not cover one round-trip cost).

Same mirrored rules for H2. H3 killed if the event median |change| is <= the placebo median or
p > 0.10. H4 killed if H1 is killed, or if the high_vol bucket's excess is not more negative than
the non-high_vol bucket's.

Results between kill and pass are recorded as INCONCLUSIVE with the achieved effect and its
interval, and the pre-computed MDE is quoted next to them; an inconclusive result does not
authorise a second holdout look, a different j, a different offset range or a different cost
assumption.

Product-level kill: if H1 and H2 are both killed and H3 does not pass, the scheduled-catalyst
thesis is refuted for VX futures with free data and the remaining version of R3 (mispricing
against option quotes) is the only one left, requiring paid data; this is written as an ADR
either way.

### Data

Scratchpad root: `C:\Users\Radu\AppData\Local\Temp\claude\d--Work-primescore-markets\1c100b72-59e9-448b-a615-aa9b145c355c\scratchpad`.

- `data\vx_curve.csv` (VX30, vx30_method, f1/f2 settles and expiries, f1_minus_vix; 2,014 fit
  rows, 1,945 holdout rows; VX trading calendar is this file's trade_date set).
- `data\vx_futures.csv` (monthly contracts only, contract_type = M, for the surviving-contract
  tradable check; settle = 0 rows already dropped; 2011 to May 2013 settles come from Internet
  Archive copies of legacy CFE files and affect the fit window only).
- `data\fomc_dates.csv` (kind = meeting, scheduled = True, status = verified_statement; 64 fit,
  61 holdout).
- `data\cpi_dates.csv` (best_release_date with verification containing verified_bls_and_fred;
  earlier_scheduled_dates for the rescheduling robustness split; 93 fit, 91 holdout after
  dropping 2020-04-10).
- `data\decisions.csv` (context = equity, regime column, last decision per NY date; regime at the
  entry close is the classifier feature; equity dates 2021-04-02 and 2021-12-24 lack a VIX row
  and are handled by using the last available regime on or before the entry date, flagged).
- `data\vix.csv` (only for the basis covariate and for documenting the 33 holiday-session rows
  that have no VX settlement; the VX calendar, not the VIX row count, drives every window).
- No SPX, OVX, FRED macro or option data is required. VVIX_History.csv from Cboe would be needed
  only for a later version of H3 against priced vol-of-vol and is out of scope here.

### Prior art

- Nikkinen and Sahlstrom (International Review of Financial Analysis 13, 2004): implied
  volatility (VXO) rises before and falls after FOMC and CPI/PPI/employment releases, 1995-1999.
- CXO Advisory extension on 1994-2012 daily data: average VIX decline on release day of 1.2-2.0
  percent for CPI and about 3 percent on FOMC days with R-squared about 0 against the surprise
  sign, i.e. uncertainty relief independent of news direction; they flag data snooping and small
  samples for the VXX version.
- Fernandez-Perez, Frijns and Tourani-Rad (AUT working paper, intraday 1996-2013): VIX falls
  about 3 percent on FOMC days, mostly within 45 minutes of the statement, but the nearest VX
  future falls only about 1.4 percent; a short-front-future open-to-close on FOMC days averaged
  about 125 bp per event before costs.
- Lucca and Moench (Journal of Finance 70(1), 2015, Table 11): part of the pre-FOMC equity drift
  is explained by implied volatility falling into the meeting.
- "The disappearing pre-FOMC announcement drift" (Finance Research Letters 2020, sample to Dec
  2019): mean log VIX change on press-conference FOMC days -6.5 percent before Dec 2015 and -1.3
  percent (insignificant) after, so the fit window (2011-2018) straddles the attenuation and the
  holdout is entirely in the weak regime.
- Bernile, Hu and Tang (Journal of Financial Economics 121(3), 2016): abnormal E-mini and
  VIX-futures order flow in the 30 minutes before FOMC/CPI releases, consistent with
  pre-positioning, which would shift any effect to before the t-1 close.
- Simon and Campasano (Journal of Derivatives 21(3), 2014): the front-future roll predicts
  futures returns and they require at least 10 business days to settlement to avoid the
  convergence artefact, which motivates the surviving-contract rule and the VX30 primary target
  here.
- Aragon, Mehra and Wahal (NBER w24575, 2018): the VIX level does not predict VX futures
  returns, 2004-2017, which is why the classifier's regime enters only as a modulating state (H4)
  and never as a directional trigger.
- Cheng (Review of Financial Studies 32(1), 2019): the VIX futures premium collapses when risk
  rises, a warning that high_vol entries are where the short leg loses most, so H4's stated
  direction (larger crush from high_vol in points) is a level-scaling expectation, not a premium
  expectation, and either sign is informative.

Net reading of the prior art: the crush exists on spot, is roughly halved on the front future,
faded after 2015, and has never been shown to clear costs out of sample on a public dataset; the
odds of H1 passing at alpha 0.0021 are low to moderate, of H3 passing moderate
(near-mechanical), of H4 low.

### Breaks if

Look-ahead:

1. An event date entered from a schedule revised after the fact; cpi_dates.csv records 4
   rescheduled releases (Oct-2013 shutdown, Dec-2017, Sept-2025, Nov-2025), so the robustness
   split on never-rescheduled events must be reported and the Oct-2025 unverified row excluded.
2. The classifier regime at t-1 must be read from the decision recorded on or before t-1, never
   the next day's row.
3. FOMC statement time moved from 14:15 to 14:00 ET in 2013 and VX settlement is at or after
   16:00 ET throughout, so the t settlement always contains the decision; if any pre-2013
   settlement time were earlier than the statement the fit window, not the holdout, would be
   affected.
4. Placebo offsets must exclude windows touching any scheduled event, otherwise the baseline is
   contaminated with other events and the excess is biased toward zero.

Roll effects:

5. VX30's interpolation weights shift one day per day, so a contango slope adds a drift of about
   0.03 points per day to every window; it is common to event and placebo windows and nets out
   in the excess, but the raw tradable F1 mean used in pass condition (c) does include roll-down,
   which in contango favours the short leg; the days-to-expiry breakdown and the 7-calendar-day
   survival rule exist to show whether the F1 result is convergence rather than crush.
6. 195 post-roll dates have VX30 extrapolated; the variant excluding them must be shown.
7. FOMC Wednesdays coincide with VX expiries on 36 of 125 events, so the surviving contract at
   those events is the second month with lower VIX sensitivity, which mixes maturities in the F1
   check; VX30 is the primary precisely to avoid this.

Data errors:

8. Fit-window settles before May 2013 come from Wayback copies of legacy CFE files, one
   settlement was overridden (2013-05-28, Feb-2014) and 0.05 tick increments apply; an error
   there moves j*/k* but not the holdout.
9. FRED's VIX lags Cboe by a day on some dates, which affects only the basis covariate and the
   classifier state, not the target.
10. The holdout contains March 2020 (VX30 above 60), August 2024 and April 2025, and a single
    event window inside those episodes can dominate a 149-event mean, which is why the
    top-1/top-3 exclusions are pass and kill conditions rather than footnotes.
11. 2020's CPI on 2020-04-10 and the cancelled March 17-18 FOMC are absent by construction and
    the unscheduled 2020-03-15 meeting is excluded as unscheduled, so the holdout deliberately
    does not contain the largest volatility event as an "event day".

Design limits:

12. The test is underpowered for a literature-sized effect (MDE about 0.45 points at 80 percent
    power vs about 0.25 published), so an inconclusive result is the most likely outcome and is
    pre-declared as not a pass.
13. The placebo p treats events as exchangeable with offsets of 6-15 trading days, which fails if
    the volatility regime changes within that range (2020-03 to 2020-04), so the log-point variant
    is reported.
14. A pass says only that VX30 fell more than baseline around 149 scheduled events in 2019-2026
    at the stated cost assumption; it says nothing about SPX or CL option prices, straddle P&L or
    OVX, none of which are tradable or measurable with the free data listed.

---

## Design 3: vrp-stretched-state-compressed-premium (vrp)

### Hypothesis

When the classifier's stretched state is on (today's VIX close sits in the upper tail of the
one-sided ECDF of the prior 1,260 closes, i.e. the engine's own regime_percentile), the one-month
variance risk premium that an SPX variance seller collects, measured scale-free as
ln(VIX_t / RV_{t+1..t+21}), is SMALLER than on other days, and realized volatility exceeds implied
more often. In words: the stretched state marks when implied volatility is LEAST over-priced. It
does not claim vol is under-priced (the mean log premium stayed positive in every state in
2011-2018), and it is the opposite of both readings the product has carried so far ("stretched =
vol expansion, buy vol" was refuted on spot; "stretched = overshoot, sell vol" is the naive
mean-reversion reading).

Direction is pre-specified from Cheng (RFS 2019: the volatility premium collapses when ex-ante
risk rises) and Bollerslev-Todorov (JF 2011: the premium is jump-tail compensation that is paid
out in stress), and is consistent with the fit window: on non-overlapping 21-day windows
2011-2018, mean log premium 0.17-0.21 on stretched events versus 0.29-0.31 otherwise; P(RV > VIX)
0.28-0.31 versus 0.14-0.17.

Economic content if it passes: a stand-aside filter for anyone harvesting the SPX variance
premium (short variance swaps, short delta-hedged straddles, short iron butterflies) and the least
expensive entry, relative to the average premium paid, for a defined-risk long-variance position
(long 30-day ATM SPX straddle). Trading it needs SPX options or variance swaps, which are not
free data; this test measures the model-free premium (the variance-swap payoff), so a pass is
necessary but not sufficient for an option strategy whose P&L is path dependent. If it kills, the
classifier's regime statistic is confirmed to be descriptive only for this target too, and the
product should stop implying any pricing content for it.

### Target

Primary: Y_t = ln(VIX_t) - ln(RV_t), where
RV_t = 100 * sqrt((252/21) * sum_{i=1..21} r_{t+i}^2), r_s = ln(SPX_close_s / SPX_close_{s-1}),
and t+1..t+21 are the next 21 SPX trading days strictly after t (VIX is a 30-calendar-day implied
vol, about 21 trading days). Y_t is the log payoff ratio of a one-month SPX volatility swap struck
at VIX_t: Y > 0 means the seller of variance won, Y < 0 means realized exceeded implied.

Secondary, descriptive only, reported not gating: D_t = VIX_t - RV_t in vol points (approximately
the P&L per unit vega notional of a one-month vol swap) and Q_t = VIX_t^2 - RV_t^2 in variance
points (variance-swap payoff per unit variance notional).

Fit-window scale (2011-2018, 2,012 start dates): mean Y 0.293, sd 0.345, P(Y<0) 0.158; mean D
3.32 points; autocorrelation of Y 0.95 at lag 1, 0.74 at lag 5, 0.006 at lag 21, 0.12 at lag 42,
-0.02 at lag 63.

Defined-risk tradable proxy (to be stated on any page, untested here): "least over-priced" state
-> long 30-day ATM SPX straddle (max loss = premium paid) or stand aside from a short iron
butterfly; "not stretched" state -> the unconditional premium harvest, e.g. short iron butterfly
or short 30-day variance swap. VIX futures are NOT a proxy for this target (they carry the VIX
premium, a different quantity).

### Features

- p_t = #{VIX_{t-k} <= VIX_t, k = 1..1260} / 1260: one-sided, right-continuous ECDF of today's
  VIX close among the 1,260 prior closes, excluding today. This is exactly the engine's
  cls_dislocations.regime_percentile (equity context, reference VIX): recomputed from Cboe
  VIX_History.csv it reproduces all 2,726 ledger rows with regime_history = 1260 (2016-01-06 to
  2026-09-22) to 1e-6. It must be recomputed from the Cboe file because the ledger's window ramps
  from 0 to 1,260 between 2011 and 2016-01-05 and is unusable for the fit window.
- Fixed constants, not fitted: N = 1260 (the classifier's long window), H = 21 SPX trading days
  (matches the VIX's 30-calendar-day horizon), upper tail only, comparison group = all non-signal
  days.
- Secondary descriptive feature (reported in one table, never gating): the shipped equity
  composite_score from decisions.csv (the classifier's signed severity) binned by
  |composite_score| deciles, and the regime label low_vol/normal/high_vol, to show what the
  product's actual output would have said.
- Deliberately excluded from this test: VX futures, term-structure slope, OVX, VX30, catalyst
  dates. This angle is spot VIX (implied) versus subsequent SPX realized volatility; an OVX/WTI
  analogue is not registered here because WTI spot (DCOILWTICO) went negative in April 2020 and
  the oil options that OVX references are not free data.

### Rule

S_t(q) = 1 (stretched, "premium compressed") if p_t >= q, else 0. Exactly ONE free parameter: q
in the grid {0.75, 0.80, 0.85} (m = 3 grid points). The second permitted parameter is
intentionally unused; nothing else may be varied (N, H, tail, direction, comparison group, target
and event definition are fixed above and in the fit procedure).

Event definition: a signal EVENT is a signal day t whose forward window [t+1, t+21] does not
overlap the window of the previous accepted event (first-come, non-overlapping, walking forward
in time); non-signal COMPARISON WINDOWS are built the same way on non-signal days. Effect
Delta(q) = mean Y over signal events - mean Y over non-signal windows; hypothesis direction
Delta < 0.

Alert-frequency constraint (DEC-002): S fires on 12.8% (q = 0.80), 15.2% (0.75) and 9.1% (0.85)
of fit-window days; state-only counts (no outcome read) on 2019-01-02..2026-08-26 give
30.2% / 24.3% / 17.5% of days and 45 / 39 / 33 non-overlapping events (2020: 12/12/10 of them;
2022: 10/9/7).

### Fit

Fit window: start dates 2011-01-03 to 2018-11-30 inclusive, so that every fit forward window ends
on or before 2018-12-31 and no SPX return from 2019 enters the fit (the designer's feasibility
look used start dates through 2018-12-31; the runner uses the strict cutoff and records any
difference). For each q in {0.75, 0.80, 0.85}: build signal events and non-signal windows as
defined, compute Delta_fit(q), its Welch standard error and t_fit(q), the fit firing rate, and
P(Y<0) in both groups. Select q* = argmin t_fit(q) (most negative t) subject to (a)
n_signal_events_fit >= 15 and (b) fit firing rate <= 30% of days; ties go to the smaller q. Only
q* is run on the holdout.

Attempt count: m = 3 (the grid); the Bonferroni correction below is applied to the single
selected q*, which over-corrects on purpose because the selection itself used the fit window.

Designer's disclosed fit-window looks (2011-2018 outcomes only; NO SPX return after 2018-12-31
has been combined with the state by anyone for this target): one 10-bin table of Y by p (both
tails); the three grid cells above (q = 0.75: 26 events, Delta -0.108, t -1.21; q = 0.80: 23,
-0.078, -0.91; q = 0.85: 18, -0.116, -1.32, so the designer expects q* = 0.85 but the runner's
strict-cutoff fit decides); two lower-tail cells (p <= 0.20, p <= 0.10: Delta +0.03 and +0.02)
abandoned before registration and NOT to be run on the holdout under this registration; a
zero-parameter Spearman(p, Y) on 96 non-overlapping fit windows = -0.003, reported as evidence
that any effect is confined to the tail. The earlier edge test
(`doc/research/edge-test-2026-09-27.md`) used the whole sample but a different target family
(spot direction, spot absolute moves); it is disclosed, not counted in m. Script for the
feasibility numbers: `scratchpad/design/vrp_feasibility_fit_only.py`.

### Holdout metrics

- Sample: start dates 2019-01-02 through the last date whose 21-SPX-day window is complete
  (2026-08-26 at the time of design; 1,923 start dates), one run only. Report n_signal_days,
  firing rate, n_signal_events (non-overlapping), n_non_signal_windows, and the per-year event
  counts.
- M1 primary: Delta_mean = mean Y over signal events - mean Y over non-signal windows, with both
  group means, group sds, Welch SE, one-sided t and p (H1: Delta < 0), Welch df.
- M2 primary confirmation: Delta_days = mean Y over ALL signal days - mean Y over all non-signal
  days, with a stationary block bootstrap of the daily holdout series (block length 63 trading
  days, 10,000 resamples, seed 20260927) giving the one-sided 98.33% upper bound and the
  two-sided 95% interval of Delta_days.
- M3 sign metric: P(Y < 0 | signal events) and P(Y < 0 | non-signal windows) with Wilson 95%
  intervals and their ratio; the same on all days.
- M4 minimum detectable effect, stated from the realised holdout n and sds:
  MDE_80 = (t_crit + 0.84) * SE(Delta); design estimate with 33-39 events (sd 0.33-0.40) and
  about 80 non-signal windows (sd 0.33) is SE about 0.07 and MDE about 0.21 log units, versus an
  in-sample effect of -0.08 to -0.12, i.e. roughly 25-35% power against the in-sample effect
  size. A non-pass is therefore the expected outcome even if the effect is real, and is recorded
  as "not supported at this power", never re-run.
- M5 robustness, reported not gating: (a) Delta_mean and M3 excluding the single 21-day block
  with the largest RV in the holdout, and excluding the single most favourable block; (b) per
  calendar year table of Delta and counts; (c) the points version D and variance version Q with
  the same groups (the fit window suggests D shows no difference or the opposite sign: 3.16 vs
  3.11 at q = 0.85), stated in advance so it cannot be promoted after the fact; (d) the same
  table with the shipped |composite_score| >= its fit-window 85th percentile in place of p; (e)
  the two non-selected q values, labelled non-registered, for the record; (f) group medians and
  the worst-decile Y in each group; (g) the unconditional holdout mean premium with its
  block-bootstrap interval (VRP existence check, expected positive with near certainty).
- M6 data-integrity gates run before any metric is read: recomputed p_t equals
  cls_dislocations.regime_percentile to 1e-4 on every ledger date with regime_history = 1260;
  Cboe VIX close equals ledger VIX on every shared date (3,986 dates matched to 0.000 at design
  time); Yahoo SPX daily log returns agree with Cboe SPX_History.csv to 1e-4 on every 2011-2026
  date or the discrepant dates are listed and the windows containing them are reported with and
  without; VIX holiday-session rows with no SPX close (33 from 2022) are dropped as start dates.

### Pass

Correction: Bonferroni over the m = 3 grid points, one-sided alpha = 0.05 / 3 = 0.0167 (t_crit
about -2.15 at Welch df around 50; block-bootstrap one-sided 98.33% bound), applied to the single
selected q* although only one rule is run on the holdout, so the correction also covers the
in-sample selection.

PASS requires ALL of:

- (i) n_signal_events >= 30;
- (ii) Delta_mean <= -0.05 log units (economic floor: about 5% of realized vol, roughly 0.8 vol
  point at VIX 16, above bid-ask on a one-month SPX straddle) AND Welch one-sided p < 0.0167;
- (iii) block-bootstrap one-sided 98.33% upper bound of Delta_days < 0;
- (iv) P(Y<0 | signal events) > P(Y<0 | non-signal windows) (direction only, no significance
  required);
- (v) holdout firing rate <= 30% of days (DEC-002 idle discipline).

If the SPX cross-check in M6 flags discrepant dates, the pass must also hold with those windows
excluded. A pass is to be read as: the classifier's stretched state identifies periods in which
the SPX variance premium is compressed by at least 5% in log terms; it is not a claim that
volatility is under-priced and not a directional VIX call.

### Kill

KILL (hypothesis refuted; no re-parametrisation, no second run, recorded with the same detail as
a pass) if n_signal_events >= 30 and either Delta_mean >= 0 (stretched events carry an equal or
larger log premium than other days) or P(Y<0 | signal events) <= P(Y<0 | non-signal windows).

INCONCLUSIVE BY DESIGN if n_signal_events < 30 (state-only counts at design time were 45/39/33,
so this should not occur unless data gates remove events); record and stop.

NOT SUPPORTED AT PRE-REGISTERED POWER if Delta_mean < 0 and the sign metric is in direction but
any pass condition (ii), (iii) or (v) fails; record the point estimates, intervals and MDE; the
state is then treated as descriptive for this target unless a NEW registration with a different
data source (e.g. option prices) is written.

In every outcome the report states results with and without the largest-RV block (Mar 2020 is
expected to be that block).

### Data

Scratchpad root: `C:\Users\Radu\AppData\Local\Temp\claude\d--Work-primescore-markets\1c100b72-59e9-448b-a615-aa9b145c355c\scratchpad`.

- Cboe VIX daily history, free:
  https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv (1990-01-02 to
  2026-09-25, 9,281 rows); already downloaded to `design\VIX_History.csv`. Needed so that p_t has
  a full 1,260-close window from 2011-01-03 (first full-window date in the file: 1994-12-27).
  Cross-checked against the ledger export: identical on all 3,986 shared dates.
- SPX daily closes: `data\spx.csv` (Yahoo ^GSPC, 1990-2026, 9,251 rows) PLUS the free Cboe
  cross-check https://cdn.cboe.com/api/global/us_indices/daily_prices/SPX_History.csv (closes
  from 1975) to flag any bad Yahoo print before RV is computed.
- Ledger export `data\vix.csv` (FRED VIXCLS via ing_signals) only as the identity check against
  the Cboe file; and the read-only ledger copy `engine.db`, table cls_dislocations
  (context = 'equity', columns as_of_ms, regime_percentile, regime_history) to prove the
  recomputed p_t equals the classifier's regime statistic on every full-window date.
- `data\decisions.csv` (equity context: composite_score, regime) for the secondary descriptive
  table only.
- Not needed for this test: vx_futures.csv, vx_curve.csv, fomc_dates.csv, cpi_dates.csv,
  ovx.csv. Not available and required for the tradable version: SPX option prices or
  variance-swap quotes (30-day ATM straddle marks); this is stated, not substituted.
- Designer's feasibility script (fit-window outcomes only, holdout state counts only):
  `design\vrp_feasibility_fit_only.py`.

### Prior art

Existence and size of the premium:

- Bollerslev, Tauchen and Zhou, "Expected Stock Returns and Variance Risk Premia", Review of
  Financial Studies 22(11), 2009, 4463-4492: monthly 1990-2007 mean VIX^2 33.2 versus realized
  variance 14.9 (percent squared), premium 18.3 with AR(1) 0.49 (Table 1); realized variance
  there is from 5-minute data, so daily-close RV as used here is a degraded version.
- Carr and Wu, "Variance Risk Premiums", Review of Financial Studies 22(3), 2009, 1311-1341: the
  average log variance risk premium on the S&P 500 is strongly negative for the variance buyer
  and is not explained by standard risk factors.
- Dew-Becker, Giglio, Le and Rodriguez, "The price of variance risk", Journal of Financial
  Economics 123(2), 2017: exposure to realized variance is priced at an annualized Sharpe of
  about -1.3; only unexpected transitory realized variance is priced.

State dependence, which is what this test is about:

- Cheng, "The VIX Premium", Review of Financial Studies 32(1), 2019, 180-227: the ex-ante
  volatility premium falls or turns negative exactly when ex-ante risk measures rise, and this
  predicts ex-post returns with a coefficient near one (that is the direction registered here).
- Bollerslev and Todorov, "Tails, Fears, and Risk Premia", Journal of Finance 66(6), 2011: the
  variance premium is largely compensation for jump-tail risk, paid out in stress.
- Bekaert and Hoerova, "The VIX, the variance premium and stock market volatility", Journal of
  Econometrics 183(2), 2014, 181-192: decomposition of VIX^2 into conditional variance and
  premium; the premium predicts returns, the variance component predicts activity.

Level statistics carry no tradable information on futures:

- Aragon, Mehra and Wahal, NBER w24575, 2018 (every regression of VIX futures returns on the VIX
  level or demeaned level over 2004-2017 has t < 2 and adjusted R^2 < 1%), which is why this
  test is on the premium, not on direction.

In-house: `doc/research/edge-test-2026-09-27.md` (spot direction refuted, whole sample),
`doc/research/recovery-plan-2026-09-27.md` section 2 (protocol followed here).

Fit-window facts measured for this design (2011-2018 only): unconditional log premium 0.293 (sd
0.345), P(RV > VIX) 0.158; stretched events (p >= 0.75/0.80/0.85) 0.200/0.211/0.170 versus
0.308/0.290/0.287 otherwise, P(RV > VIX) 0.31/0.30/0.28 versus 0.15/0.14/0.17, Welch t
-1.21/-0.91/-1.32; Spearman(p, Y) on non-overlapping windows -0.003. Expectation stated in
advance: the premium's existence in 2019-2026 is near certain; the state effect is in the Cheng
direction in-sample but small relative to the power available, so "not supported at this power"
is the most likely outcome and would be an honest result.

### Breaks if

- **Look-ahead.** p_t must use only the 1,260 closes strictly before t (as the classifier does)
  and RV must use SPX closes t+1..t+21 only; a window that includes day t's return, or an ECDF
  that includes today's close, leaks. VIX closes at 16:15 New York, SPX at 16:00: both are day-t
  information, acceptable, but a VIX series that FRED posted a day late would misalign the
  state; the Cboe file is the authority and was found identical to the ledger on 3,986 dates.
- **Fit/holdout leakage.** Fit windows starting in December 2018 end in January 2019; the strict
  start-date cutoff 2018-11-30 removes it.
- **Calendar.** 33 VIX holiday-session rows (2022 onward) have no SPX close and must not be start
  dates; forward windows must be counted on the SPX calendar, never on VIX row count.
- **Data errors.** One bad Yahoo SPX print inflates RV for 21 consecutive windows and could
  manufacture or destroy the effect; the Cboe SPX_History.csv cross-check gate exists for this.
- **Tie handling.** The ECDF must be right-continuous (<=) to reproduce regime_percentile; a
  strict-inequality version fails the 1e-4 identity gate.
- **Regime non-stationarity.** The 5-year percentile fired on 12.8% of fit days but 24.3% of
  holdout days at q = 0.80 because 2020-2022 sat above the 2015-2019 distribution; 21 of 39
  holdout events fall in 2020 and 2022, so two episodes with opposite premium behaviour (March
  2020 onset, where RV exceeded VIX; the 2022 grind, where it mostly did not) can decide the
  mean, and the with/without-largest-block report and the 63-day block bootstrap are the only
  defence.
- **Window overlap.** A non-signal comparison window that starts the day before the state
  switches on shares 20 of 21 days with the first signal event, which dilutes the contrast toward
  zero (conservative) but makes the two samples dependent; inference should lean on the block
  bootstrap, not on the Welch t alone.
- **Horizon mismatch.** VIX's 30 calendar days span 20-22 trading days; the fixed 21 is an
  approximation of a few percent in RV. Close-to-close RV omits intraday variation and treats
  overnight gaps as one return; the variance-swap payoff is also close-to-close, so the target
  matches the instrument, but not BTZ's 5-minute measure.
- **Roll effects.** None, no futures are used.
- **Wrong-metric promotion.** The points version D and the composite-score table are expected to
  disagree with the log version (fit: D shows no difference), and may look "better" by chance;
  only the log version gates, by this registration.
- **Autocorrelation of the state.** Lag-1 0.96, so signal days are not independent bets; only
  non-overlapping events are counted toward n >= 30.
- **Ledger ramp.** cls_dislocations.regime_percentile before 2016-01-06 is computed on a window
  shorter than 1,260 and must not be used for the fit.
- **Tradability.** Even a pass does not establish an option strategy's P&L (path-dependent gamma,
  bid-ask on a one-month SPX straddle of roughly 0.3-0.5 vol points, margin on short variance);
  the defined-risk proxy is stated so that the next registration can test it if option data are
  ever bought.
- **Adversary check.** The designer looked at the fit window's both tails and at holdout state
  frequencies but at no holdout outcome; if the runner or judges find any holdout outcome for
  this target was examined before the run, the run is void.

---

## Design 4: r2-magnitude-vx30-state-vs-realized-baseline (magnitude)

### Hypothesis

The classifier's stretched/compressed state (the engine's one-sided level percentile of the spot
volatility index against its prior 1,260 closes, cls_dislocations.regime_percentile, reproduced
3985/3985 equity and 3954/3954 oil rows as #{prior closes <= today}/min(i,1260)) predicts that
the forward ABSOLUTE LOG change of a tradable volatility price (VX30, the 30-day
constant-maturity VX future) over the next 5 and 21 VX trading days will be larger, relative to
the recent realized absolute-change baseline (the free proxy for what a straddle costs), than on
unconditional days.

Null: once level scaling (log changes) and volatility-of-volatility clustering (trailing 21-day
realized baseline) are removed, state days move no more than any other day.

The design problem this addresses: the shipped system used the state's SIGN as a direction
forecast; the only reading a long-volatility buyer needs is magnitude beyond cost, which was
never measured on a tradable price. Expected outcome per the literature (Kaeck-Alexander level
scaling, Park VVIX premium): the raw magnitude effect on spot will be near-mechanical and the
relative effect on VX30 small; a pass requires a large effect (see minimum detectable effect in
Pass).

### Target

Primary (tradable): r_{t,h} = |log VX30_{t+h} - log VX30_t| / b_{t,h}, with VX30 from
vx_curve.csv (monthly contracts, calendar-day linear interpolation to 30 days), h in {5, 21} VX
trading days, and b_{t,h} = sqrt(h) * mean_{i=0..20} |log VX30_{t-i} - log VX30_{t-i-1}|
(trailing 21-day realized mean absolute daily log change, the straddle-cost proxy; known at close
t). Statistic per horizon: EXCESS_h = median(r_{t,h} on state days) - median(r_{t,h} on all days)
within the evaluation window, and the relative excess REL_h = median(r on state days)/median(r on
all days) - 1.

Robustness target (reported, not gating): same-contract front future,
R1_{t,h} = |log S_{t+h}(c) - log S_t(c)| where c is the F1 monthly contract at t if its expiry
falls after date t+h, else the F2 contract, so no roll gap enters the return; its baseline is the
trailing 21-day mean absolute same-contract daily log change.

Secondary market (NOT tradable, labelled as such): OVX spot index, r computed identically on the
OVX calendar; there are no OVX futures, so this arm can inform the classifier but cannot produce
a product pass.

Fit-window unconditional medians of r (2011-2018): VX30 0.755 (h = 5) and 0.639 (h = 21); OVX
0.773 and 0.693.

### Features

- P_t = one-sided level percentile of the spot index close on date t against the prior
  min(i,1260) closes, #{c_s <= c_t, s in [t-1260, t-1]}/N, computed from vix.csv (VIX) and
  ovx.csv (OVX) exactly as the engine does including the 2011-2015 ramp (shorter windows), and
  cross-checked against cls_dislocations.regime_percentile (must match to 4 dp on every
  overlapping date; the designer verified 100% match).
- D_t = |P_t - 0.5| (distance from the median of the level distribution; 0 = mid-distribution,
  0.5 = most extreme).
- SIDE_t = high if P_t >= 0.5 (stretched), low if P_t < 0.5 (compressed).
- b_{t,h} = sqrt(h) x trailing 21-day mean absolute daily log change of the target series (VX30,
  same-contract F1, or OVX), the recent-realized straddle-cost proxy, used only as the
  denominator of r and as the stratification variable in the clustering diagnostic; fixed, not
  fitted.
- Calendar: signal dates are VX trading dates (vx_curve.csv trade_date) that have a VIX close in
  vix.csv; the 32 FRED holiday-session VIX rows with no VX settlement and the 12 VX dates with no
  VIX close (e.g. 2015-04-03, 2018-12-05) are not signal dates but remain in the VX30 series for
  forward changes and baselines. For OVX the OVX calendar is used.
- Reused from the classifier: nothing but the one-sided level percentile (the diagnosis names it
  the sound stretched/compressed statistic). The signed composite_score, the dislocation and the
  regime label k-map are NOT used.

### Rule

STATE_t(d, side) = 1 if [side = high and P_t >= 0.5 + d] or [side = low and P_t <= 0.5 - d] or
[side = both and |P_t - 0.5| >= d]; else 0.

Exactly two free parameters: d in {0.35, 0.40, 0.45} (i.e. the level is in the outer 15%, 10% or
5% tail(s) of its own five-year distribution) and side in {high, low, both}. Grid = 9
combinations per market; evaluated at two pre-fixed horizons h in {5, 21}, so 18 attempts per
market, 36 in total across VIX and OVX.

Everything else is fixed by this pre-registration: baseline window 21 days, sqrt(h) scaling,
median statistic, VX30 as primary target, 63-day blocks, 5,000 placebo shifts, episode gap 5
days. Claim tested per horizon: EXCESS_h > 0 (state days move more than all days relative to
recent realized).

Fire rates of the grid on the fit window (VIX calendar, 2011-2018): high 11.8/7.9/3.7%, low
33.8/27.3/18.4%, both 45.6/35.2/22.2% for d = 0.35/0.40/0.45; OVX: high 16.3/12.1/6.8, low
23.4/18.5/11.7, both 39.7/30.7/18.4. The DEC-002 alert constraint (fire on <= 30% of days) is
REPORTED for the chosen rule but is not a gate for this research question; it becomes a gate
only if the rule is later proposed as a product alert.

### Fit

All fitting uses signal dates 2011-01-03 to 2018-12-31 only, with forward windows allowed to end
in early 2019 (t+21 for December 2018 signals) since the target date is chosen by the signal
date. For each market and each of the 9 (d, side) combinations, compute on the fit window:

- (a) fire rate;
- (b) n_eff,h = number of non-overlapping h-day forward windows starting on state days (greedy:
  take the first state day, skip h days, take the next state day) for h = 5 and 21, plus the
  number of state episodes (runs of state days merged across gaps of <= 5 trading days);
- (c) EXCESS_h and REL_h;
- (d) placebo distribution: 5,000 circular shifts of the STATE indicator by offsets uniform on
  [63, N-63] relative to the r series, recomputing EXCESS_h each time; placebo
  z_h = EXCESS_h / sd(placebo) and one-sided p_h = (1 + #{placebo >= observed})/5001.

Eligibility: n_eff,5 >= 30 and n_eff,21 >= 30 in the fit window (from the designer's
feature-only count, this excludes the one-sided high combinations at d = 0.40 and 0.45 for VIX
and d = 0.45 for OVX, whose 21-day counts are 19/11 and 15).

Selection: among eligible combinations, choose the one maximizing the mean of placebo z_5 and
z_21.

Gate to proceed to the holdout: the selected combination must have fit-window placebo
p_21 <= 0.05/18 = 0.00278 and EXCESS_5 > 0. If no combination clears the gate, the holdout is
NOT run, and the result is recorded as: the state carries no in-sample magnitude information
beyond recent realized vol-of-vol, with the full 9-row table published. All 9 rows (both markets,
both horizons) are published whether or not the gate is cleared.

Attempts counted: 18 per market, 36 total; the correction below uses 18 (per market) because the
two markets are separately reported hypotheses, and the 36 figure is stated alongside. The
designer of this test does not run or judge it. Feasibility scripts used by the designer:
`scratchpad\feas_r2.py` and `feas_r2b.py` (fit-window unconditional ratio distribution, placebo
spread, grid fire rates and counts; holdout touched only for state-day counts, never outcomes).

### Holdout metrics

- ONE run on signal dates 2019-01-02 to the last date with t+21 available (about 2026-08-26), at
  the fitted (d, side), for h = 5 and h = 21, on VX30 (primary) and, if fitted, OVX (secondary,
  non-tradable).
- n_eff,5 and n_eff,21 (non-overlapping forward windows on state days) and the number of state
  episodes (gap 5); state-day count and fire rate; DEC-002 fire-rate check (<= 30%) reported.
- EXCESS_h = median r on state days minus median r on all days; REL_h = ratio of medians minus
  1; also the mean of r on state vs all days and the 75th/90th percentiles of r (tail of the move
  distribution, which is what a straddle monetizes).
- Placebo p_h and z_h from 5,000 circular shifts of the state indicator within the holdout
  (offsets uniform on [63, N-63]); this null keeps the runs structure of the state and the
  clustering of large moves and breaks only their alignment.
- 63-day circular block-bootstrap 95% interval for EXCESS_h (2,000 replicates resampling aligned
  (state, r) rows jointly).
- Clustering diagnostic: baseline-stratified excess STRAT_h = state-day-count-weighted average
  over deciles of b_{t,h} of [median r on state days in decile minus median r on all days in
  decile]; reported next to EXCESS_h (fit-window Spearman of P_t with b_t is 0.535 for VIX and
  0.450 for OVX, so this control matters).
- Same-contract F1 robustness: EXCESS_h and placebo p on the R1 target; VX30 excluding the 195
  extrapolated dates (vx30_method = extrap_F1F2_front_beyond_30d); result excluding the single
  largest 63-day block of the holdout; entry-day-only version (first day of each episode).
- Per-year table of state days, median r on state vs all days, and the largest single
  contributing episode, so the adversaries can see whether March 2020, August 2024 or April 2025
  alone carries the result.
- Descriptive side split at the fitted d: EXCESS_h for high-only and low-only (not a test;
  published for the classifier's benefit).

### Pass

Correction: Bonferroni over the 18 attempts per market (9 grid points x 2 horizons), one-sided
alpha = 0.05/18 = 0.00278 per test (placebo resolution 1/5001 suffices). The strictly valid
holdout family is smaller (only the fitted rule is run) but the founder's instruction is to
correct for everything tried, so 18 is used; the 36-attempt figure (both markets) gives
alpha = 0.00139 and is reported as a sensitivity.

PASS for the primary VIX/VX30 arm requires ALL of:

- (P1) n_eff,5 >= 30 and n_eff,21 >= 30 in the holdout, else INCONCLUSIVE BY DESIGN (the
  designer's feature-only holdout counts are: 0.35 both 128/46, 0.40 both 91/33, 0.45 both
  48/22, 0.35 high 86/33, 0.40 high 61/22, 0.45 high 31/16, 0.35 low 42/13, 0.40 low 30/12,
  0.45 low 17/6 for h = 5/h = 21, so any low-side rule and d = 0.45 will be inconclusive at 21
  days because compressed VIX states were rare after 2018; this is disclosed now, not discovered
  later);
- (P2) h = 21 placebo p_21 <= 0.00278;
- (P3) the 63-day block-bootstrap 95% interval for EXCESS_21 excludes 0;
- (P4) economic size: REL_21 >= 0.15, i.e. the median state-day move relative to recent realized
  is at least 15% larger than on all days, the margin chosen to cover the excess of implied over
  realized vol-of-vol that the free baseline omits (VVIX_History.csv can be fetched to report
  the actual fit-window VVIX/realized ratio, but 0.15 is fixed regardless);
- (P5) EXCESS_5 > 0 (same sign at the short horizon);
- (P6) STRAT_21 >= 0.5 x EXCESS_21 (the effect survives within baseline strata; otherwise it is
  labelled clustering-driven and is not a pass).

Minimum detectable effect, from the fit-window placebo spread at comparable sample size (about
2,000 days): for side = both at d = 0.40 the placebo sd of EXCESS_21 is about 0.040, so the alpha
quantile is about +0.10 in ratio units against an unconditional median of about 0.64, i.e. a
pass needs roughly +16% relative (consistent with P4); for one-sided high at d = 0.40 the sd is
about 0.11 and a pass needs roughly +48% relative.

PARTIAL (recorded, not a pass, no second look): P2-P6 hold at h = 5 but not at h = 21, or all
hold except P4.

OVX arm: identical thresholds, reported with the same detail, labelled non-tradable; it cannot
produce a product pass.

### Kill

KILL (the feature family "spot level percentile as a predictor of the magnitude of tradable
volatility moves beyond recent realized" is retired; no refit, no new grid on this feature, no
second look at 2019+) if, in the single holdout run at the fitted rule, EITHER (K1)
EXCESS_21 <= 0 (state days do not move more than unconditional days relative to recent realized,
or move less), OR (K2) placebo p_5 > 0.20 AND placebo p_21 > 0.20.

Also KILL-IN-SAMPLE if no grid combination clears the fit-window gate (p_21 <= 0.00278 with
EXCESS_5 > 0): the holdout is not run and the negative result is recorded with the full 9 x 2
table.

Any outcome that is neither PASS nor KILL nor INCONCLUSIVE is recorded as NOT PASSED with the
same tables; the only permitted follow-up is a new pre-registration with a different feature
(e.g. VVIX-relative or term-structure-based), never a re-run of this one with moved thresholds.
If the result is INCONCLUSIVE by P1, the recorded conclusion is "too few independent state
windows after 2018 to test", not "weak evidence for".

### Data

Scratchpad root: `C:\Users\Radu\AppData\Local\Temp\claude\d--Work-primescore-markets\1c100b72-59e9-448b-a615-aa9b145c355c\scratchpad`.

- `data\vx_curve.csv` (trade_date, vx30, vx30_method, f1_expiry, f1_settle, f2_expiry,
  f2_settle, vix_close): VX30 primary target and calendar; 2,014 rows 2011-2018, 1,945 rows from
  2019-01-01.
- `data\vx_futures.csv` (monthly rows only, contract_type = M): same-contract F1 robustness
  target and its daily same-contract log changes.
- `data\vix.csv` and `data\ovx.csv`: spot closes for the one-sided percentile feature (ledger
  export, FRED VIXCLS/OVXCLS) and the OVX secondary target.
- `engine.db` (read-only copy), table cls_dislocations columns regime_percentile,
  regime_history, iv_observed_at_ms, context: cross-check that the recomputed P_t equals the
  engine's statistic on every date (New York date of iv_observed_at_ms, last row per context per
  date by as_of_ms).
- Optional, reported only: https://cdn.cboe.com/api/global/us_indices/daily_prices/VVIX_History.csv
  to state the fit-window ratio of implied (VVIX) to realized VX30 vol-of-vol behind the 15%
  economic margin; and https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv
  if anyone wants to document how the 2011-2015 window ramp changes P_t (a different feature;
  would need a new pre-registration).
- Not needed: SPX, FOMC and CPI dates, decisions.csv (the signed composite and the decision
  outcomes are not inputs to this test).

### Prior art

- Level scaling: Kaeck and Alexander (IRFA 2013,
  https://www.sciencedirect.com/science/article/abs/pii/S1057521913000094) show the volatility
  of VIX depends on its level, so absolute point changes scale with the level and log changes are
  much closer to homoskedastic; this is why the target is in logs and why the edge-test note's
  OVX finding (median absolute 5-day move 2.84 points on signal days vs 2.14 overall,
  `doc/research/edge-test-2026-09-27.md` item 4) is not yet evidence.
- Vol-of-vol clustering and mean reversion: arXiv 1507.00846 (Sections 3.2-3.4) documents
  positively autocorrelated absolute variations of the whole VX curve with vol-of-vol half-life
  under a month; this is why the baseline is trailing realized and why the placebo null
  preserves runs (fit-window lag-1 autocorrelation of |daily log VX30 change| is 0.28).
- Priced vol-of-vol: Park (J. Financial Markets 26, 2015,
  https://www.sciencedirect.com/science/article/abs/pii/S1386418115000403) shows a higher VVIX
  raises the price of tail hedges (SPX puts, VIX calls) and lowers their subsequent 3-4 week
  returns; hence a magnitude effect that exists statistically can still lose money after
  premium, and this test's free baseline understates the cost (P4's 15% margin).
- Level is not directional information on futures: Aragon, Mehra and Wahal (NBER w24575, 2018,
  https://www.nber.org/system/files/working_papers/w24575/revisions/w24575.rev0.pdf, Table 3)
  find no predictability of VX futures returns from the VIX level or demeaned level 2004-2017;
  this test deliberately asks a magnitude question instead of the refuted directional one.
- Reporting template: Simon and Campasano (J. Derivatives 21(3), 2014) report Sortino, medians
  and worst outcomes because vol P&L is non-normal; this design uses medians, percentiles, and
  the result without the largest block.
- Internal: ADR-0008 (directional scenario refuted), `doc/research/recovery-plan-2026-09-27.md`
  sections 2 and 3 (R2 hypothesis and protocol), and the diagnosis finding that the one-sided
  regime_percentile is the sound stretched/compressed statistic while the signed score is a
  folded rank with low-side compression.

### Breaks if

- **Look-ahead.** P_t must use only closes on or before t (window strictly before t, ranked value
  = close t); b_{t,h} only changes ending at t; the forward change starts at settle t, so the
  implied trade is entered at the close on which the state is known (realistically the next
  open, a small slippage that is stated, not modelled). Any use of the FRED VIX date that is one
  day late (FRED can lag Cboe by a day) would misalign state and price; the runner must confirm
  vix.csv dates match vx_curve.vix_close dates and report any mismatch.
- **Calendar leakage.** Using VIX rows (33 holiday sessions) as the calendar would create
  phantom forward windows; the VX calendar is mandatory.
- **Roll effects.** VX30 is constant maturity, so its 21-day log change includes moving along a
  contango/backwardation curve that a held contract does not experience in the same way; the
  same-contract F1 target is the control, and a pass that vanishes on F1 is a curve artefact,
  not tradable magnitude. The 195 extrapolated VX30 dates just after rolls are noisier; the
  exclusion sensitivity guards against them.
- **Data errors.** 2011-May 2013 settlements come from Internet Archive copies of legacy Cboe
  files (one implausible legacy value already overridden on 2013-05-28); a systematic error there
  affects only the fit window, but the runner should scan for daily |log change| > 0.5 and settle
  steps inconsistent with VIX, and report them. Pre-2014 settles are in 0.05 increments
  (immaterial in logs).
- **Vol-of-vol mean reversion as a confound.** High realized b_t tends to mean-revert, lowering r
  on high-b days, while compressed states have low b_t (fit-window Spearman(P, b) = 0.535); a
  low-side pass could be vol-of-vol mean reversion rather than level information, which is what
  P6 (stratified excess) is designed to catch.
- **Regime dominance.** March 2020 (VX30 to about 70), August 2024 and April 2025 sit in the
  holdout; the per-year table and the largest-block exclusion show whether one episode decides
  the result, and a pass carried by one 63-day block should be read as one observation.
- **Sample size.** One-sided and d = 0.45 rules will not reach 30 independent 21-day windows
  after 2018 (disclosed above); a fitted low-side rule makes the 21-day test inconclusive by
  design.
- **Non-stationarity of the fire rate.** The state fires far more often on 2013-2017
  (compressed) than after 2018, so a rule that looks good in-sample on the low side is being
  tested out of sample mostly on the high side; that is a legitimate failure mode of the
  level-percentile feature, not a data problem.
- **The cost proxy.** Recent realized understates option premium (Park); a statistical pass here
  is necessary, not sufficient, for a long-volatility edge and must be stated that way on any
  page.
- **Designer foresight.** The designer knows the fit-window unconditional medians and placebo
  spreads and the holdout state counts (no holdout outcomes); if that knowledge is judged to have
  shaped the grid, the adversaries should treat d and side as chosen with feature-level
  foresight and weigh the 36-attempt correction (alpha 0.00139).

---

End of the frozen registration section. Test runners append results below this line; nothing
above it is edited after registration.

---

## Result: catalyst-vx30-crush

Runner: `scripts/research/catalyst_test.py` (phase fit, run 2026-09-27T22:19:39 UTC). Results JSON: `scratchpad/catalyst_results.json`.
Input hashes (SHA-256) recorded before any 2019+ number was computed:

- `vx_curve.csv`: `93ffc173c99df67d221c94993e78a65030a4195d74e8b792cb94c7f53ab29592`
- `vx_futures.csv`: `42af11c33f0523b6241c9c9463b93704729dc974a07a73c28d4be242592b8ca1`
- `fomc_dates.csv`: `94b20050a7ea2de438836b59ffd168e1fb75ffd79b8cd06ea9425436e1ffca8e`
- `cpi_dates.csv`: `57c4104ba0208c7d8e520f019b32dd455c489a782f1cf9a3699555e7a5765c0d`
- `decisions.csv`: `a9db3178ad7523527f546a3aee6aa16636fd0fb63180481b1f7b99078e28ac15`
- `vix.csv`: `20e5b432be9bf0262cb8eed184eed17ecebf913e175fa7e0eaf3a0880187f2b4`

### Fit window 2011-01-01 to 2018-12-31

Events on VX trading days: 151 distinct days (64 FOMC, 93 CPI, 6 same-day pairs); 2014 VX trading days. Dropped (no VX settlement): 2017-04-14 (cpi), 2020-04-10 (cpi). Placebo windows for the fit are restricted to end on or before 2018-12-31.

| Leg | Window | n | Mean change | Placebo mean | Excess | Placebo z | One-sided p | Tradable mean change (n) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| post (short) | j = 0 | 151 | -0.060 | -0.018 | -0.043 | -0.63 | 0.2662 | -0.071 (151) |
| post (short) | j = 1 | 151 | -0.016 | -0.024 | 0.008 | 0.09 | 0.5373 | -0.056 (151) |
| post (short) | j = 2 | 150 | -0.092 | -0.042 | -0.051 | -0.52 | 0.2988 | -0.168 (151) |
| post (short) | j = 3 | 146 | -0.036 | -0.064 | 0.028 | 0.26 | 0.6126 | -0.170 (151) |
| pre (long) | k = 1 | 151 | 0.000 | 0.000 | 0.000 | n/a | 1.0000 | 0.000 (151) |
| pre (long) | k = 2 | 151 | -0.061 | -0.002 | -0.059 | -0.86 | 0.8061 | -0.089 (151) |
| pre (long) | k = 3 | 151 | 0.042 | 0.000 | 0.041 | 0.46 | 0.3216 | -0.040 (151) |
| pre (long) | k = 5 | 149 | 0.018 | -0.012 | 0.029 | 0.28 | 0.3858 | -0.154 (151) |

Selected and frozen: **j\* = 0** (most negative post-leg z), **k\* = 3** (most positive pre-leg z). 8 in-sample evaluations; Bonferroni m = 24 as registered (alpha_adj = 0.00208). Undefined cells (zero-length window, excess identically 0, z = 0/0), counted as attempts but excluded from the argmax: k = 1.

### Holdout 2019-01-01 onward, one run at j\* = 0, k\* = 3 (phase holdout, run 2026-09-27T22:22:19 UTC)

Events: 149 distinct days (61 FOMC, 91 CPI, 3 same-day), 2019-01-11 to 2026-09-16, 1945 VX trading days. Dropped for an incomplete post window: none.

Intervals: 95 percent, 10,000 resamples; 'iid' resamples events, 'block' is a circular block bootstrap of the date-ordered per-event excess with blocks of 2 consecutive events (about 21 trading days). p is the one-sided placebo p from 10,000 one-offset-per-event draws. Hit rate = share of events with the hypothesised sign / same on placebo windows.

| Hypothesis | n | Mean | Median | Placebo mean | Excess | Excess CI iid | Excess CI block | z | p | Hit / placebo hit |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| H1 pooled post (short, t-1 to t+0) | 149 | -0.081 | -0.257 | 0.019 | -0.101 | [-0.310, 0.115] | [-0.290, 0.099] | -1.30 | 0.0947 | 0.631 / 0.549 |
| H2 pooled pre (long, t-3 to t-1) | 149 | -0.068 | 0.011 | -0.042 | -0.026 | [-0.267, 0.211] | [-0.278, 0.223] | -0.26 | 0.6085 | 0.517 / 0.442 |
| H5 FOMC post | 61 | -0.110 | -0.327 | 0.039 | -0.149 | [-0.490, 0.210] | [-0.532, 0.223] | -1.30 | 0.1002 | 0.607 / 0.543 |
| H6 CPI post | 91 | -0.070 | -0.251 | 0.005 | -0.074 | [-0.322, 0.194] | [-0.327, 0.217] | -0.73 | 0.2330 | 0.659 / 0.551 |

Robustness (H1 unless stated; not separate hypotheses):

| Variant | n | Mean | Median | Placebo mean | Excess | Excess CI iid | Excess CI block | z | p | Hit / placebo hit |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| H1 ex top-1 |change| | 148 | -0.119 | -0.269 | 0.024 | -0.143 | [-0.339, 0.060] | [-0.316, 0.036] | -1.81 | 0.0345 | 0.635 / 0.547 |
| H1 ex top-3 |change| | 146 | -0.187 | -0.292 | 0.027 | -0.214 | [-0.392, -0.035] | [-0.377, -0.048] | -2.73 | 0.0031 | 0.644 / 0.547 |
| H1 ex extrapolated VX30 | 137 | -0.072 | -0.257 | 0.022 | -0.094 | [-0.315, 0.134] | [-0.286, 0.119] | -1.14 | 0.1230 | 0.642 / 0.548 |
| H1 log points | 149 | -0.005 | -0.013 | 0.001 | -0.006 | [-0.014, 0.002] | [-0.014, 0.002] | -1.95 | 0.0223 | 0.631 / 0.549 |
| H1 never-rescheduled events | 147 | -0.077 | -0.257 | 0.018 | -0.095 | [-0.312, 0.131] | [-0.283, 0.105] | -1.22 | 0.1140 | 0.626 / 0.549 |
| H2 ex top-1 | 148 | -0.039 | 0.015 | -0.043 | 0.004 | [-0.235, 0.239] | [-0.243, 0.238] | 0.04 | 0.4942 | 0.520 / 0.444 |
| H2 ex top-3 | 146 | -0.041 | 0.015 | -0.031 | -0.010 | [-0.229, 0.211] | [-0.238, 0.210] | -0.11 | 0.5432 | 0.521 / 0.442 |
| H2 log points | 149 | -0.002 | 0.001 | -0.001 | -0.001 | [-0.011, 0.008] | [-0.012, 0.009] | -0.33 | 0.6267 | 0.517 / 0.442 |
| H5 ex top-1 | 60 | -0.204 | -0.342 | 0.050 | -0.254 | [-0.552, 0.026] | [-0.588, 0.056] | -2.20 | 0.0126 | 0.617 / 0.539 |
| H5 ex top-3 | 58 | -0.205 | -0.342 | 0.047 | -0.253 | [-0.527, -0.003] | [-0.531, 0.010] | -2.14 | 0.0153 | 0.621 / 0.541 |
| H6 ex top-1 | 90 | -0.130 | -0.254 | 0.006 | -0.136 | [-0.363, 0.116] | [-0.370, 0.132] | -1.31 | 0.0953 | 0.667 / 0.551 |
| H6 ex top-3 | 88 | -0.225 | -0.257 | 0.012 | -0.237 | [-0.423, -0.034] | [-0.422, -0.039] | -2.26 | 0.0127 | 0.682 / 0.550 |

Largest |VX30 change| events (post window): 2021-01-27 (fomc, 5.51), 2025-04-10 (cpi, 5.38), 2020-05-12 (cpi, 4.23).

Strictly tradable check (surviving monthly contract, 7-calendar-day rule; short for the post leg, long for the pre leg; net = after 0.10 round trip):

| Leg | n | Mean change | Median change | Mean P&L gross | Mean P&L net | Sortino (net) | Worst net | Hit (net > 0) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| H1 post, short | 149 | -0.127 | -0.283 | 0.127 | 0.027 | 0.03 | -6.22 | 0.591 |
| H2 pre, long | 149 | -0.212 | -0.124 | -0.212 | -0.312 | -0.27 | -5.10 | 0.409 |
| H5 FOMC post, short | 61 | -0.170 | -0.357 | 0.170 | 0.070 | 0.07 | -6.22 | 0.574 |
| H6 CPI post, short | 91 | -0.107 | -0.276 | 0.107 | 0.007 | 0.01 | -5.35 | 0.615 |

H1 tradable by calendar days to F1 expiry at entry:

| Bucket | n | Mean change | Mean P&L net | Sortino (net) |
| --- | --- | --- | --- | --- |
| <=10 | 110 | -0.072 | -0.028 | -0.03 |
| 11-20 | 13 | -0.748 | 0.648 | 1.54 |
| >20 | 26 | -0.053 | -0.047 | -0.04 |

H1 per year:

| Year | n | Mean VX30 change | Excess | Hit | Tradable n | Tradable mean change | Tradable mean P&L net |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2019 | 19 | -0.336 | -0.336 | 0.737 | 19 | -0.421 | 0.321 |
| 2020 | 17 | 0.158 | -0.022 | 0.588 | 17 | 0.137 | -0.237 |
| 2021 | 20 | 0.131 | 0.197 | 0.600 | 20 | 0.057 | -0.157 |
| 2022 | 20 | -0.401 | -0.413 | 0.650 | 20 | -0.396 | 0.296 |
| 2023 | 20 | -0.275 | -0.204 | 0.650 | 20 | -0.314 | 0.214 |
| 2024 | 19 | 0.018 | -0.027 | 0.632 | 19 | -0.021 | -0.079 |
| 2025 | 19 | -0.111 | -0.199 | 0.789 | 19 | -0.184 | 0.084 |
| 2026 | 15 | 0.283 | 0.294 | 0.333 | 15 | 0.246 | -0.346 |

H3 magnitude (|VX30 change|):

| Window | n | Event median | Placebo median (draw mean / pooled) | Ratio | Ratio CI iid | p (median) | Event mean | Placebo mean | p (mean) | Median |tradable change| |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| post, j = 0 | 149 | 0.530 | 0.420 / 0.427 | 1.262 | [1.028, 1.561] | 0.0026 | 0.819 | 0.640 | 0.0010 | 0.597 |
| pre, k = 3 | 149 | 0.609 | 0.602 / 0.596 | 1.011 | [0.848, 1.189] | 0.4219 | 0.907 | 0.871 | 0.2870 | 0.600 |

H4 regime at t-1 (cfg v2 buckets, not refitted):

| Bucket | n | Mean change | Excess (points) | Excess (log) | Hit |
| --- | --- | --- | --- | --- | --- |
| high_vol | 52 | -0.061 | -0.103 | -0.0073 | 0.654 |
| normal | 64 | -0.144 | -0.148 | -0.0080 | 0.609 |
| low_vol | 33 | 0.007 | -0.006 | -0.0011 | 0.636 |

high_vol minus non-high_vol excess: -0.004 points (permutation p = 0.5000, n_high = 52, n_other = 97); log points -0.0017 (p = 0.4327).

Descriptive only (no pass claim): excess by basis sign at entry: f1 - VIX > 0 n = 96, excess -0.032; f1 - VIX <= 0 n = 53, excess -0.226. 2019-2021 n = 56, excess -0.050; 2022-2026 n = 93, excess -0.132. Years with negative pooled mean VX30 change: 4 of 8 (2019, 2022, 2023, 2025).

Verdicts against the registered thresholds (pass: excess <= -0.15, p <= 0.00208, tradable mean <= -0.20, sign kept ex top-3; kill: wrong sign, p > 0.10, sign flips ex top-1, tradable mean > -0.10; mirrored for H2):

| Hypothesis | Verdict | Pass conditions met | Kill conditions triggered |
| --- | --- | --- | --- |
| H1 | INCONCLUSIVE | d_sign_kept_ex_top3 | none |
| H2 | KILL | none | a_wrong_sign, b_p_gt_0.10, c_sign_flips_ex_top1, d_tradable_mean_lt_+0.10 |
| H5 | KILL | d_sign_kept_ex_top3 | b_p_gt_0.10 |
| H6 | KILL | d_sign_kept_ex_top3 | b_p_gt_0.10 |
| H3 | INCONCLUSIVE | ratio >= 1.25 and p <= 0.00208 | median <= placebo or p > 0.10 |
| H4 | INCONCLUSIVE | | |

Pre-registered MDE (80 percent power, alpha 0.00208): about 0.45 points; realised holdout SE of the mean VX30 change 0.102 points (sd 1.242, n 149), realised MDE 0.376 points.

Attempt log: 30 quantities computed on 2019+ data, all listed in `holdout.attempt_log` of the results JSON. In-sample grid evaluations: 8. Bonferroni m used: 24.

Deviations and interpretations recorded by the runner:

- The registered pre-leg window runs from settle t-k to settle t-1, so the grid point k = 1 is a zero-length window (t-1 to t-1): its change is identically 0 for events and placebos and its placebo z is 0/0. The registration's rule 'k* = the k with the most positive z' is undefined for it. The cell is kept in the fit table and in the attempt count (m = 24 unchanged) but is excluded from the argmax; k* is chosen among k in {2, 3, 5}. The window was not reinterpreted (for example as t-k-1 to t-1). Decided on fit-window information only, before any 2019+ number.
- Fit-phase placebo windows are restricted to end on or before 2018-12-31, so no 2019 settle enters the fit even as a baseline (the registration is silent on this).
- An event with no valid placebo offset (all 20 shifted windows touch another event's halo or leave the data) is dropped from that leg's statistics and listed; none of its outcomes is used.
- The placebo halo [e-3, e+3] is built from every scheduled FOMC / verified CPI date, including the two event dates that are not VX trading days (mapped to the next VX day).
- 'Excluding extrapolated VX30 dates' is read as excluding events whose entry or exit settle is a flagged extrapolation.
- The H3 placebo median is the mean of the 10,000 draw medians (the pooled median over all valid placebo windows is reported beside it); the pass ratio uses the draw-mean version.
- H5/H6 are judged with the H1 pass AND kill conditions on their subsample (the registration states the pass conditions explicitly and is read as mirroring the kill conditions).
- The 95 percent interval registered is an iid event bootstrap; a circular block bootstrap of the date-ordered per-event excess with about 21 trading days per block is reported beside it as asked by the workflow, and neither is a pass condition.
- Sortino uses the net P&L series with MAR = 0 (downside deviation = root mean square of negative net P&L).

Data flags: regime read from an earlier decision than t-1 on 0 events (none); tradable settle missing on 0 post windows and 0 pre windows; 12 post windows touch an extrapolated VX30; events without any valid placebo offset: post 0, pre 0.

**Overall: passed = false** (H1 INCONCLUSIVE, H2 KILL, H3 INCONCLUSIVE, H4 INCONCLUSIVE, H5 KILL, H6 KILL). Product-level kill condition not met (see individual verdicts).

Runner notes:

- Holdout invocation history: the first `--phase holdout` call aborted inside the pre-run fit-reproduction check (the frozen JSON stores floats rounded to 6 decimals and the check demanded 1e-9 agreement). That check runs before run_holdout, so no 2019+ quantity was computed; the tolerance was set to 1e-6 and the second call is the one and only holdout run. All holdout numbers are deterministic given the registered seed 20260927.
- The k = 1 pre-leg cell is a zero-length window under the registered definition (settle t-k to settle t-1); it was counted as an attempt (m = 24 unchanged) and excluded from the argmax on fit-window information only, before the fit was frozen and before any 2019+ number; k* = 3 was chosen among {2, 3, 5}.
- The registration anticipated regime counts at t-1 of high_vol 53 / normal 65 / low_vol 34 (sum 152, which appears to count the three same-day FOMC+CPI pairs twice); realised on the 149 distinct event days: 52 / 64 / 33.
- H1 sits between the kill and pass lines on every axis: excess -0.101 (pass needs <= -0.15, kill at >= 0); placebo p 0.0947 against the 0.10 kill line, with a Monte Carlo standard error of about 0.003 at 10,000 draws (normal approximation of z = -1.30 gives 0.097); tradable surviving-contract mean -0.127 against the -0.10 kill line and the -0.20 pass line. No reseeding or extra draws were made; the registered seed and draw count stand.
- H3: ratio of event to placebo median |VX30 change| 1.262 clears the 1.25 floor but the placebo p 0.0026 misses alpha_adj 0.00208 (Monte Carlo SE about 0.0005); it would have cleared the m = 6 alpha 0.0083 that the registration deliberately rejected, and is recorded as INCONCLUSIVE, not a pass.
- Verification: the H1 mean VX30 change (-0.0814) and the tradable surviving-contract mean (-0.1273) were re-derived with a separate plain-pandas computation of the same registered quantities and agree with the runner.
- The 'by calendar days to expiry at entry' breakdown uses calendar days to F1 expiry at t-1 (the registered covariate), not the surviving contract's days; 110 of 149 entries have F1 within 10 days because FOMC Wednesdays and mid-month CPI releases cluster near the monthly VX expiry.
- Product-level kill condition (H1 and H2 killed and H3 not passed) is formally not met because H1 is INCONCLUSIVE rather than KILLED; nothing passed, and the registration authorises no second look.

Summary: H1 INCONCLUSIVE (excess -0.101 points, 95 percent CI [-0.310, 0.115] iid / [-0.290, 0.099] block, p 0.0947, n 149; tradable short mean -0.127, +0.027 net of cost; registered MDE 0.45, realised 0.376); H2 KILLED (excess -0.026, wrong sign, p 0.61, tradable long -0.212); H3 INCONCLUSIVE (median ratio 1.262, p 0.0026); H4 INCONCLUSIVE (high_vol minus non-high_vol -0.004, permutation p 0.50); H5 FOMC KILLED (p 0.1002); H6 CPI KILLED (p 0.233). passed = false. Attempts: 8 in-sample grid cells (1 undefined), 30 holdout quantities logged, Bonferroni m = 24.

---

## Result: vx-roll-carry-5d

Runner: test runner for Design 1 (not the designer, not a judge). Script:
`scripts/research/basis_test.py` (read-only data access, all paths as arguments, seed 20260927).
Results: `scratchpad\basis_results.json`; fit freeze written before any 2019+ outcome:
`scratchpad\basis_fit_frozen.json` (2026-09-27T19:35:48Z). Holdout run started 19:35:48Z and
finished 19:35:59Z in the same process. **Verdict: KILL. passed = false.**

### Data checks (before any outcome)

- Input SHA-256: vx_curve.csv `93ffc173...ab29592`, vx_futures.csv `42af11c3...592b8ca1`,
  vix.csv `20e5b432...0187f2b4`, decisions.csv `a9db3178...78e28ac15`, Cboe VIX_History.csv
  `4bca3f49...74d52796` (full digests in the freeze file). Re-hashed unchanged on 2026-09-28.
- VIX cross-check: 3,986 shared dates ledger vs Cboe VIX_History.csv, 0 dates differing by more
  than 0.01 (max abs diff 0.000); no ledger date missing from Cboe and no Cboe date 2011-01-03 to
  2026-09-22 missing from the ledger.
- Held-contract settle pulled from vx_futures.csv by (trade_date, expiry): 0 missing, 0 mismatches
  against the curve's F1/F2 columns. Minimum TD_t on the strategy calendar 9 (so the same-contract
  5-day hold was always feasible; 0 infeasible signals skipped in fit or holdout).
- Strategy calendar: 3,954 VX dates with a VIX close, 2011-01-03 to 2026-09-22; excluded VX dates
  without VIX: 2015-04-03, 2018-12-05, 2026-09-23/24/25. The 32 holiday-session VIX rows never
  enter (they have no vx_curve row). Fit calendar 2,012 days, holdout calendar 1,942 days.

### Fit (2011-01-03 to 2018-12-31, exits <= 2018-12-31), attempts = 5

| theta | n | short/long | exposure | mean | sd | t | hit | median | worst | best | eligible |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0.125 | 131 | 103/28 | 32.6% | +0.219 | 2.019 | 1.24 | 61.1% | +0.30 | -6.925 | +7.95 | no (exposure) |
| 0.150 | 81 | 58/23 | 20.1% | -0.038 | 2.340 | -0.15 | 54.3% | +0.10 | -6.925 | +7.95 | yes |
| 0.175 | 48 | 29/19 | 11.9% | -0.104 | 2.790 | -0.26 | 56.3% | +0.20 | -6.925 | +7.95 | yes |
| 0.200 | 31 | 17/14 | 7.7% | +1.181 | 3.483 | 1.89 | 77.4% | +0.85 | -4.000 | +15.75 | yes |
| 0.250 | 10 | 3/7 | 2.5% | +2.343 | 4.974 | 1.49 | 70.0% | +1.13 | -1.450 | +15.75 | no (n) |

Chosen parameter: **theta = 0.200** (highest fit t among the eligible set {0.150, 0.175, 0.200};
no tie). Fit mean excluding the best trade (2011-07-29 long, +15.75) is +0.695; fit t = 1.89 is
itself below the one-sided 99 percent threshold, so the selected cell was never significant in
sample. The designer's predictor-only counts (80/48/31/10 trades and 129 at 0.125) are reproduced
within one or two trades; the differences (81, 131) come from the designer's probe applying the
regime gate by default, which the registered primary rule does not.

### Holdout (signal dates 2019-01-02 onward, ONE run at theta = 0.200)

Primary, n = 48 trades (27 short, 21 long), exposure 12.4 percent of 1,942 days, 6.2 trades per
year, 26 short and 18 long sign-run episodes on the signal series:

| metric | value | 95% interval (21-day block bootstrap unless stated) |
| --- | --- | --- |
| mean net P&L per trade (points) | **-0.556** (USD -556 per contract) | [-1.911, +1.111] daily series; [-1.697, +0.583] trade blocks |
| trade-level sd / t | 4.957 / -0.78 | |
| one-sided 99% lower bound, trade iid bootstrap (10,000) | -2.060 | two-sided 95% [-1.817, +0.909]; P(mean <= 0) = 0.784 |
| one-sided 99% lower bound, 63-day block bootstrap (10,000) | -1.822 | two-sided 95% [-1.641, +0.747]; P(mean <= 0) = 0.825 |
| mean excluding the single best trade (+20.375, long 2020-03-11) | **-1.002** | [-1.893, +0.174] |
| placebo (1,000 circular shifts, U{63..252} days) | real mean at the **6.7th** percentile | placebo p50 -0.111, p99 +0.639, mean n 47.9 |
| hit rate | 41.7% (20/48) | exact binomial [27.6%, 56.8%]; block [27.7%, 54.9%] |
| median net P&L | -0.176 | [-1.626, +0.314] |
| Sortino (MAR 0) | -0.176 | [-0.518, +0.177] |
| worst trade | -8.325 (long 2024-08-05, exit 08-13) | |
| largest 63-day drawdown of cumulative P&L | 31.7 points (full-period drawdown 71.6) | |
| MDE = 2.326 x sd / sqrt(n) | 1.66 points | power against a 0.5-point effect was far below design |

Pass conditions: (1) n >= 30: met. (2) 99 percent lower bound > 0 under both bootstraps: failed
(both negative). (3) mean ex-best > 0: failed. (4) real mean >= placebo 99th percentile: failed.
Kill conditions: (a) mean <= 0.10: met (-0.556). (b) mean ex-best <= 0: met (-1.002). (c) real mean
<= placebo 50th percentile: met (6.7th). All three kill conditions hold, so the rule is retired on
the 2019+ data; no redesign of this hypothesis is permitted on that data.

### Appendix (computed after the primary result was recorded; no pass consequence)

- Legs: short n = 27, mean +0.350, sd 1.333, t 1.36, hit 59.3% [38.8%, 77.6%], median +0.323,
  worst -2.876, mean ex-best +0.265. Long n = 21, mean -1.722, sd 7.273, t -1.08, hit 19.0%
  [5.4%, 41.9%], median -3.309, worst -8.325, best +20.375, mean ex-best -2.826. The loss is the
  long leg (steep backwardation, VIX 22-72): after 2020-03-11 every long trade but two lost.
- Gated (no short entry when regime = high_vol): n = 38 (17 short, 21 long), mean -0.901,
  hit 34.2%; the gate blocked 37 of 74 short signal days and removed 10 shorts that had netted
  +7.5 points. Paired difference gated minus ungated: -0.344 per trade, 63-day block 95% interval
  [-0.745, +0.067]. The level-rank regime removed the profitable shorts and kept the losing
  longs; the secondary null (the gate adds nothing) is not rejected and the point estimate is the
  wrong sign for the gate.
- Lag-0 entry: n = 50, mean -0.741 (worse than lag-1), so no sign of timing leakage.
- Cost 0.20: n = 48, mean -0.656.
- Always-short-held-contract benchmark, all days overlapping: n = 1,937, mean +0.149, t 2.38, hit
  61.4%, worst -32.0; non-overlapping consecutive 5-day: n = 388, mean +0.143, t 1.01.
- Unconditional contango-short (theta 0, basis > 0): n = 286, mean +0.142, t 1.41, hit 59.8%,
  exposure 73.6%, worst -9.5. Steepness selection made the short leg no better than unconditional
  carry and added a long leg that lost.
- All 5 grid cells on the holdout (non-registered, for the record): 0.125 n 140 mean +0.066;
  0.150 n 94 -0.087; 0.175 n 64 -0.166; 0.200 n 48 -0.556; 0.250 n 17 -0.280. No cell would have
  passed.
- Per year (n, mean): 2019 (1, -1.65); 2020 (16, +0.87); 2021 (11, -0.35); 2022 (5, -1.87);
  2023 (2, -0.05); 2024 (4, -4.81); 2025 (6, -0.54); 2026 (3, -1.04). Only 2020 is positive and
  its mean ex-best is -0.43.
- Exclusions: ex 2020-02-24..04-30 removes 7 trades (+13.875 net): n 41, mean -0.990, t -2.43.
  Ex 2024-08-01..16 removes 1 trade (-8.325): n 47, mean -0.391.
- VX30 21-trading-day forward change (descriptive): all dates n 1,921 mean -0.02, median -0.16;
  short-signal dates n 73 mean +2.20, median +1.87; long-signal dates n 60 mean -4.27, median
  -7.02. On the 21-day view VX30 moved against both legs.
- Basis F1 - VIX on signal dates: short +2.72, long -6.50; slope F2 - F1: short +2.47, long -3.96.
  Regime at signal on the 48 trades: high_vol 29, normal 13, low_vol 6, consistent with the
  designer's disclosure that high_vol co-occurs with steep curves in 2019+.

### Deviations from the registration and other disclosures

1. TD_t is undefined on the last 14 strategy-calendar dates (2026-09-02 to 2026-09-22): the held
   contract there is the Oct-2026 expiry, which lies beyond the last vx_curve.csv date
   (2026-09-25), so the registered trading-day count is truncated. roll_t was set to undefined and
   no signal taken on those dates; 9 of them (09-02 to 09-14) could otherwise have completed a
   trade by 2026-09-22. Last signal date with a defined roll: 2026-09-01. Recorded, not silently
   adapted.
2. The registration specifies the 63-day block bootstrap for the one-sided 99 percent bound; the
   workflow additionally asked for 95 percent intervals with 21-day blocks. Both are reported;
   the 21-day intervals are additional reporting and change no pass or kill condition.
3. The VIX cross-check used a copy of Cboe's VIX_History.csv downloaded 2026-09-27 into the
   scratchpad rather than a live fetch; 0 discrepancies.
4. Computation log on 2019+ data: the primary run at theta 0.200 (2026-09-27 19:35Z), the two
   bootstraps, the 21-day intervals, the placebo, and the appendix items above, exactly as listed
   in `holdout_computation_log` of the results file. On 2026-09-28 the runner (a) re-executed the
   unchanged script with the same seed to a scratch path and confirmed identical results apart
   from timestamps, then deleted the scratch copy, and (b) independently recomputed every holdout
   trade's roll, held contract, entry/exit dates, settles and P&L from the raw files
   (`scratchpad\spotcheck_basis.py`: 48 of 48 reproduced, 0 missed signals, non-overlap
   confirmed). Neither changed a parameter or a rule; both are disclosed as executions that
   touched 2019+ data.
5. No sibling correction was needed: the result is a KILL at the uncorrected level.

### Reading

The steepness-conditioned basis trade did not survive out of sample: mean -0.556 points per trade
(USD -556 per contract), 41.7 percent hit rate, real mean below the placebo median, negative even
after removing the best trade. The short leg in steep contango earned +0.35 per trade (t 1.4), no
better than shorting the front contract unconditionally (+0.14 to +0.15 per 5 days at 4 to 6 times
the exposure), and the long leg in steep backwardation lost -1.72 per trade because spot VIX fell
back toward the curve faster than the future rose. The classifier's level-rank regime did not help
as a filter. Consistent with Quantpedia's out-of-sample note, the Simon-Campasano basis signal has
decayed; nothing here supports a "carry regime" product state, and a "vol expansion" reading is
contradicted by both legs.

---

## Result: r2-magnitude-vx30-state-vs-realized-baseline

Runner: `scripts/research/magnitude_test.py` (final run 2026-09-27T22:26 UTC, seed 20260927).
Results JSON: `scratchpad/magnitude_results.json`. A first run at 2026-09-27T19:37 UTC used
floating-point thresholds (deviation 6) and is kept as
`scratchpad/magnitude_results_run1_float_boundary.json`; it reached the same gate outcome and,
like the final run, computed no 2019+ outcome. Both runs are deterministic. Input hashes
(SHA-256), recorded before any statistic was computed: `vx_curve.csv` 93ffc173c99df67d...,
`vx_futures.csv` 42af11c33f0523b6..., `vix.csv` 20e5b432be9bf026..., `ovx.csv` e23844d4b07d5060...
(full digests in the results file; identical to the sibling runners' hashes for the shared files).

**Outcome: KILL-IN-SAMPLE in both markets. The holdout was NOT run. passed = false.** No 2019+
outcome (a state aligned with a forward VX30 or OVX move) was computed by this test for any rule,
horizon or market. Under the Kill section the feature family "spot level percentile as a
predictor of the magnitude of tradable volatility moves beyond recent realized" is retired: no
refit, no new grid on this feature, no second look at 2019+.

### Attempt count

18 attempts per market (9 grid points x 2 horizons), 36 in total, all evaluated on the fit window
2011-2018 only; zero attempts on the holdout. Correction as registered: one-sided alpha =
0.05/18 = 0.00278 per test (0.05/36 = 0.00139 as sensitivity). The gate applied p_21 <= 0.00278.

### Data gates (run before any statistic)

- Recomputed P_t equals `cls_dislocations.regime_percentile` (last row per NY date of
  `iv_observed_at_ms`): equity 3,986/3,986 dates, oil 3,955/3,955 dates, max |diff| 0.0; the
  window length equals `regime_history` on every date (2011-2015 ramp included); one date per
  market (2011-01-03) undefined in both.
- `vix.csv` closes equal `vx_curve.vix_close` on all 3,954 shared dates (0 mismatches), so no
  FRED one-day lag misaligns state and price.
- Calendar: 12 VX dates have no VIX close (7 in Dec 2010, 2015-04-03, 2018-12-05, 2026-09-23 to
  09-25) and 32 VIX holiday-session dates have no VX settlement; neither set is a signal date;
  the VX30 rows remain in the series for forward changes and baselines.
- Daily |log change| > 0.5 scan (price series only): VX30 2018-02-05 (+0.676) and same-contract
  F1 2018-02-05 (+0.754), the genuine 5 February 2018 event, kept; no VX30 move above 20 percent
  with the opposite sign to VIX, so no legacy settlement inconsistent with VIX was found. OVX:
  2020-03-09 (+0.578), 2020-04-20 (+0.858), 2020-04-30 (-0.622), 2021-11-26 (+0.636), all genuine
  oil events; these dates lie in the holdout period and this scan read OVX prices there, but no
  state-outcome pair.
- VX30 method counts: interp_F1F2 3,651, interp_F2F3 120, extrapolated 195.

### Fit window, VIX / VX30 (primary, tradable)

Signal dates 2011-01-24 to 2018-12-31, 1,998 rows (deviation 1). Unconditional medians of r:
0.7554 (h = 5), 0.6390 (h = 21), matching the registration's 0.755 / 0.639. Spearman(P_t, b_t)
= 0.528 (designer: 0.535). Eligibility requires n_eff,5 >= 30 and n_eff,21 >= 30.

| d | side | state days | fire % | episodes | n_eff 5 / 21 | EXCESS_5 | REL_5 | z_5 | p_5 | EXCESS_21 | REL_21 | z_21 | p_21 | eligible | mean z |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0.35 | high | 235 | 11.8 | 20 | 63 / 23 | +0.206 | +0.272 | +2.53 | 0.0120 | +0.090 | +0.140 | +0.93 | 0.1914 | no (n_eff21) | +1.73 |
| 0.35 | low | 673 | 33.7 | 36 | 171 / 52 | -0.114 | -0.151 | -2.45 | 0.9994 | -0.073 | -0.114 | -1.52 | 0.9648 | yes | -1.99 |
| 0.35 | both | 908 | 45.4 | 51 | 232 / 69 | -0.023 | -0.031 | -0.62 | 0.7375 | -0.031 | -0.048 | -0.94 | 0.7826 | yes | -0.78 |
| 0.40 | high | 157 | 7.9 | 19 | 45 / 19 | +0.171 | +0.227 | +1.67 | 0.0548 | +0.136 | +0.212 | +1.24 | 0.1348 | no (n_eff21) | +1.45 |
| 0.40 | low | 544 | 27.2 | 34 | 143 / 46 | -0.124 | -0.164 | -2.29 | 0.9888 | -0.087 | -0.136 | -1.60 | 0.9560 | yes | -1.95 |
| 0.40 | both | 701 | 35.1 | 52 | 188 / 61 | -0.055 | -0.073 | -1.21 | 0.8894 | -0.044 | -0.069 | -1.07 | 0.8364 | yes | -1.14 |
| 0.45 | high | 73 | 3.7 | 15 | 24 / 11 | +0.331 | +0.439 | +2.11 | 0.0352 | +0.199 | +0.312 | +1.21 | 0.1268 | no (both) | +1.66 |
| 0.45 | low | 368 | 18.4 | 30 | 105 / 36 | -0.086 | -0.113 | -1.24 | 0.8834 | -0.043 | -0.067 | -0.62 | 0.7259 | yes | -0.93 |
| 0.45 | both | 441 | 22.1 | 45 | 129 / 46 | -0.019 | -0.026 | -0.32 | 0.5883 | +0.004 | +0.007 | +0.07 | 0.4791 | yes | -0.13 |

Placebo sd of EXCESS (5,000 circular shifts): 0.033-0.060 for side = both, 0.047-0.069 for low,
0.081-0.164 for high, similar at both horizons; the registration's MDE figures (about 0.040 and
0.11 at d = 0.40) hold.

Selected (max mean z among the 6 eligible): d = 0.45, side = both, mean z = -0.13. Gate:
p_21 = 0.4791 > 0.00278 (fails) and EXCESS_5 = -0.019 <= 0 (fails). Not cleared. No row of the
9 x 2 table, eligible or not, has p_21 <= 0.00278 (the smallest is 0.1268 at 0.45 high), so the
in-sample kill does not depend on the eligibility filter. The only positive cells are the
one-sided high rules, none with 30 independent 21-day windows and none below p_21 0.12; the
low-side rules move LESS than unconditional days relative to recent realized (EXCESS_5 -0.09 to
-0.12, placebo p 0.88 to 0.9994), consistent with the vol-of-vol mean reversion the registration
named as a confound rather than with level information.

In-sample diagnostics of the selected VIX rule (0.45, both), published for the record; these are
fit-window figures, no holdout exists:

| metric | h = 5 | h = 21 |
| --- | --- | --- |
| state days / all days | 441 / 1,998 | 441 / 1,998 |
| n_eff (non-overlapping windows); episodes (gap 5) | 129; 45 | 46; 45 |
| median r state / all | 0.7360 / 0.7554 | 0.6432 / 0.6390 |
| EXCESS; REL | -0.0194; -2.6% | +0.0042; +0.7% |
| mean r state / all | 1.061 / 1.086 | 1.005 / 0.932 |
| p75 state / all; p90 state / all | 1.394 / 1.451; 2.276 / 2.262 | 1.184 / 1.189; 2.118 / 1.950 |
| placebo z; p (5,000 shifts) | -0.32; 0.6007 | +0.08; 0.4765 |
| 95% block bootstrap of EXCESS, 63-day blocks, 2,000 reps | [-0.131, +0.102] | [-0.121, +0.151] |
| 95% block bootstrap, 21-day blocks (sensitivity) | [-0.142, +0.095] | [-0.117, +0.144] |
| STRAT (baseline-decile stratified excess) | -0.136 | -0.117 |
| side split, high (73 days): EXCESS; n_eff; p | +0.331; 24; 0.035 | +0.199; 11; 0.133 |
| side split, low (368 days): EXCESS; n_eff; p | -0.086; 105; 0.876 | -0.043; 36; 0.718 |
| entry-day only (45 entries): EXCESS; p | -0.257; 0.970 | -0.209; 0.987 |
| same-contract F1 target: EXCESS; REL; p | -0.099; -12.3%; 0.961 | -0.074; -9.2%; 0.819 |
| VX30 excluding extrapolated dates (196 rows dropped): EXCESS; p | -0.062; 0.820 | +0.007; 0.430 |
| excluding the most volatile 63-day block (2015-07-28 to 10-23): EXCESS; p | -0.023; 0.649 | +0.010; 0.432 |
| excluding the block with the largest state contribution (2017-10-25 to 2018-01-25, 30 state days): EXCESS; p | -0.011; 0.561 | +0.007; 0.479 |

Fire rate 22.1 percent (the DEC-002 30 percent limit would hold; not a gate here). Per year
(state days; median r_21 state / all): 2011 48 (0.77 / 0.74), 2012 35 (0.43 / 0.57), 2013 78
(0.48 / 0.55), 2014 59 (1.04 / 0.77), 2015 3 (0.75 / 0.57), 2016 24 (0.86 / 0.59), 2017 156
(0.59 / 0.65), 2018 38 (1.02 / 0.72). The largest single episode by summed (r_21 - median) is
2017-12-07 to 2018-01-11 (17 days, +69.1), the compressed run into 5 February 2018; without its
block the result is unchanged (table above).

### Fit window, OVX (secondary, spot index, NOT tradable)

Signal dates 2011-02-02 to 2018-12-31, 1,991 rows (deviation 1). Unconditional medians of r:
0.7727 (h = 5), 0.6931 (h = 21), matching 0.773 / 0.693. Spearman(P_t, b_t) = 0.450.

| d | side | state days | fire % | episodes | n_eff 5 / 21 | EXCESS_5 | REL_5 | z_5 | p_5 | EXCESS_21 | REL_21 | z_21 | p_21 | eligible | mean z |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0.35 | high | 322 | 16.2 | 15 | 77 / 23 | +0.002 | +0.002 | +0.03 | 0.4857 | +0.067 | +0.097 | +0.64 | 0.2943 | no (n_eff21) | +0.33 |
| 0.35 | low | 466 | 23.4 | 9 | 102 / 29 | +0.072 | +0.093 | +1.72 | 0.0682 | -0.018 | -0.025 | -0.22 | 0.5715 | no (n_eff21) | +0.75 |
| 0.35 | both | 788 | 39.6 | 24 | 179 / 52 | +0.035 | +0.046 | +1.06 | 0.1426 | +0.010 | +0.014 | +0.22 | 0.4133 | yes | +0.64 |
| 0.40 | high | 240 | 12.1 | 13 | 60 / 20 | -0.045 | -0.058 | -0.59 | 0.7299 | +0.019 | +0.028 | +0.16 | 0.4627 | no (n_eff21) | -0.22 |
| 0.40 | low | 370 | 18.6 | 11 | 85 / 23 | +0.101 | +0.130 | +1.89 | 0.0436 | +0.042 | +0.061 | +0.49 | 0.3177 | no (n_eff21) | +1.19 |
| 0.40 | both | 610 | 30.6 | 24 | 145 / 43 | +0.035 | +0.046 | +0.80 | 0.2018 | +0.032 | +0.047 | +0.59 | 0.3041 | yes | +0.70 |
| 0.45 | high | 132 | 6.6 | 12 | 34 / 14 | -0.052 | -0.067 | -0.47 | 0.7289 | -0.043 | -0.062 | -0.25 | 0.6331 | no (n_eff21) | -0.36 |
| 0.45 | low | 232 | 11.7 | 12 | 59 / 19 | +0.200 | +0.259 | +2.68 | 0.0028 | +0.100 | +0.145 | +0.90 | 0.2258 | no (n_eff21) | +1.79 |
| 0.45 | both | 364 | 18.3 | 24 | 93 / 33 | +0.062 | +0.080 | +0.98 | 0.1872 | +0.057 | +0.082 | +0.70 | 0.2667 | yes | +0.84 |

Selected (max mean z among the 3 eligible): d = 0.45, side = both, mean z = +0.84. Gate:
p_21 = 0.2667 > 0.00278 (fails); EXCESS_5 = +0.062 > 0 (holds). Not cleared. The smallest p_21
in the OVX table is 0.2258 (0.45 low, n_eff,21 = 19). The one cell at the 5-day alpha (0.45 low,
p_5 = 0.0028 with 59 windows) is a 5-day result on a spot index whose 21-day arm has 19
independent windows and p 0.23; it is recorded as descriptive, not as evidence, and was not
carried anywhere. In-sample diagnostics of the selected OVX rule (0.45, both; 364 state days, 24
episodes, fire 18.3 percent): h = 5 n_eff 93, EXCESS +0.062 (REL +8.0%), p 0.1876, CI63 [-0.072,
+0.253], CI21 [-0.065, +0.237], STRAT +0.091; h = 21 n_eff 33, EXCESS +0.057 (REL +8.2%), p
0.2775, CI63 [-0.121, +0.314], CI21 [-0.112, +0.282], STRAT +0.038; side split high (132 days)
-0.052 / -0.043, low (232 days) +0.200 / +0.100; entry-day only (24) +0.150 / -0.073; excluding
the most volatile block (2014-08-06 to 11-03) +0.052 / +0.041; excluding the largest-contribution
block (2016-02-05 to 05-05) +0.052 / +0.004. Every interval contains zero.

### Verdict against the registered rules

- Fit-window gate (p_21 <= 0.00278 and EXCESS_5 > 0 for the selected eligible rule): FAILED in
  both markets. Per the Fit and Kill sections this is KILL-IN-SAMPLE: the state carries no
  in-sample magnitude information beyond recent realized vol-of-vol; the full 9 x 2 tables are
  published above.
- Holdout metrics (P1 to P6, K1 and K2, the per-year table, the largest-block exclusion, the
  same-contract F1 target on 2019+): NOT COMPUTED, by design. Nothing about 2019-2026
  state-conditional moves was looked at, so the 2019+ period stays clean for a pre-registration
  with a different feature.
- passed = false. Parameters chosen on the fit window (both markets): d = 0.45, side = both,
  chosen only to apply the gate; they carry to no holdout.
- What is refuted and what is not. On 2011-2018, once VX30 moves are taken in logs and scaled by
  the trailing 21-day realized mean absolute change, days on which the VIX level sits in the
  outer 5 to 15 percent of its own five-year distribution do not move more than other days at 5
  or 21 days, and the same-contract front future moves less; compressed states move less. The
  one-sided stretched rules show positive point estimates (+0.09 to +0.20 at 21 days, +0.17 to
  +0.33 at 5 days) but never with 30 independent windows and never below p_21 0.12; they are not
  evidence and were not tested out of sample. The test says nothing about option premium (no
  option data) and does not test term-structure or VVIX-relative features, which need their own
  registration.

### Deviations from the registration and undefined cases

1. Fit-window start. The registration names signal dates from 2011-01-03; b_{t,h} needs 21 daily
   log changes and the VX30 series begins 2010-12-22, so the first 13 VIX-calendar dates
   (2011-01-04 to 2011-01-21) and the first 20 OVX dates (2011-01-04 to 2011-02-01) have an
   undefined baseline and are not signal dates (1,998 VIX and 1,991 OVX fit rows). Recorded in
   the results file under `signal_dates_dropped_undefined_baseline`.
2. Eligibility exclusions are wider than the designer stated. The registration expected only VIX
   high at d = 0.40 and 0.45 and OVX high at d = 0.45 to fail n_eff,21 >= 30. Applying the rule
   as written also excludes VIX 0.35 high (23) and OVX 0.35 high (23), 0.35 low (29), 0.40 high
   (20), 0.40 low (23) and 0.45 low (19), leaving 6 eligible VIX and 3 eligible OVX
   combinations. The rule was applied as registered; no combination in either table clears
   p_21 <= 0.00278 regardless, so the outcome does not depend on this.
3. Same-contract F1 target. When both F1 and F2 expire on or before t+h (206 of the 3,945 21-day
   windows), the registration's "else the F2 contract" is undefined; the runner used the nearest
   monthly contract expiring strictly after t+h (F3), so no roll gap enters the return. Its
   baseline is the trailing 21-day mean absolute daily log change of the F1 contract held on each
   day. This affects only the in-sample robustness row.
4. "The single largest 63-day block" is ambiguous; two definitions are reported (the block with
   the largest mean absolute 21-day log move, and the block with the largest summed state-day
   contribution to EXCESS_21).
5. A 21-day-block bootstrap interval is reported next to the registered 63-day one, as the
   runner's brief asked; the 63-day interval is the registered statistic.
6. Threshold arithmetic. The first run compared P_t with 0.5 - d and 0.5 + d in floating point;
   0.5 - 0.45 evaluates to 0.04999..., which dropped the two fit rows with P_t exactly 0.05 from
   the VIX 0.45 low and both states (439 instead of 441 state days; no other fit cell in either
   market affected). The runner found this while cross-checking, added a 1e-9 tolerance so the
   comparisons are the registered <= 0.05 and >= 0.95, and re-ran the fit. Gate outcome and
   selection unchanged (p_21 0.4813 -> 0.4791, EXCESS_5 -0.020 -> -0.019 at the selected rule).
   The first run's file is kept for the record.
7. The runner's Spearman(P_t, b_t) is 0.528 for VIX against the designer's 0.535 (different first
   fit row); OVX is 0.450 in both.

No deviation changes the gate outcome: with 21-day blocks, with the F1 target, with either block
exclusion, with exact or floating-point thresholds, or with the designer's narrower eligibility
set, no rule reaches p_21 <= 0.00278 in the fit window.

---

## Result: vrp-stretched-state-compressed-premium

Transcribed on 2026-09-28 by the orchestrator from the runner's structured return, because the runner
did not append this section itself. Script: `scripts/research/vrp_test.py`; results JSON in the
session scratchpad (`vrp_results.json`). Nothing below was recomputed or edited.

- **Fit (2011-2018):** 3 grid cells; q* = 0.75 chosen (Welch t −1.000; 0.80 gave −0.642, 0.85 gave
  −0.991). Frozen before any 2019+ outcome.
- **Holdout (one run, start dates 2019-01-02 to 2026-08-26):** 581 signal days (30.2 percent), 45
  events against 71 non-signal windows. Mean log premium Y 0.323 on events against 0.305 on windows:
  Delta +0.018, t +0.256, one-sided p 0.601. Day-level Delta +0.036, 95 percent block interval
  [−0.067, +0.141]. P(Y < 0) 0.200 against 0.141, the only metric in the hypothesised direction,
  driven by two February and March 2020 events. One of eight years in direction. Power against the
  fit effect 15 to 30 percent.
- **Deviations:** fit cutoff 2018-11-28 instead of the registered 2018-11-30 so no 2019 SPX return
  enters the fit (literal cutoff computed too: identical selection); six Yahoo and Cboe SPX closes
  disagree in 2019 and 2021, reported with and without them; the fire rate 30.2 percent is marginally
  above the DEC-002 cap.
- **Verdict: KILL** (n ≥ 30 and Delta ≥ 0). **passed = false.** Adversaries: data lens not refuted,
  statistics lens not refuted.
