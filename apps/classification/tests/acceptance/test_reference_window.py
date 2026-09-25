"""
Caller-supplied reference window (ADR-0005).

A request carrying `reference_window` must produce exactly the response the
same history produces when seeded into process state, and must neither read
nor mutate that state. Expected responses come from the existing seeded path,
whose values the anchor fixtures already pin to hand-computed bands.
"""
from __future__ import annotations

import copy
import json
from typing import Any

import pytest
from fastapi.testclient import TestClient

from app.state import state

from .conftest import ACCEPTANCE_FIXTURES, seed_macro_window, seed_market_data_window


def _market_anchors() -> list[tuple[str, dict[str, Any]]]:
    anchors = []
    for path in sorted(ACCEPTANCE_FIXTURES.glob("market_data_*.json")):
        fixture = json.loads(path.read_text(encoding="utf-8"))
        if fixture.get("srs_version") == "2.3.3":
            anchors.append((path.stem, fixture))
    return anchors


MARKET_ANCHORS = _market_anchors()
MACRO_ANCHORS = [
    (path.stem, json.loads(path.read_text(encoding="utf-8")))
    for path in sorted(ACCEPTANCE_FIXTURES.glob("macro_*.json"))
]


def _with_window(request: dict[str, Any], values: list[float], last_update: str | None) -> dict[str, Any]:
    body = copy.deepcopy(request)
    body["reference_window"] = {"values": values, "last_update": last_update}
    return body


@pytest.mark.parametrize("name,anchor", MARKET_ANCHORS, ids=[name for name, _ in MARKET_ANCHORS])
def test_supplied_window_reproduces_the_seeded_anchor_response_byte_for_byte(
    client: TestClient, name: str, anchor: dict[str, Any],
) -> None:
    seed_market_data_window(anchor["symbol"], anchor["window"], anchor["last_update"],
                            long_horizon_values=anchor["long_horizon_window"])
    seeded = client.post("/classify", json=anchor["request"])

    state.windows.clear()
    state.is_ready = False
    supplied = client.post("/classify", json=_with_window(
        anchor["request"], anchor["long_horizon_window"], anchor["last_update"]))

    assert seeded.status_code == supplied.status_code == 200, name
    assert supplied.content == seeded.content, name


@pytest.mark.parametrize("name,anchor", MACRO_ANCHORS, ids=[name for name, _ in MACRO_ANCHORS])
def test_supplied_surprise_window_reproduces_the_seeded_macro_anchor_byte_for_byte(
    client: TestClient, name: str, anchor: dict[str, Any],
) -> None:
    seed_macro_window(anchor["indicator"], anchor["window"], anchor["last_update"])
    seeded = client.post("/classify", json=anchor["request"])

    state.windows.clear()
    state.is_ready = False
    supplied = client.post("/classify", json=_with_window(anchor["request"], anchor["window"], anchor["last_update"]))

    assert seeded.status_code == supplied.status_code == 200, name
    assert supplied.content == seeded.content, name
    assert state.windows == {}, name


def test_supplied_macro_surprise_window_matches_the_seeded_path(client: TestClient) -> None:
    surprises = [0.1, 0.3, 0.2, 0.0, 0.4, 0.1, 0.2, 0.5, 0.3, 0.1, 0.2, 0.6]
    request = {
        "source_category": "MACROECONOMIC",
        "payload_type": "STRUCTURED",
        "structured_payload": {
            "indicator": "INITIAL_CLAIMS", "actual": 219000, "expected": 210000,
            "release_timestamp": "2026-04-09T12:30:00+00:00",
        },
    }
    seed_macro_window("INITIAL_CLAIMS", surprises, "2026-04-02T12:30:00+00:00")
    seeded = client.post("/classify", json=request)

    state.windows.clear()
    supplied = client.post("/classify", json=_with_window(request, surprises, "2026-04-02T12:30:00+00:00"))

    assert seeded.status_code == supplied.status_code == 200
    assert supplied.content == seeded.content


def test_supplied_window_neither_reads_nor_mutates_process_state(client: TestClient) -> None:
    anchor = MARKET_ANCHORS[0][1]
    request = _with_window(anchor["request"], anchor["long_horizon_window"], anchor["last_update"])
    baseline = client.post("/classify", json=request)

    # A different history already in process state must not change the answer.
    seed_market_data_window(anchor["symbol"], [50.0, 60.0, 70.0], "2000-01-03T00:00:00+00:00")
    before = (list(state.windows[anchor["symbol"]].values), state.windows[anchor["symbol"]].last_update)
    with_state = client.post("/classify", json=request)
    after = (list(state.windows[anchor["symbol"]].values), state.windows[anchor["symbol"]].last_update)

    assert with_state.content == baseline.content
    assert after == before
    assert set(state.windows) == {anchor["symbol"]}


def test_request_without_window_still_uses_and_extends_process_state(client: TestClient) -> None:
    seed_market_data_window("VIX", [14.0, 15.0, 16.0], "2018-02-01T00:00:00+00:00")
    request = {
        "source_category": "MARKET_DATA", "payload_type": "STRUCTURED",
        "structured_payload": {"symbol": "VIX", "current_value": 17.0, "timestamp": "2018-02-02T00:00:00+00:00"},
    }

    assert client.post("/classify", json=request).status_code == 200
    assert list(state.windows["VIX"].values) == [14.0, 15.0, 16.0, 17.0]


def test_a_request_with_its_own_window_is_served_while_bootstrapping(client: TestClient) -> None:
    anchor = MARKET_ANCHORS[0][1]
    state.is_ready = False

    without = client.post("/classify", json=anchor["request"])
    with_window = client.post("/classify", json=_with_window(
        anchor["request"], anchor["long_horizon_window"], anchor["last_update"]))

    assert without.status_code == 503
    assert with_window.status_code == 200


def test_unknown_symbol_with_a_window_is_the_cls009_unknown_indicator_response(client: TestClient) -> None:
    request = _with_window({
        "source_category": "MARKET_DATA", "payload_type": "STRUCTURED",
        "structured_payload": {"symbol": "NOT_REGISTERED", "current_value": 1.0, "timestamp": "2020-01-02T00:00:00+00:00"},
    }, [1.0, 2.0], "2020-01-01T00:00:00+00:00")

    body = client.post("/classify", json=request).json()

    assert body["score"] == 0.0
    assert body["computed_metrics"]["unknown_indicator"] is True
    assert "NOT_REGISTERED" not in state.windows


@pytest.mark.parametrize("window", [
    {"values": ["NaN"]},
    {"values": [1.0], "unexpected": True},
    {"last_update": "2020-01-01T00:00:00+00:00"},
    {"values": [1.0], "last_update": "2020-01-01T00:00:00"},
    {"values": [1.0], "last_update": "yesterday"},
])
def test_malformed_windows_are_rejected(client: TestClient, window: dict[str, Any]) -> None:
    request = {
        "source_category": "MARKET_DATA", "payload_type": "STRUCTURED",
        "structured_payload": {"symbol": "VIX", "current_value": 17.0, "timestamp": "2020-01-02T00:00:00+00:00"},
        "reference_window": window,
    }

    assert client.post("/classify", json=request).status_code == 422
