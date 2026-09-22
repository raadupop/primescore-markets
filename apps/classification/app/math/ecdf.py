"""
ECDF (empirical cumulative distribution function) rank.

Replaces tanh-of-z-score severity mapping per ADR-0002. Distribution-free,
regime-adaptive, paper-computable per indicator without a magic scale constant.
"""
from __future__ import annotations

from collections.abc import Iterable
from math import copysign

# Rounding precision for flat-window detection. Defensive against float-noise
# spurious distinctness in derived series; on the present sources
# (VIX/OVX 2dp, CPI YoY 1dp, claims integers) it is a no-op.
_FLAT_ROUND_NDIGITS = 4


def ecdf_rank(value: float, history: Iterable[float]) -> float:
    """Empirical-CDF rank of `value` against `history`.

    Returns the fraction of historical magnitudes <= `value`, in [0, 1].
    Returns 0.0 for an empty history (caller's responsibility to short-circuit
    on insufficient data; this function is a pure mathematical primitive and
    doesn't carry domain semantics about "enough" history).
    """
    history_list = list(history)
    if not history_list:
        return 0.0
    n = len(history_list)
    count_le = sum(1 for h in history_list if abs(h) <= value)
    return count_le / n


def signed_severity(deviation: float, percentile: float) -> float:
    """Apply the CLS-001 sign, including sign(0) = 0."""
    return round(copysign(percentile, deviation), 4) if deviation else 0.0


def is_window_flat(history: Iterable[float]) -> bool:
    """True iff the window has exactly one distinct value (after rounding).

    Binary degeneracy guard with no calibration: a flat window has zero
    spread, so any non-zero |deviation| ranks at 1.0 — a guaranteed false
    positive. Caller emits CLS-009 degraded response. Anything past flat
    (two or more distinct values) is ranked normally; "almost flat" is
    deliberately not a category here (see ADR-0002 amendment 2).
    """
    distinct = {round(v, _FLAT_ROUND_NDIGITS) for v in history}
    return len(distinct) == 1
