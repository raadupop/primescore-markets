# Math Concepts Reference

Concepts used in PrimeScore AI classification service, explained from first principles.

**Update log.**
- 2026-04-14 — initial file (§1–§7: exponential decay, normalization, clamping, multiplicative vs additive, discounting, z-score, tanh).
- 2026-04-19 — ECDF-era additions (§8–§16). z-score (§6) and tanh (§7) marked DEPRECATED per [ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md) and SRS CLS-001.
- 2026-04-23 — §9 Deviation added (after §8 ECDF). Canonical definition per SRS §3. §9–§16 renumbered to §10–§17.
- 2026-04-30 — §18–§23 added (signed deviation, long-horizon reference window, parametric fallback, high-conviction bypass percentile, corroboration window, duration-scaled source-dropout penalty) per current SRS CLS-001 / CLS-002 / EXT-004. §11 IQR-floor `D` discussion softened — `D` is fully removed in current SRS and retained here as historical only. Doc cross-references de-versioned.

---

## 1. Linear vs Exponential Decay

Two ways something can decrease over time.

**Linear decay** — drops by the same AMOUNT each period:
```
100 → 80 → 60 → 40 → 20 → 0
    -20   -20   -20   -20   -20
```
Like a candle burning: loses the same amount of wax per hour. Hits zero and stops.

**Exponential decay** — drops by the same PROPORTION each period:
```
100 → 37 → 14 → 5 → 2 → 0.7
    ×0.37  ×0.37  ×0.37  ×0.37  ×0.37
```
Like hot coffee cooling: loses heat fast at first, then slows down. Never quite reaches room temperature.

**When to use which:**
- Linear: when decrease is constant (paying off a loan at fixed rate, countdown timer)
- Exponential: when the decrease is proportional to what's left (information decay, radioactive decay, interest rates, options theta, volatility mean reversion)

**Formula:** `e^(-x)` where e ≈ 2.71828

| x | e^(-x) | Meaning |
|---|---|---|
| 0 | 1.0 | No decay |
| 0.5 | 0.61 | Moderate decay |
| 1.0 | 0.37 | ~63% lost |
| 2.0 | 0.14 | ~86% lost |
| 3.0 | 0.05 | ~95% lost |

**Used in finance:**
- Black-Scholes: `e^(-rT)` discounts strike price over time
- Theta decay: options lose value exponentially as expiration approaches
- VIX mean reversion: spikes decay fast at first, then slow down
- Bond pricing: `PV = FV × e^(-rt)` — continuous discounting
- EWMA: exponentially weighted moving averages for vol estimation

---

## 2. Normalization

Converting raw values into comparable units by dividing by a reference value.

**Problem without normalization:**
- MARKET_DATA is 3 days overdue = 259,200 seconds
- MACROECONOMIC is 1 month overdue = 2,592,000 seconds
- Both are "1 period late" but raw seconds are wildly different
- `exp(-259200)` = effectively zero. Raw seconds are too large.

**Solution:** Divide by the expected frequency:
```
normalized = staleness / expected_frequency
```
- 259,200 / 259,200 = 1.0 (1 period late)
- 2,592,000 / 2,592,000 = 1.0 (1 period late)

Now both produce `exp(-1) = 0.37`. Same "lateness", same decay.

**General principle:** When comparing values on different scales, divide each by its own baseline to get a ratio. The ratio is unit-free and comparable.

**Other examples:**
- Z-score: `(value - mean) / std` — normalizes by how spread out the data is
- Percentage change: `(new - old) / old` — normalizes by starting value
- BMI: `weight / height^2` — normalizes weight by body size

---

## 3. Clamping (max / min)

Forcing a value to stay within a range.

**`max(0.0, x)`** — if x goes below 0, force it to 0:
```
max(0.0, 5)   = 5    (no change, already positive)
max(0.0, -3)  = 0    (clamped, was negative)
```

**`min(1.0, x)`** — if x goes above 1, force it to 1:
```
min(1.0, 0.7) = 0.7  (no change, already below 1)
min(1.0, 1.5) = 1.0  (clamped, was too high)
```

