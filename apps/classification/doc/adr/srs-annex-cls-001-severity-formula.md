# SRS Annex — CLS-001 severity formula (stub)

- **Status:** **Superseded (2026-04-19).** The normative text of this
  annex is now inline in the [SRS](../../../../doc/srs/PrimeScore-SRS.md)
  §3 Definitions (deviation, ECDF rank, indicator class, indicator
  registry, `N`, window-degeneracy guard) and §5.2 CLS-001 / CLS-009.
  The indicator registry is anchored in §3 Definitions, not as
  SIG-001.1. This file is retained as review-audit text only — do not
  edit. Canonical source is the SRS.
- **Date:** 2026-04-18 (superseded 2026-04-19)
- **Relates to:** [ADR-0002](0002-ecdf-severity-and-backtest-harness.md), CLS-001, CLS-009 (RULE_BASED degraded-confidence fallback), CLS-003 (GEOPOLITICAL LLM path, out of scope for ECDF), CLS-004 (AI-response validation — disjoint from this annex)

This annex defines the normative formula for per-signal severity produced
by the classification service's RULE_BASED strategies. It is the missing
formula piece of CLS-001 and resolves the self-validating-loop bug class
documented in ADR-0002. Once the SRS is revised, CLS-001 will reference
this annex; the text here is authoritative in the interim.

## Applicability

| Strategy | `source_category` | `deviation` | Applies |
|---|---|---|---|
| MARKET_DATA level-anomaly | `MARKET_DATA` | `\|current − rolling_median\|` | Yes |
| MACROECONOMIC surprise | `MACROECONOMIC` | `\|actual − expected\|` | Yes |
| CROSS_ASSET_FLOW correlation deviation | `CROSS_ASSET_FLOW` | `\|pairwise_correlation − rolling_baseline_correlation\|` | Yes |
| GEOPOLITICAL (structured / unstructured) | `GEOPOLITICAL` | n/a — LLM-judged | **No** |

The `deviation` variable differs by strategy; the mapping from
`|deviation|` to severity is identical.

## Normative formula

```
severity = ecdf_rank(|deviation|) / N
```

- `ecdf_rank(x)` = count of values in the history window `H` with
  `|value_i| ≤ x`, using the standard left-continuous empirical CDF rank
  convention.
- `H` is the rolling history of length `N` of the same `|deviation|`
  quantity for the same indicator.
- `severity ∈ [0.0, 1.0]` is the resulting `score` field of the
  classification response when `score_type = ANOMALY_DETECTION`.

The formula is distribution-free and regime-adaptive: a move's rank
adjusts automatically as the indicator's distribution shifts. It
eliminates the scale constant (`_TANH_SCALE`) that ADR-0002 retires.

## Window-degeneracy guard

Let `distinct(H)` be the count of unique values in the history window
after rounding to 4 decimals.

```
if distinct(H) < k_min:        # k_min = 10, global (ADR-0002, 2026-04-27 amendment)
    classification_method = RULE_BASED
    score                 = ecdf_rank(|deviation|) / N        # computed as usual
    computed_metrics.window_degenerate = true
    certainty            *= DEGRADED_FACTOR                    # CLS-004 fallback shape
```

Rationale: a percentile rank over fewer than 10 distinct values has
resolution coarser than 10 percentage points; below that the rank is not
informative regardless of the underlying distribution. The ADR-0002
2026-04-27 amendment supersedes the per-class IQR floor (`D`) that the
earlier draft of this annex described. The new check is sample-size independent and
distribution-free, so it works at the sample sizes available for monthly
and weekly series, where a calibrated `D` could not be defended.

## Registry schema

Each indicator class is registered as:

```yaml
indicator_class:
  N: <int>                 # history-window length
  deviation_kind: enum     # LEVEL_VS_MEDIAN | SURPRISE | CORR_DEVIATION
```

The registry is a closed universe. Symbols not present in the registry
trigger the CLS-004 degraded-confidence fallback (see "Unknown
indicators" below).

## Unknown indicators

An incoming `/classify` request whose symbol is absent from the registry
is not an error. The strategy returns a well-formed response with:

- `score = 0.0`
- `certainty = 0.0`
- `classification_method = RULE_BASED`
- `computed_metrics = { "unknown_indicator": true }`
- `reasoning_trace` naming the unknown symbol.

This matches the CLS-004 fallback shape and lets downstream composite
scoring (CLS-002) ignore the signal without a null or an exception.

## Worked example — VIX on Volmageddon (2018-02-05)

Illustrative; exact `N`, `D`, and history values land with the
implementation. The purpose here is to show that an independent reviewer
can compute severity on paper.

Registry entry (indicative):

```yaml
equity_vol_index:
  N: 60
  D: 1.5
  deviation_kind: LEVEL_VS_MEDIAN
```

1. Pull the preceding 60 trading-day VIX closes. Compute their rolling
   median `m`. (For 2018-02-05, historical close is 37.32; the 60-day
   median of the preceding window is ≈ 10–11 based on the calm late-2017
   regime.)
2. Compute `|deviation| = |37.32 − m| ≈ 26.5`.
3. For each of the 60 values `v_i` in the window, compute
   `|v_i − m|` and count how many are `≤ 26.5`. On a calm late-2017
   window, effectively all 60 are below the Volmageddon deviation.
4. `severity = 60 / 60 ≈ 1.00`. In practice, with rank normalization and
   the specific window, this will land in the ~0.95–1.00 band.
5. Verify dispersion floor: IQR of the window is well above `D = 1.5`;
   no degraded-confidence fallback triggered.

The paper-computable number is the reviewer's check on the
implementation. Any classifier that returns a materially different
number on the same inputs is wrong, and the test can say so
deterministically.

## References

- ADR-0002: [`0002-ecdf-severity-and-backtest-harness.md`](0002-ecdf-severity-and-backtest-harness.md)
- CLS-001 (current, qualitative): SRS v2.3.1 §3 Classification
- CLS-004 (degraded-confidence fallback): SRS v2.3.1 §3 Classification
- Empirical CDF: Wasserman, *All of Statistics* §7.2
