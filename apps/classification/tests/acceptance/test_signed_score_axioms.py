"""
Black-box axioms for SRS v2.3.3 signed severity (CLS-001).

Synthetic windows. No FRED dependency. Properties that must hold for any
valid v2.3.3 calibration regardless of formula details.
"""
from __future__ import annotations

from math import exp

import pytest
from fastapi.testclient import TestClient

from .conftest import seed_macro_window, seed_market_data_window


def _vix_request(current_value: float) -> dict:
    return {
        "source_category": "MARKET_DATA",
        "payload_type": "STRUCTURED",
        "structured_payload": {
            "symbol": "VIX",
            "current_value": current_value,
            "timestamp": "2026-04-15T00:00:00Z",
        },
    }


def _cpi_request(actual: float, expected: float) -> dict:
    return {
        "source_category": "MACROECONOMIC",
        "payload_type": "STRUCTURED",
        "structured_payload": {
            "indicator": "CPI_YOY",
            "actual": actual,
            "expected": expected,
            "release_timestamp": "2026-04-15T12:30:00Z",
        },
    }


def test_score_in_signed_unit_interval(client: TestClient) -> None:
    history = [15.0 + (i % 7) * 0.3 for i in range(1260)]
    seed_market_data_window("VIX", history, "2026-04-14T00:00:00+00:00")
    body = client.post("/classify", json=_vix_request(40.0)).json()
    assert -1.0 <= body["score"] <= 1.0


def test_vol_compression_returns_negative_score(client: TestClient) -> None:
    history = [20.0 + (i % 11) * 0.5 for i in range(1260)]
    seed_market_data_window("VIX", history, "2026-04-14T00:00:00+00:00")
    body = client.post("/classify", json=_vix_request(11.0)).json()
    assert body["score"] < 0, f"compression must yield negative score, got {body['score']}"


def test_vol_expansion_returns_positive_score(client: TestClient) -> None:
    history = [15.0 + (i % 7) * 0.3 for i in range(1260)]
    seed_market_data_window("VIX", history, "2026-04-14T00:00:00+00:00")
    body = client.post("/classify", json=_vix_request(45.0)).json()
    assert body["score"] > 0, f"expansion must yield positive score, got {body['score']}"


def test_symmetric_deviations_have_opposite_signs(client: TestClient) -> None:
    history = [20.0] * 1260
    seed_market_data_window("VIX", history, "2026-04-14T00:00:00+00:00")
    up = client.post("/classify", json=_vix_request(30.0)).json()["score"]
    seed_market_data_window("VIX", history, "2026-04-14T00:00:00+00:00")
    down = client.post("/classify", json=_vix_request(10.0)).json()["score"]
    assert up * down < 0, f"symmetric nonzero deviations must have opposite signs (up={up}, down={down})"


def test_parametric_path_engages_for_low_cadence_class(client: TestClient) -> None:
    """CPI_YOY has N_L=None → parametric fallback path must be reachable.

    Asserts the response is well-formed and signed; does NOT assert
    `parametric_fit_used` (implementation-internal).
    """
    history = [0.1 * ((-1) ** i) for i in range(60)]
    seed_macro_window("CPI_YOY", history, "2026-03-15T12:30:00+00:00")
    body = client.post("/classify", json=_cpi_request(actual=3.5, expected=3.0)).json()
    assert -1.0 <= body["score"] <= 1.0
    assert body["score_type"] == "ANOMALY_DETECTION"


