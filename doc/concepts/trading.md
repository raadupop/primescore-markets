# Trading Concepts Reference

Concepts used in PrimeScore AI position construction and decision logic, explained from first principles.

**Update log.**
- 2026-04-23 — initial file (§1: Long Straddle).
- 2026-04-30 — §2–§4 added (catalyst-relative exit, Vega-crush gate, expected Gamma-Vega ledger) per current SRS EXT-004.

---

## 1. Long Straddle

Buying both a call and a put on the same underlying, same strike, same expiry.

**What you own.** Two options:
- A **call** — right to buy the underlying at the strike price. Profits if the underlying rises above strike.
- A **put** — right to sell the underlying at the strike price. Profits if the underlying falls below strike.

You hold both simultaneously. You paid a premium for each.

**Why you'd do this.** You believe the underlying is about to move significantly — but you don't know which direction. The call captures an upward move; the put captures a downward move. You profit if the move is large enough in *either* direction to recover the total premium paid.

**Payoff at expiry.** Let `S` = price of the underlying at expiry, `K` = strike, `C` = call premium, `P` = put premium, `Total cost = C + P`:

```
Payoff from call  = max(S − K, 0)
Payoff from put   = max(K − S, 0)
Total payoff      = max(S − K, 0) + max(K − S, 0)
                  = |S − K|

Net P&L           = |S − K| − (C + P)
```

You make money if the underlying moves more than `C + P` away from the strike in either direction.

**Breakeven points.** Two of them, symmetric around the strike:

```
Upper breakeven = K + (C + P)
Lower breakeven = K − (C + P)
```

If the underlying stays between the two breakevens at expiry, the trade loses money. If it breaks out of either side, the trade profits.

**Example.** VIX options, strike = 25, call premium = 3.0, put premium = 2.5. Total cost = 5.5.

```
Upper breakeven = 25 + 5.5 = 30.5
Lower breakeven = 25 − 5.5 = 19.5
```

If VIX expires at 35: call pays 10, put pays 0, net P&L = 10 − 5.5 = **+4.5**.
If VIX expires at 15: put pays 10, call pays 0, net P&L = 10 − 5.5 = **+4.5**.
If VIX expires at 25: both options expire worthless, net P&L = **−5.5** (maximum loss).

**Maximum loss.** Always the total premium paid (`C + P`). Occurs when the underlying expires exactly at the strike — both options are worthless. Known and bounded from entry.

**Maximum gain.** Theoretically unlimited on the upside (underlying can keep rising, call payoff grows without bound). On the downside, capped at `K` (underlying can only fall to zero) minus premium. In practice vol instruments don't go to zero, so both sides have large but finite upside.

**The enemy: theta (time decay).** Options lose value every day they are held, all else being equal. A straddle is long two options — it decays twice as fast as a single position. Every day the underlying doesn't move is a day that eats into your premium. Theta is the constant opponent of a long straddle.

```
Daily theta decay ≈ −(C_theta + P_theta)
```

The trade is only profitable if the move happens *faster* than theta erodes the position.

**The friend: vega (implied volatility).** If implied volatility rises after you buy the straddle, both options become worth more — even if the underlying hasn't moved yet. This is the core PrimeScore AI edge: if the system detects that market IV is underpriced relative to signal-implied IV (CLS-006 IV dislocation), buying a straddle before the IV repricing captures the vega gain regardless of direction.

```
Vega P&L ≈ (IV_new − IV_entry) × (call_vega + put_vega)
```

**When PrimeScore AI opens a long straddle.** The position constructor (POS-001) selects LONG_STRADDLE when:
- IV dislocation is positive (signal-implied IV > market observed IV) — market is underpricing risk.
- The event has directional uncertainty — the underlying could spike in either direction (geopolitical shocks, surprise economic prints, central bank pivots).
- The exploitability window is wide enough that theta decay does not consume expected vega gain before the move occurs.

**The Iran war example (Feb 27, 2026).** OVX = 64.68, ECDF severity = 0.85. Signal suggests crude-oil vol is being underpriced ahead of the weekend military strike. The direction is uncertain — crude could gap up (supply disruption, Hormuz closed) or gap down (demand destruction, recession pricing). LONG_STRADDLE on USO/OVX options captures either outcome. The risk: if the market has already priced in the strike by Friday close and OVX opens Monday unchanged or lower (geopolitical risk premium evaporates), both options expire worthless or near-worthless — the full premium is lost.

**Contrast with directional spreads (CALL_SPREAD, PUT_SPREAD).** A call spread profits only if the underlying rises past the lower strike. A put spread profits only if it falls past the upper strike. Both cost less than a straddle (you sell one option to partially fund the other), but you must have a directional view. A straddle is more expensive and profits from size of move regardless of direction. PrimeScore AI picks between them based on whether the triggering signal is directionally ambiguous or directionally biased.

**Liquidity note.** Not all option markets have the depth a straddle requires. VIX options are liquid enough. OVX options are substantially thinner — bid-ask spreads widen exactly during the high-vol events PrimeScore AI wants to trade. The theoretical edge from a 0.85 severity score shrinks or disappears if the fill is 2 points wide. POS-001 must account for available liquidity when sizing legs.

---

## 2. Catalyst-Relative Exit

Exit timing keyed to a known **catalyst** — a scheduled, pre-announced
event whose outcome will mechanically reprice implied volatility — rather
than to a fixed clock-time horizon.

**The problem with fixed-horizon exits.** A long straddle entered three
days before an FOMC meeting and exited "after 5 trading days" prices the
exit on a calendar that ignores why the position exists. Two failure
modes:

- *Exiting before the catalyst.* The vega gain PrimeScore AI targets is
  precisely the IV repricing the catalyst forces. Closing before the
  meeting throws away the trade's reason to exist.
