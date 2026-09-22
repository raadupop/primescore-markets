"""Signed level and surprise deviations; callers rank their magnitudes."""
from __future__ import annotations

from collections.abc import Iterable
from statistics import median


def level_vs_median_deviation(current_value: float, history: Iterable[float]) -> float:
    """Signed deviation for level series (vol indices, prices).

    Uses median rather than mean to be robust to the same fat-tailed events
    we're trying to rank — mean-baseline severity self-suppresses on shocks.
    """
    history_list = list(history)
    if not history_list:
        return 0.0
    return current_value - median(history_list)


def surprise_deviation(actual: float, expected: float) -> float:
    """Signed deviation for surprise series (inflation, claims). History not used —
    the consensus IS the baseline."""
    return actual - expected
