# ADR-0004: Signed severity and explicit fit rejection

**Status:** Proposed
**Date:** 2026-09-22
**Deciders:** Radu Pop, operator

## Context

Unsigned ranks discard the distinction between observations above and below a
reference. Existing acceptance axioms detect this loss, nonzero output at zero
deviation, and missing confidence degradation after parametric fit rejection.
The SRS also described a percentile using the conflicting term “right-tail
probability”; a survival probability decreases with magnitude while a
percentile increases.

## Decision

The RULE_BASED score is the raw mathematical sign of the deviation times its
magnitude percentile, with `sign(0) = 0`. A fitted percentile is a cumulative
probability, consistent with the ECDF path and the amended
[SRS](../../../../doc/srs/PrimeScore-SRS.md).

Gaussian fitting estimates population location and scale; log-Gaussian fitting
does the same after taking logarithms of strictly positive magnitudes.
Shapiro-Wilk tests the standardized fit residuals at a configurable significance
level, default `0.05`. Invalid support, insufficient observations, zero spread,
or failed goodness of fit reject the fitted result. No sample values are
discarded or shifted to force a fit.

A rejected fit retains the signed empirical rank as a diagnostic and multiplies
certainty by the configured degradation factor, default `0.5`. The response
identifies the rejection. Short-window degeneracy applies the same factor once
and preserves severity, as required by CLS-009. Unknown indicators return zero
score and zero certainty with an explicit indicator flag.

API acceptance controls independently check an analytical Gaussian percentile,
opposite signs, exact zero, horizon-based confidence, and fit rejection. Source
fixtures retain their historical values and expected bands; expected-failure
markers may be removed only when the stated arithmetic independently agrees.

## Consequences

Deterministic arithmetic and failure handling become demonstrable. A passing
normality test supplies no evidence of stationarity, independence or economic
usefulness. The level-history approximation, empirical calibration and source
coverage remain explicit in [LIMITATIONS](../../LIMITATIONS.md).

## References

- [ADR-0002](0002-ecdf-severity-and-backtest-harness.md): ECDF and registry.
- [ADR-0003](0003-test-oracle-architecture.md): independent validation boundaries.
- [SciPy Shapiro-Wilk](https://docs.scipy.org/doc/scipy-1.15.3/reference/generated/scipy.stats.shapiro.html): normality test and sample limits.
- [SciPy normal distribution](https://docs.scipy.org/doc/scipy-1.15.3/reference/generated/scipy.stats.norm.html): cumulative and survival probabilities.
