# Session Notes — 2026-04-13: SENSITIVITY_FACTOR & Dislocation Exploitability

## Context

Continued from session `a9f29ab4` (Apr 12 night) which produced the SRS v2.3.1 alignment plan (14 gaps, 4 phases). This session explored SENSITIVITY_FACTOR from trader and risk perspectives.

---

## 1. What is SENSITIVITY_FACTOR?

A regime-aware multiplier in the IV dislocation formula (CLS-006):

```
SignalImpliedIV = MarketObservedIV x (1 + CompositeScore x SENSITIVITY_FACTOR)
Dislocation = SignalImpliedIV - MarketObservedIV
```

Configured via a map of volatility regimes:
```json
{"low_vol": 1.5, "normal": 1.0, "high_vol": 0.6}
```

- **Low-vol (VIX < 15):** Market complacent, more repricing room -> factor UP (1.5)
- **High-vol (VIX > 25):** Market already in panic, diminishing marginal impact -> factor DOWN (0.6)
- **Normal:** Baseline (1.0)

### Trader Concerns (from /trader)

1. Regime boundaries are step functions, not smooth curves — discontinuity at boundary (VIX 14.9 vs 15.1 = 50% sensitivity drop)
2. Factor values (1.5 / 1.0 / 0.6) are examples, not calibrated to anything
3. VIX term structure shape matters more than spot level
4. Needs backtesting against 2018-2024 historical events

---

## 2. Core Risk Challenge: Is a 10-Point Dislocation Exploitable?

**Question:** ~70% of market volume is institutional/algorithmic. How can an event remain unpriced long enough for PrimeScore AI to exploit?

### Latency Stack Reality

| Step | Latency | |
|------|---------|---|
| Event occurs | T+0 | Everyone sees simultaneously |
| Institutional algos reprice | T+50ms to T+2s | Options surface adjusts |
| PrimeScore AI classifier processes | T+seconds to minutes | Rolling window + HTTP to Python |
| PrimeScore AI detects dislocation | T+minutes | Composite -> IV comparison |
| PrimeScore AI places order | T+minutes+ | Retail broker API |

**Conclusion:** By the time PrimeScore AI computes a 10-point dislocation, it no longer exists.

### When Dislocations DO Persist

| Scenario | Dislocation | Duration | PrimeScore AI Exploitable? |
|----------|-------------|----------|-------------------|
| Standard macro release (CPI, NFP) | Repriced <2s | Milliseconds | No |
| Sudden geopolitical event | 5-15 pts | Seconds to minutes | No — too slow |
| Novel/unclassifiable crisis | 10+ pts mispriced | Hours to days | Maybe — but classifier also struggles |
| Structural vol suppression | 1-3 pts | Days to weeks | **Most realistic edge** |
| Cross-asset lag | 2-5 pts | Minutes to hours | **Yes — if using non-VIX instruments** |
| Multi-signal synthesis | 2-5 pts | Minutes to hours | **Possible — this is the real thesis** |

### Realistic PrimeScore AI Operating Zone

- Dislocations of **1-4 points** (not 10)
- In **less liquid vol instruments** (not VIX itself)
- Persisting for **30 minutes to several hours**
- Edge comes from **signal combination**, not speed

### Requirements Before Deploying Capital

1. Realistic dislocation thresholds: 1.5-3 pts for VIX-adjacent, 3-5 for less liquid instruments
2. Latency budget: measure actual event-to-order time; if >60s, competing on synthesis not speed
3. Backtest against execution reality: actual fills with spread widening, not mid-price
4. Accepted risk statement: "PrimeScore AI does not compete on latency. The edge is in multi-source signal synthesis during novel events."

---

## 3. Implementation Plan Status (from previous session)

The SRS v2.3.1 alignment plan was produced but execution was interrupted mid-Phase 1. Status:

- **Phase 1 (OpenAPI spec):** Partially applied — Changes 1.1-1.3 were being implemented
- **Phase 2 (Root CLAUDE.md + README.md):** Not started
- **Phase 3 (Classification CLAUDE.md):** Not started
- **Phase 4 (Classification code):** Design only, deferred

See previous session for the full 14-gap plan.
