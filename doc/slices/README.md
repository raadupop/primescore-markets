# Slices

A slice is one question a customer asks, answered on dashboard screens, from data to UI. Each
slice has one spec here, written before its code. Build order and the direction each slice
serves: "event-driven market intelligence connecting macroeconomic, geopolitical and market
signals to volatility dislocations, options strategies and position risk".

| # | Slice | The screens answer | Direction served | Spec |
| --- | --- | --- | --- | --- |
| 1 | Volatility state | Where does volatility stand, and what followed past extremes? | Market signals; dislocation of the level against its own history | [01](01-volatility-state.md) |
| 2 | Events ahead | What is scheduled, and how is the short end priced before it? | Event-driven; macro and OPEC dates; pre-event term-structure dislocation | [02](02-events-ahead.md) |
| 3 | Event record | What did options price before each event, and what happened? | Dislocation of priced against actual move; strategy outcomes estimated from indices | [03](03-event-record.md) |
| 4 | Your trade | What do the events before my expiry mean for my position? | Position risk (SRS EXT-004 inputs measured by slice 3) | to write |
| 5 | Daily brief | What matters today? | Market intelligence, delivered | to write |
| 6 | Proof | Which claims are under forward test? | Credibility of every slice | to write |
| 7 | Macro and world context | Was the number a surprise, and what else is going on? | Macro surprise; geopolitical shock days as events | to write |
| 8 | Options lab | How did real option structures do around events? | Options strategies on licensed option prices | to write |

## Spec shape

1. **Question** and the screens that answer it.
2. **Requirements**: SRS IDs only. Requirements live in the [SRS](../srs/PrimeScore-SRS.md), never here.
3. **Design**: modules, sources, API paths, ADRs. Decisions live in [ADRs](../adr/), never here.
4. **UI acceptance**: the automated tests that load each screen, then a click-through on the demo
   ledger with the exact values to see.
5. **Deferred**: [registry](../todo/registry.yaml) IDs.

## Done

A slice is done when `bash harness/check-suite.sh` passes, the operator has run the click-through
and recorded the date in the spec, and the slice is committed. The next slice starts after that.

## Demo ledger

The live ledger is append-only, so click-throughs run on a copy:

```powershell
.\scripts\build-demo-ledger.ps1                    # copy + state-gate replay; prints the replay id
.\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db               # frozen data
.\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db -PullSources  # live feeds
```

Values in a click-through come from the copy built on the date the spec names. A rebuilt copy
with newer data changes counts that depend on later closes; each spec says which.