**Combined — clamp to [0, 1]:**
```
max(0.0, min(1.0, x))
```

**Why it matters:** Mathematical formulas can produce values outside the valid range. Clamping prevents nonsensical outputs:
- `exp(-negative)` = number > 1.0 — temporal relevance shouldn't exceed 1.0
- Certainty shouldn't be negative
- Scores must stay in [0.0, 1.0]

---

## 4. Multiplicative vs Additive Combination

Two ways to combine independent dimensions into one score.

**Additive (weighted average):**
```
certainty = 0.5 × history_sufficiency + 0.5 × temporal_relevance
```
- HS=1.0, TR=0.0 → certainty = 0.5 (one perfect dimension masks a zero)
- "At least one good dimension gives partial credit"

**Multiplicative:**
```
certainty = history_sufficiency × temporal_relevance
```
- HS=1.0, TR=0.0 → certainty = 0.0 (zero in any dimension kills the result)
- "Both dimensions must be good for the result to be good"

**When to use which:**
- Multiplicative: when dimensions are prerequisites (need BOTH fresh data AND enough history)
- Additive: when dimensions are complementary (a bit of A can compensate for less B)

**Analogy:**
- Multiplicative = AND logic: "I need a valid passport AND a visa to enter"
- Additive = partial credit: "Score is 50% exam + 50% coursework"

---

## 5. Discounting

Reducing the value of something because of a condition (time, risk, quality).

**Core idea:** Something worth X today may be worth less under certain conditions.

**Examples:**
- **Time discount:** $100 next year is worth ~$95 today (at 5% rate). The further away, the less it's worth now. Formula: `PV = FV × e^(-rt)`
- **Staleness discount:** A VIX reading from 3 hours ago is worth less than one from 3 seconds ago for trading decisions
- **Source discount (PrimeScore AI d_c):** A composite score is discounted when expected data sources are missing — you have less information, so trust the result less
- **Risk discount:** A trade with higher risk is "discounted" — you need a bigger expected return to justify it

**In PrimeScore AI:** Temporal relevance IS a discount factor. It takes the "base certainty" (history sufficiency) and discounts it for staleness:
```
certainty = history_sufficiency × temporal_relevance(discount)
```

---

## 6. Z-Score

> **DEPRECATED (2026-04-19).** PrimeScore AI no longer uses z-score as an intermediate severity measure. Replaced by ECDF rank (§8) per [ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md) and SRS CLS-001. Section retained as audit trail.

How many standard deviations a value is from the mean.

```
z = (value - mean) / standard_deviation
```

**What it tells you:** "How unusual is this value compared to recent history?"

| z-score | Meaning | In a normal distribution |
|---|---|---|
| 0 | Exactly average | 50% of values are below this |
| 1 | 1 std above mean | ~84% below — somewhat unusual |
| 2 | 2 std above mean | ~97.7% below — unusual |
| 3 | 3 std above mean | ~99.9% below — rare |
| 6+ | Extreme | Shouldn't happen under normal conditions |

**PrimeScore AI usage:** MARKET_DATA strategy computes z-score of current VIX value against 20-day rolling window. Volmageddon produced z=27.5 (VIX jumped from ~10 to 37 against a calm window).