def test_parametric_gate_failure_returns_degraded_certainty(client: TestClient) -> None:
    """Non-Gaussian history → Shapiro-Wilk gate fails → certainty halved.

    Heavy-tailed surprise history with one massive outlier should fail the
    α=0.05 normality gate. Per SRS v2.3.3, certainty is multiplied by
    DEGRADED_CERTAINTY_FACTOR=0.5 and the response is well-formed.
    """
    history = [0.05] * 50 + [5.0, -4.5, 6.0, -5.5, 4.8, -6.2, 5.3, -4.9, 5.7, -5.1]
    seed_macro_window("CPI_YOY", history, "2026-03-15T12:30:00+00:00")
    body = client.post("/classify", json=_cpi_request(actual=3.5, expected=3.0)).json()
    assert body["certainty"] <= 0.5, (
        f"gate failure must halve certainty, got {body['certainty']}"
    )
    assert body["history_sufficiency"] == 1.0
    assert body["certainty"] == pytest.approx(body["temporal_relevance"] * 0.5, abs=0.0001)


def test_zero_deviation_returns_near_zero_score(client: TestClient) -> None:
    history = [20.0 + (i % 5) * 0.4 for i in range(1260)]
    seed_market_data_window("VIX", history, "2026-04-14T00:00:00+00:00")
    median_value = 20.8
    body = client.post("/classify", json=_vix_request(median_value)).json()
    assert body["score"] == 0.0, f"zero signed deviation must score zero, got {body['score']}"


def test_history_sufficiency_uses_long_horizon(client: TestClient) -> None:
    for depth, expected in ((126, 0.1), (252, 0.2)):
        history = [15.0 + (i % 7) for i in range(depth)]
        seed_market_data_window("VIX", history, "2026-04-14T00:00:00+00:00")
        body = client.post("/classify", json=_vix_request(40.0)).json()
        assert body["history_sufficiency"] == expected
        assert body["certainty"] == expected


def test_recent_degenerate_window_reduces_certainty(client: TestClient) -> None:
    history = [15.0 + (i % 7) for i in range(756)] + [20.0] * 504
    seed_market_data_window("VIX", history, "2026-04-14T00:00:00+00:00")
    body = client.post("/classify", json=_vix_request(40.0)).json()
    assert body["history_sufficiency"] == 1.0
    assert body["score"] == 1.0
    assert body["certainty"] == 0.5


def test_fitted_percentile_has_analytic_value_and_raw_surprise_sign(client: TestClient) -> None:
    # log(history) has mean -2 and population variance 1 exactly:
    # the symmetric integers -3..3 have mean 0 and variance 4.
    # At exp(-1), the fitted Gaussian CDF is Phi(1) = 0.841344746.
    # This external mathematical reference does not import classifier math.
    history = [exp(-2.0 + i / 2.0) for i in range(-3, 4)]
    for actual, expected, score in ((exp(-1.0), 0.0, 0.8413), (0.0, exp(-1.0), -0.8413)):
        seed_macro_window("CPI_YOY", history, "2026-03-15T12:30:00+00:00")
        body = client.post("/classify", json=_cpi_request(actual, expected)).json()
        assert body["score"] == pytest.approx(score, abs=0.0001)
        assert body["certainty"] == pytest.approx(
            body["history_sufficiency"] * body["temporal_relevance"], abs=0.0001,
        )


def test_zero_surprise_is_zero_on_parametric_path(client: TestClient) -> None:
    history = [exp(-2.0 + i / 2.0) for i in range(-3, 4)]
    seed_macro_window("CPI_YOY", history, "2026-03-15T12:30:00+00:00")
    body = client.post("/classify", json=_cpi_request(3.0, 3.0)).json()
    assert body["score"] == 0.0


def test_log_gaussian_zero_history_is_degraded_without_dropping_observations(client: TestClient) -> None:
    history = [0.0] + [exp(-2.0 + i / 2.0) for i in range(-3, 4)]
    seed_macro_window("CPI_YOY", history, "2026-03-15T12:30:00+00:00")
    body = client.post("/classify", json=_cpi_request(0.5, 0.0)).json()
    assert body["history_sufficiency"] == round(8 / 60, 4)
    assert body["certainty"] == pytest.approx(
        body["history_sufficiency"] * body["temporal_relevance"] * 0.5, abs=0.0001,
    )
