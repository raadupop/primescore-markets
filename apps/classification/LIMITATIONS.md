# Classification limitations

These limits apply even when the deterministic checks pass. Historical decisions
remain in [the component ADRs](doc/adr/).

## 1. Market history is a level-distribution approximation

The market strategy centers the retained `N_L` levels on their current median,
matching the independently calculated fixture targets. It does not reconstruct
each historical observation's deviation from its own trailing `N`-point median.
That distinction remains between this prototype and the historical-deviation
definition in [SRS §3](../../doc/srs/PrimeScore-SRS.md).

Reopen with independently reconstructed point-in-time reference distributions
and recalculated anchor expectations. Do not describe the current output as a
validated volatility forecast.

## 2. Formula conformance does not establish a trading signal

The low-volatility and normal-day anchors now verify signed arithmetic:
`−1115/1260 = −0.8849` and `−398/1260 = −0.3159`. Their former expected-failure
markers mixed formula conformance with an unresolved economic judgment; removing
those markers does not validate a noise filter, entry, holding period, liquidity,
or profit. Source records and historical values retain pending review status.

[ADR-0003](doc/adr/0003-test-oracle-architecture.md) defines the independent
market-outcome oracle; that backtest is unbuilt. Registry horizons are
operator-set, not established by out-of-sample calibration. The binomial
`N_L ≥ 278` calculation assumes independent observations; serial dependence and
regime changes can reduce effective sample size.

## 3. Parametric fitting is conditional and uncalibrated

[ADR-0004](doc/adr/0004-signed-severity-and-fit-rejection.md) defines the fitted
percentile and rejection behavior. Shapiro-Wilk acceptance does not establish
stationarity, independence, predictive accuracy, or tail calibration. The
available window is used as supplied; no algorithm selects a stationary
sub-window. For monthly classes, history sufficiency uses the configured `N`
because `N_L` is absent.

Log-Gaussian fitting rejects zero magnitudes; it does not delete zeros or add a
pseudocount. A rejected fit returns a signed diagnostic ECDF with reduced
certainty and an explicit rejection reason. That value is not a valid fitted
percentile. Reopen the family selection with independently sourced consensus
histories and calibration evidence.

## 4. Historical macro acceptance is incomplete

The two CPI fixtures and the initial-claims fixture lack the current signed
reference baseline and remain explicitly skipped. Synthetic acceptance cases
exercise signed surprises, an analytical fitted percentile, rejection and zero
handling; they do not replace historical validation. Macro source metadata does
not establish every historical consensus value's provenance.

## 5. Live bootstrap cannot complete with the default data coverage

Only FRED history fetching is implemented. The registry requires verified
windows for VIX, OVX, CPI and initial claims. Initial claims has no consensus
bootstrap; the static CPI consensus ends in December 2024. Missing or short
required windows leave `/health` at 503. Provider failures are logged; there is
no automatic retry or refresh loop.

`BOOTSTRAP_MODE=disabled` skips external requests and preserves an explicitly
loaded snapshot; it does not mark an empty service ready. Live bootstrap tests
require both `RUN_LIVE_BOOTSTRAP=1` and `FRED_API_KEY`.

## 6. State and symbol coverage are limited

Reference windows are process-local and `/classify` appends each observation.
Independent replay runs must restore their snapshots. Multiple workers do not
share history; restart discards it. Registry reload requires restart.

The .NET engine does not use process windows: it owns history in its ledger and
sends each request its own `reference_window`
([ADR-0005](doc/adr/0005-caller-supplied-reference-window.md)). The classifier
cannot verify that a supplied window is genuine or point in time.

Historical anchors cover VIX, OVX, CPI and initial claims; the remaining registry
symbols lack independent anchors. Shared class parameters do not demonstrate
equivalent behavior across class members.