- *Holding past the catalyst.* Once the catalyst prints, IV typically
  collapses (see §3, Vega-crush gate). Theta still ticks. Holding past
  the print means paying the worst part of the vega curve voluntarily.

**The catalyst-relative exit rule (current SRS EXT-004).** Exit timing
is parameterised relative to a calendar of known catalysts (FOMC, CPI,
NFP, central-bank decisions, scheduled earnings, OPEC+ announcements).
For a position whose thesis is *"signal-implied IV is underpriced ahead
of catalyst X"*:

- Entry occurs `T_entry` units of time before catalyst X (configurable
  per event class).
- Exit occurs at `t_X + T_post` — a small, controlled window after the
  catalyst print, designed to capture the IV-repricing move without
  overstaying.

**Default `T_post` is short by design.** IV typically reprices in
minutes-to-hours after a scheduled print, not days. A long `T_post`
re-introduces the calendar-driven failure modes above.

**What about unscheduled catalysts.** Geopolitical shocks (military
strike, airstrike, regulatory surprise) are not on the calendar; they
trigger CLS-003 EVENT_ASSESSMENT signals, not catalyst-relative exits.
EXT-004 handles them via a separate non-catalyst exit pathway.

---

## 3. Vega-Crush Gate

A pre-trade check that prevents PrimeScore AI from holding a long-vega position
across a scheduled catalyst whose realised IV-repricing is expected to
be **negative**.

**What IV crush is.** Implied volatility is forward-looking. Ahead of a
binary scheduled event (an FOMC decision, an earnings print), market
makers price IV up to compensate for the uncertainty. Once the event
prints — regardless of direction of the underlying — uncertainty
collapses, and IV mechanically reprices down. This is *vega crush*: the
P&L any long-options holder eats simply because the calendar passed
through the print.

**Why it matters for PrimeScore AI.** A long straddle is long vega. If POS-001
opens a straddle on Tuesday and the position is still open on Wednesday
afternoon when CPI prints, the straddle's value drops by the *vega ×
ΔIV_crush* amount on the print, before the underlying has had time to
move. If the underlying *also* fails to move enough to recover the
crush, the trade loses the full vega-crush amount on top of theta.

**The gate.** Before opening a long-vega position, EXT-004 compares the
position's expected holding period against the catalyst calendar:

- If a scheduled catalyst falls inside the holding period **and** the
  position's vega is positive **and** the signal-implied IV does not
  exceed market IV by enough to absorb the expected post-print crush,
  the gate **blocks** the position.
- If the position is structured to *capture* the IV repricing (entry
  before catalyst, catalyst-relative exit per §2), the gate permits it
  — that's the vega-positive trade PrimeScore AI wants.

**Gate parameters.** Expected post-event IV crush is sourced from
historical realised crush distributions for the same indicator class
(CPI prints, FOMC days, earnings of the same liquidity tier). Not a
fixed magic number; a per-class calibration that lives in the
indicator registry alongside `N_L` and `deviation_kind`.

**What the gate does not protect against.** Mid-position
unscheduled catalysts (a surprise statement between meetings, an
intra-day geopolitical shock). Those are not on the calendar; the
exit-side controls (stop-loss, severity-driven re-evaluation) are
the line of defense.

---

## 4. Expected Gamma–Vega Ledger

A position-level accounting surface introduced in EXT-004 to track the
**expected** Greek exposures the trade was sized for, against the
**realised** Greek exposures actually accumulated as the position
moves through its holding period.

**The two Greeks the ledger tracks.**

- *Vega.* Sensitivity of the position's value to a 1-vol-point change
  in implied volatility. Long straddles are long vega; the trade thesis
  for PrimeScore AI is "vega gain on IV repricing dominates theta loss." The
  ledger tracks how much vega the position actually carries each day,
  given that gamma exposure decays as the underlying moves.
- *Gamma.* Sensitivity of *delta* to changes in the underlying. A
  straddle is gamma-long; its delta swings (and thus its instantaneous
  P&L sensitivity) as the underlying moves around the strike.

**Why a ledger.** A straddle entered ATM with an expected 5-day hold
has a specific *expected* path through (vega, gamma, theta) space. As
the position trades, reality diverges:

- Underlying moves away from the strike → gamma collapses, vega declines.
- Underlying stays at the strike → gamma stays high, but theta accumulates.
- IV moves (in either direction) → vega P&L realises immediately.

Without a ledger, the position-management decision ("close now? roll?
hold to catalyst?") is taken against gut feel about *current* Greeks,
disconnected from what the trade was actually sized for.

**What the ledger records.** Per position, per period:

- *Expected.* `(vega_expected, gamma_expected)` over the planned holding
  path, derived from the entry conditions plus the catalyst calendar
  (§2).
- *Realised.* `(vega_realised, gamma_realised)` measured at end-of-day
  from the actual position state and current market vol surface.
- *Divergence.* The two-vector delta. Used by EXT-004 as one of the
  inputs to the *re-evaluate* path (alongside severity decay and
  vega-crush gate evaluation).

**Why this is a *ledger* and not just a check.** Aggregated over many
trades, the divergence series is the empirical answer to *"is PrimeScore AI
sizing positions for the Greek exposure it actually realises?"* If
realised vega is systematically below expected, the IV repricing
thesis is not surviving contact with execution — and POS-001's sizing
formula (or the entry timing) is the place that needs revisiting.
This is a feedback signal into the harness's Backtest Layer A, not
just per-position bookkeeping.

**Out of scope for the ledger.** Theta and rho. Theta is deterministic
and already priced into the entry decision; rho is negligible at PrimeScore AI
holding horizons.
