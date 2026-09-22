"""Fitted magnitude percentile with a Shapiro-Wilk validity gate (ADR-0004)."""
from __future__ import annotations

from collections.abc import Iterable
from dataclasses import dataclass
from math import isfinite, log
from statistics import NormalDist, mean, pstdev

from scipy.stats import shapiro

from app.math.ecdf import ecdf_rank
from app.registry import SeverityFallbackFamily

_MIN_FIT_SAMPLE = 3


@dataclass(frozen=True)
class ParametricEstimate:
    percentile: float
    rejection_reason: str | None = None
    pvalue: float | None = None


def parametric_estimate(
    magnitude: float,
    history: Iterable[float],
    family: SeverityFallbackFamily,
    alpha: float,
) -> ParametricEstimate:
    """Fit the registered family; retain a diagnostic ECDF if fitting fails.

    Log-Gaussian fitting requires strictly positive magnitudes. No pseudocount,
    zero deletion, or distribution-family substitution is applied to the data.
    """
    values = [abs(value) for value in history]
    fallback = ecdf_rank(magnitude, values)
    if len(values) < _MIN_FIT_SAMPLE:
        return ParametricEstimate(fallback, "fewer than three observations")
    if family == "log_gaussian" and min(values) <= 0.0:
        return ParametricEstimate(fallback, "log-Gaussian history contains zero")
    sample = [log(value) for value in values] if family == "log_gaussian" else values
    observation = magnitude
    if family == "log_gaussian":
        observation = log(magnitude) if magnitude else float("-inf")
    return _fitted_estimate(observation, sample, fallback, alpha)


def _fitted_estimate(
    observation: float, sample: list[float], fallback: float, alpha: float,
) -> ParametricEstimate:
    """Fit Gaussian location/scale on the original or log-transformed sample."""
    location, scale = mean(sample), pstdev(sample)
    if not isfinite(scale) or scale == 0.0:
        return ParametricEstimate(fallback, "zero or non-finite fitted spread")
    residuals = [(value - location) / scale for value in sample]
    pvalue = float(shapiro(residuals).pvalue)
    if not isfinite(pvalue) or pvalue < alpha:
        return ParametricEstimate(fallback, "Shapiro-Wilk rejected fitted family", pvalue)
    percentile = NormalDist(location, scale).cdf(observation)
    return ParametricEstimate(percentile, pvalue=pvalue)
