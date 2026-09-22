"""Transparent scenario arithmetic; no calibrated forecast or aggregation."""
from math import isfinite


def scenario_result(
    observed_iv: float, severity: float, certainty: float, sensitivity: float,
) -> dict[str, float]:
    """Return a single-event scenario in the observed index's units."""
    if not all(isfinite(value) for value in (observed_iv, severity, certainty, sensitivity)):
        raise ValueError("Scenario inputs must be finite.")
    if observed_iv <= 0 or not -1 <= severity <= 1:
        raise ValueError("Observed IV must be positive and severity must lie in [-1, 1].")
    if not 0 <= certainty <= 1 or not 0 <= sensitivity <= 1:
        raise ValueError("Certainty and sensitivity must lie in [0, 1].")
    conviction = severity * certainty
    scenario_iv = observed_iv * (1 + conviction * sensitivity)
    return {
        "observed_iv": observed_iv,
        "conviction": conviction,
        "sensitivity": sensitivity,
        "scenario_iv": scenario_iv,
        "gap": scenario_iv - observed_iv,
    }