**Why it's useful:** It's normalized (see concept #2). A VIX move from 10 to 15 and a move from 30 to 35 are both +5 points, but the first is far more unusual relative to its recent history. Z-score captures that.

---

## 7. tanh (Hyperbolic Tangent)

> **DEPRECATED (2026-04-19).** PrimeScore AI no longer uses `tanh(|z|/scale)` for severity compression. The fitted `_TANH_SCALE` constant assumed a time-invariant distribution, which no vol indicator has across 2017–2024. Replaced by ECDF rank (§8) per [ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md) and SRS CLS-001. Section retained as audit trail.

A function that maps any number to the range (-1, 1). PrimeScore AI uses `tanh(|x|)` to map to [0, 1].

```
tanh(0) = 0
tanh(1) = 0.76
tanh(2) = 0.96
tanh(3) = 0.995
tanh(∞) = 1.0
```

**Shape:** S-curve. Starts flat near 0, rises steeply in the middle, flattens near 1.

**Why PrimeScore AI uses it:** Raw z-scores are unbounded (can be 6, 27, 100+). Severity needs to be [0, 1]. `tanh` compresses large values toward 1.0 without hard-clipping them — a z-score of 27 maps to 0.88, not 1.0, preserving the information that "even more extreme" is possible.

**The scale parameter:** `tanh(z / scale)` controls where the curve is steep vs flat. A larger scale (20 for MARKET_DATA) means higher z-scores are needed to reach high severity. A smaller scale (3 for MACROECONOMIC) means the curve is more sensitive.

---

## 8. Empirical Cumulative Distribution Function (ECDF) / ECDF Rank 

A way to say "where does this value sit in its history?" without assuming the history follows any particular distribution shape.

**Core idea.** You have a history window of `N` past values. For a new value `x`, count how many of those past values are `≤ x`. Divide by `N`. That's the ECDF rank.

```
history = [10, 12, 11, 15, 13, 22, 14]   (N = 7)
new value x = 16
count of history ≤ 16:  [10, 12, 11, 15, 13, 14] → 6 out of 7
ECDF rank = 6/7 = 0.857
```

**What it tells you:** "86% of the last 7 values were at or below this one." Equivalent phrasing: "this is roughly at the 86th percentile of recent history."

**Left-continuous convention for ties.** When the new value equals some history values, the convention matters. PrimeScore AI uses `≤` (left-continuous): ties count toward the rank. A value exactly equal to the previous 3 values gets rank `3/N`, not `0/N`. This keeps the rank monotone-non-decreasing as `x` grows.

**Why PrimeScore AI uses it.**

- **Distribution-free.** Works whether the history is Gaussian, fat-tailed, skewed, bimodal, or weird. No assumption about shape.
- **Bounded output by construction.** Always in `[0, 1]`. No compression function (tanh) needed. No magic scale constant.
- **Regime-adaptive.** As the history window rolls forward, the ECDF updates automatically. A VIX of 25 during calm times gets a high rank; during a crisis window that already absorbed 40s, the same VIX of 25 gets a low rank.

**Contrast with z-score (§6, deprecated).** Z-score assumes standard deviation is a meaningful yardstick — true only for roughly symmetric, roughly Gaussian distributions. Vol indicators are neither. ECDF doesn't care.

**Formula (PrimeScore AI CLS-001):** `severity = ecdf_rank(|deviation|) / N`.

---

## 9. Deviation

The unsigned distance between an observed value and a strategy-specific reference value, computed without rescaling by standard deviation. This is the raw input to the ECDF severity formula (§8).

**Why not z-score.** Z-score divides by standard deviation: `z = (value − mean) / std`. That requires `std` to be stable and meaningful. Vol indicators are fat-tailed, regime-shifting, and serially correlated — `std` inflates after a spike and deflates in quiet regimes, silently distorting what "2 sigma" means. Deviation skips the division entirely. Magnitude is kept in native units; the ECDF then ranks it within its own history. The distribution shape does not matter.

**Per-category definitions (SRS §3):**

| Source category | Formula | Reference value |
|---|---|---|
| MARKET_DATA | `\|current_value − rolling_median(history)\|` | Rolling median of the history window |
| MACROECONOMIC | `\|actual_value − consensus_expected_value\|` | Analyst consensus estimate |
| CROSS_ASSET_FLOW | `\|pairwise_correlation − rolling_baseline_correlation\|` | Rolling baseline correlation (§14) |

GEOPOLITICAL signals do not produce a deviation. Severity for GEOPOLITICAL is LLM-judged (CLS-003).

**`deviation_kind`.** An enumeration stored in the indicator registry (§16) that records which of the three definitions above applies to a given indicator class: `LEVEL_VS_MEDIAN`, `SURPRISE`, or `CORR_DEVIATION`. The classifier reads this from the registry at runtime — no conditional logic on source category in the formula itself.

**Worked example (MARKET_DATA).** VIX closes: history window = `[14.0, 14.5, 15.1, 13.8, 14.2, ...]` (N=60 days). Rolling median = 14.3. Today VIX = 28.6.
```
deviation = |28.6 − 14.3| = 14.3
```
ECDF then ranks 14.3 against the 60 history values of `|VIX − 14.3|`. If 57 of those are smaller, `ecdf_rank = 57`, `severity = 57 / 60 = 0.95`.

**Worked example (MACROECONOMIC).** CPI YoY: actual = 9.1%, consensus estimate = 8.8%.
```
deviation = |9.1 − 8.8| = 0.3 percentage points
```
ECDF ranks 0.3 against the N-period history of `|actual − expected|` for that indicator class.

**Relationship to §8 (ECDF), §11 (IQR), §17 (D).** Deviation is the value passed into `ecdf_rank()`. IQR is computed over the history of deviations to measure dispersion. `D` is the minimum IQR below which the history is too flat to trust. All three concepts work together in CLS-001 and CLS-009.

---

## 10. Percentile Rank

ECDF rank expressed as a human-readable percentile.

```
ECDF rank = 0.857  ⟺  "87th percentile"  ⟺  "p87"
ECDF rank = 0.95   ⟺  "95th percentile"  ⟺  "p95" (top 5%)
ECDF rank = 0.99   ⟺  "99th percentile"  ⟺  "p99" (top 1%)
```

**Why the traders talk this way.** Percentile rank is regime-relative and unit-free. "VIX deviation is at p95 against the trailing 60 days" means **95% of recent observations were at or below this one** — equivalently, it sits in the **top 5%** of the recent window. The rank communicates signal strength without requiring the listener to know raw levels, units, or the reference median that produced it.

**Careful with the phrasing.** "p95" does NOT mean "it has been this high 95% of the time." It means the opposite: it has been this high (or higher) only 5% of the time.

**PrimeScore AI ranks deviation, not the raw level — and these differ.** On a typical trading desk "VIX at p95" refers to the *level* of VIX against its trailing history. PrimeScore AI ranks `|deviation from median|` (§9), not the raw level. They diverge in important cases:

- VIX grinds from 15 → 20 over a month. The median drifts up with it. Deviation stays small → low severity, even though the absolute level is elevated.
- VIX spikes overnight from 15 → 28 while the 60-day median is still 14. Deviation = 14 points → high severity. Both approaches agree: this is extreme.

**The rank is sufficient for signal strength, not for execution.** A consumer can act on `severity = 0.95` knowing the move is statistically extreme for this regime. But execution still requires the raw level — bid-ask spreads, term structure shape, and position sizing all depend on whether VIX is at 25 or 45, even if both have the same deviation rank.

**In PrimeScore AI.** ECDF rank IS a percentile (just on a 0–1 scale). `severity = 0.95` means "this signal's `|deviation|` is in the top 5% of its own recent history."

---

## 11. Interquartile Range (IQR)

A robust measure of how spread out a set of values is.

**Recipe.**

1. Sort the values.
2. Find Q1 (25th percentile — cuts off the bottom quarter).
3. Find Q3 (75th percentile — cuts off the bottom three-quarters).
4. IQR = Q3 − Q1.

```
history = [10, 11, 12, 13, 14, 15, 22]   (sorted)
Q1 = 11 (cuts bottom 25%)
Q3 = 15 (cuts bottom 75%)
IQR = 15 − 11 = 4
```

**What it tells you:** "The middle half of the values spans 4 units."

**Why IQR, not standard deviation.** Standard deviation gets destroyed by outliers — one VIX spike from 10 to 50 makes std explode, making every other value look "normal" afterward. IQR ignores the top and bottom 25% by construction. A single spike doesn't move it.

**Historical note on `D` in PrimeScore AI.** Earlier drafts of CLS-009 specified
a minimum-informative-dispersion floor `D` measured in IQR, intended to
catch flat-window pathologies. **`D` was removed from the current SRS**
and replaced by the global window-degeneracy guard described in §17 —
distinct-value count, not dispersion magnitude. IQR is retained in
PrimeScore AI vocabulary because it remains the right tool for *describing*
spread robustly; it is no longer the harness's flat-window check.

---

## 12. Rolling Median

The median of the last `N` values, recomputed every period.

**Recipe.** Sort the last `N` values. Take the middle one. (For even `N`, take the average of the two middle ones.)

```
history = [10, 12, 11, 15, 13]   sorted → [10, 11, 12, 13, 15]
median = 12
```

**Why median, not mean.** For level-anomaly deviation (`|current − reference|`):

- **Mean** gets pulled toward outliers. A recent spike from 10 to 40 inflates the mean, making the spike "look less anomalous" in future readings because the reference moves toward it. **Spike-absorbing-into-mean pathology.**
- **Median** doesn't move much from an outlier. A single 40 in `[10, 12, 11, 15, 13, 40]` gives median = 12.5, barely budged. The reference stays anchored to "normal."

**In PrimeScore AI.** MARKET_DATA deviation is `|current_value − rolling_median(history)|`. The rolling median is the "normal level" against which deviation is measured.

---

## 13. Pairwise Correlation (Pearson ρ)

How two return streams move together. Ranges from −1 (perfectly opposite) through 0 (unrelated) to +1 (perfectly together).

**Formula.**

```
ρ(X, Y) = covariance(X, Y) / (std(X) × std(Y))
```

- Covariance: average of `(xᵢ − x̄) × (yᵢ − ȳ)` — positive when X and Y deviate in the same direction, negative when they deviate oppositely.
- Dividing by the two standard deviations normalizes to `[−1, +1]`.

**Intuition.**

- `ρ = +0.9`: SPY and QQQ. They go up and down together.
- `ρ = 0`: SPY and gold-miner stocks (some periods). Roughly independent.
- `ρ = −0.6`: SPY and VIX. Inverse relationship — when stocks fall, vol rises.

**In PrimeScore AI.** CROSS_ASSET_FLOW strategy doesn't care about the *level* of correlation. It cares about *changes* in correlation (see §13).

---

## 14. Rolling Baseline Correlation

The correlation of two return streams measured over a trailing window, recomputed each period.

**Recipe.**

```
baseline_ρ(t) = Pearson correlation of (X, Y) over the last N periods ending at t
current_ρ(t)  = Pearson correlation of (X, Y) over a shorter recent window ending at t
deviation      = |current_ρ(t) − baseline_ρ(t)|
```

**What trips the signal.** SPY and VIX normally have `ρ ≈ −0.7`. If that relationship breaks (suddenly drops to `ρ ≈ −0.2` or flips positive), something unusual is happening in the market — often a liquidity event or a regime change. The `deviation` is how much the current correlation has departed from its rolling baseline.

**In PrimeScore AI.** CROSS_ASSET_FLOW deviation (CORR_DEVIATION kind) is `|current_ρ − rolling_baseline_ρ|`. The ECDF of that deviation over history produces severity.

---

## 15. Distribution-Free / Regime-Adaptive

Two properties of ECDF that together explain why it replaced tanh.

**Distribution-free.** Does not assume the underlying data follows any particular distribution shape (Gaussian, exponential, power-law, etc.). The ECDF rank is just a count — it works on any data.

- Z-score assumes symmetric, roughly Gaussian. VIX is not.
- tanh with a fitted scale assumes the scale is stable across time. It isn't — VIX regime of 2017 and VIX regime of 2020 have wildly different dispersions.
- ECDF makes no such assumption.

**Regime-adaptive.** As market conditions change, the trailing history window changes with them, so the ECDF rank recalibrates automatically.

- In a calm regime (VIX 10–15), a VIX of 25 is rank ≈ 1.0 (off the top).
- In a crisis regime (VIX 30–50), a VIX of 25 is rank ≈ 0.0 (below the window).
- Same raw value, opposite signal strength — because the signal is *relative to the prevailing regime*, not absolute.

**Trade-off.** ECDF loses the ability to say "this is a 5-sigma event in absolute terms." It only tells you "this is extreme for the recent window." For vol-exploitation trading, the relative view is what matters — you're looking for *dislocations from current normal*, not deviations from a long-dead baseline. This is intentional.

---

## 16. Indicator-Class Pooling

Why PrimeScore AI calibrates per *class* (group of symbols), not per symbol.

**Motivation.** Every symbol needs calibration parameters (`N`, `D`, `deviation_kind`). With 50+ symbols, per-symbol calibration means 50+ parameter rows to maintain and validate. Pooling symbols that behave the same statistically collapses those to a handful of classes.

**The pooling criterion is empirical, not taxonomic.** This is the subtle part. It is tempting to say:

- "VIX and VVIX are both equity-vol indices → same class."
- "WTI and Brent are both crude spot → same class."
- "CPI and PCE are both US inflation prints → same class."

**/trader caveat: this is often wrong.** Class membership is a statistical question, answered by testing whether the two symbols' `|deviation|` distributions pool without shifting the p95 rank by more than some tolerance. Test with Kolmogorov–Smirnov or Anderson–Darling; reject pooling on failure.

- **VIX vs VVIX.** VIX measures 30-day S&P 500 implied vol. VVIX measures the implied vol *of VIX itself* — vol of vol. The distributions have different tails, different regime structure, different jump behavior. VVIX routinely spikes on days VIX barely moves. **Likely fails the pooling test.**
- **WTI vs Brent.** 95% of the time they move together and pool fine. But during pipeline events (Aramco attack, Druzhba contamination), one can gap 10% while the other moves 2%. **Pooling works most days, fails exactly when it matters most.**
- **CPI vs PCE.** Different reference bases, different surprise distributions. Pool-validation is non-obvious.

**Realistic PrimeScore AI universe size.** A naive asset-taxonomy pooling gives 15–30 classes. An empirically validated pooling (with KS rejection) probably gives **30–50 classes** across equity IV, rate vol, crude spot, gold, FX pairs, inflation prints, growth prints, employment prints, correlation pairs. The registry needs to absorb that, not be fixed-size.

**Bottom line.** Classes are defined by distributional equivalence under deviation, proven by test — not by "they look similar to a human."

---

## 17. Window-Degeneracy Guard (replaces per-class `D`)

A global check that the history window has enough distinct values for the ECDF rank to be meaningful, regardless of dispersion shape.

**The pathology.** Suppose OVX has been trading in a range of 22.0 to 22.3 for 60 days — flat regime. A modest move to 22.5 ranks above every one of the 60 history points → **ECDF rank = 1.0, severity = 1.0**. The classifier screams. The market yawns.

**The guard (ADR-0002, 2026-04-27 amendment).** Compute the count of distinct values in the history window after rounding to 4 decimals. If that count is below a global threshold `k_min` (= 10), the history is declared degenerate:

- The classifier returns a **CLS-009 degraded-confidence response** instead of the raw ECDF severity.
- `computed_metrics.window_degenerate = true` so the composite (CLS-002) can exclude it deterministically.

**Why `k_min` and not a per-class IQR floor (`D`).** A defensible empirical-p5 estimate of an IQR distribution requires ≥ 200 non-overlapping IQR observations, i.e. `200 × N` raw points. For monthly inflation at `N = 60`, that is 1,000 years of data — unreachable. Any per-class `D` set without that data is a placeholder, not a calibrated parameter. The `k_min` threshold instead is sample-size independent and distribution-free: 10 distinct values is the resolution floor below which a percentile rank cannot resolve finer than 10 percentage points, period. `/statistician` rejects per-class `D` calibration at the available sample sizes; `/trader` accepts the global threshold because it lines up with desk experience for what counts as "enough variation to call a tail."

**Why rounding to 4 decimals.** Matches the significance of the inputs — FRED daily closes carry 2 decimals, pct-change `|deviation|` carries at most 4–5 significant decimals after division. Rounding to 4 squashes float noise without conflating genuinely distinct values.

**Trade-off made explicit.** The guard introduces false-negatives in genuine slow-burn regime-change events (OVX grinding upward from 22 to 25 over a month, all distinct values). It will *not* trip then, because the history has plenty of distinct values — that is the correct behaviour, those grinds should produce real ranks. The guard's only job is to catch the pathological case where the history has structurally collapsed.

---

## 18. Signed Deviation and Sign Convention

ECDF rank gives severity *magnitude*. CLS-001 (current SRS) also requires
severity *direction* — vol-expansion vs vol-compression — so the
composite (CLS-002), the IV dislocation (CLS-006), and the position
constructor (POS-001) can natively distinguish the two opportunity
types without re-deriving direction from auxiliary fields.

**Definition.** `deviation_signed` is `deviation` (§9) without the
absolute-value bars:

| Source category | `deviation_signed` |
|---|---|
| MARKET_DATA | `current_value − rolling_median(history)` |
| MACROECONOMIC | `actual_value − consensus_expected_value` |
| CROSS_ASSET_FLOW | `pairwise_correlation − rolling_baseline_correlation` |

By construction `\|deviation_signed\| ≡ deviation`.

**Sign convention.** Per-strategy rules in SRS §3 attach a meaning to
the sign:

- `+` severity → vol-expansion opportunity (signal-implied volatility
  *above* market).
- `−` severity → vol-compression opportunity (signal-implied
  volatility *below* market).

For MARKET_DATA on a vol indicator (VIX / OVX), a positive
`deviation_signed` is a vol-expansion signal (the vol index is above
its rolling median). For MACROECONOMIC on a hawkish-surprise indicator
(CPI YoY), a positive `deviation_signed` (actual hotter than expected)
is a vol-expansion signal. The convention is per-class, registered
alongside `deviation_kind`.

**The composite (CLS-001 v current).** Severity is the signed ECDF rank
in `[-1.0, +1.0]`:

```
severity = sign(deviation_signed) × ecdf_rank(|deviation_signed|) / N_L
```

Magnitude in `[0, 1]`, sign in `{−, +}`, product in `[-1, +1]`.

**Why one signed scalar, not two unsigned ones.** Splitting severity
into a magnitude field and a direction enum was rejected: every
downstream consumer would have to recombine them, the OpenAPI surface
doubles, and an ambiguous direction (zero magnitude with a "direction"
field) becomes representable. A single signed scalar is the minimal
encoding.

---

## 19. Long-Horizon Reference Window (`H_L`, `N_L`)

Earlier ECDF drafts ranked `|deviation|` against a short rolling
window (`N` = 20–60 days). The current SRS replaces that with a
long-horizon reference window `H_L` of length `N_L`, with `N_L`
**bounded below by binomial standard error** to ensure the p99
percentile rank is meaningful.

**The pathology the long horizon fixes — window absorption.** Suppose
VIX gaps from 14 to 28 (large dislocation). The short window absorbs
the spike. A second identical move days later ranks *lower* than the
first because the window has internalised it. Severity falls while the
underlying physics has not changed. The 2017-10-05 / 2019-07-15 anchor
fixtures surfaced this directly: two structurally identical moves,
divergent severities, no rule that distinguished them.

**The binomial-SE bound.** The ECDF rank of an out-of-sample value
near the p99 percentile has standard error roughly
`√(p(1-p)/N) ≈ √(0.01 × 0.99 / N)`. Demanding that the standard error
be ≤ 0.0059 (so that a "p99" rank is statistically distinguishable
from "p98") gives `N ≥ 278`. The current SRS pins `N_L ≥ 278` for any
indicator class that supports a daily or higher cadence.

**Practical consequence.** Bootstrap depth scales with `N_L` —
classifier startup pulls ≥ 278 days of history per qualifying symbol,
not the legacy 20-day window. This shifts cost onto bootstrap, in
exchange for a regime-stable rank.

**What `N_L` does not fix.** Event-clustering / autocorrelation. A
post-event ramp still produces rank decay if the events repeat inside
`H_L`. The SRS treats this as a residual limitation, not a bug.

---

## 20. Parametric Fallback

Some indicator classes cannot reach `N_L ≥ 278` even with full
historical bootstrap — monthly macro releases (CPI YoY, PCE) at one
print per month would need 23+ years of clean history per class, which
the providers don't supply uniformly. For those classes, the current
SRS specifies a **parametric fallback**: fit a parametric distribution
(typically Gaussian or Student-t) to whatever `H_L` is available, and
compute the percentile rank from the fitted CDF rather than the empirical
ECDF.

**Trade-off, made explicit.**

- *Cost.* Introduces a distributional assumption that the empirical
  ECDF deliberately avoided. A misfit (e.g. Gaussian on a fat-tailed
  surprise distribution) silently distorts the rank.
- *Benefit.* Produces a usable severity rank where the alternative is
  no severity at all (or a CLS-009-style degraded response on every
  print, which makes the indicator class operationally dead).

**Routing.** The fallback is selected by the indicator registry, not
inferred at request time. A class flagged `parametric_fallback = true`
runs the fitted-CDF path; classes with sufficient `H_L` run pure
ECDF. CLS-009's degraded-confidence response remains available for
classes whose `H_L` is too thin even to fit.

**Implementation hint.** The fitted distribution is refreshed on
bootstrap and at registry reload; not re-fit per request.

---

## 21. High-Conviction Bypass Percentile

CLS-002 (current SRS) requires *corroboration* before a single signal
escalates the composite — a cross-source check that prevents a single
noisy print from triggering a position. The current SRS adds a
**high-conviction bypass**: a single signal whose severity magnitude
exceeds a global percentile threshold `p_bypass` is exempted from the
corroboration requirement.

**Why a percentile, not a `kσ`.** The underlying `|deviation|`
distributions are fat-tailed and non-stationary. A `kσ` threshold
defines the bypass relative to a moving (and possibly distorted)
standard deviation; a percentile defines it relative to the long-horizon
ECDF rank itself, which is invariant to distribution shape.

**Default.** `p_bypass = 0.999` (top 0.1% of the long-horizon
distribution). Configurable per deployment.

**Failure mode it accepts.** A genuine top-0.1% signal that is in fact
a data error will bypass corroboration. The bypass is a deliberate
asymmetry: the cost of missing a true tail event is large enough to
tolerate occasional false-positive bypass triggers, which downstream
risk controls (POS-001 sizing, EXT-004 vega-crush gate) should
ultimately catch.

---

## 22. Corroboration Window

The same-category corroboration rule in earlier CLS-002 drafts
required an additional signal in the *same* `source_category` within
an unspecified time window. The current SRS replaces "same category"
with **event-typology-dependent corroboration windows** matched to
realistic PrimeScore AI latency:

- **Intraday market-data events** (vol spike, correlation break) —
  corroboration window on the order of 1–5 seconds, acknowledging the
  HTTP-coupled .NET → Python boundary.
- **Macroeconomic releases** — corroboration window on the order of
  30–60 minutes, acknowledging that related prints arrive on a release
  calendar, not in lockstep.
- **Cross-asset flow events** — somewhere in between, depending on the
  basket's typical lead-lag structure.

**Why typology, not a fixed window.** A 1-second window for a CPI
print rejects every plausibly corroborating signal (NFP doesn't print
the same second). A 60-minute window for a vol spike admits unrelated
signals from later in the session. Matching the window to the typology
keeps the corroboration rule operationally meaningful.

---

## 23. Duration-Scaled Source-Dropout Penalty `d_c(Δt)`

CLS-002's source-dropout penalty `d_c` discounts the composite when a
required source category is unavailable. Earlier drafts used a static
`d_c = 0.7` for any unavailability; the current SRS replaces this with
a **monotone-decreasing schedule** in elapsed unavailability `Δt`:

| `Δt` | `d_c(Δt)` |
|---|---|
| `Δt < 5 min` | 0.95 |
| `5 ≤ Δt < 30 min` | 0.7 |
| `Δt ≥ 30 min` | 0.5 |

**Why duration-scaled, not static.** A 30-second blip in a streaming
source is operationally different from a 30-minute outage. A static
penalty over-punishes the blip and under-punishes the outage. The
schedule restores monotonicity: longer outages discount harder.

**Configurable per deployment.** The schedule above is the default;
the registry can override per-class.

**Bound.** `d_c ∈ [0, 1]` always; the composite remains in
`[-1.0, +1.0]` after multiplication.
