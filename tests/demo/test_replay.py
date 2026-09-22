"""Real TCP replay acceptance and independently calculated scenario boundaries."""
from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import httpx
import pytest

from apps.demo.scenario_result import scenario_result

ROOT = Path(__file__).resolve().parents[2]
EVENT = "market_data_vix_volmageddon_2018_02_05"


@contextmanager
def running_demo(log_path: Path, *, unavailable: bool = False, classifier_port: int | None = None):
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    env = os.environ.copy()
    env["BOOTSTRAP_MODE"] = "disabled"
    # A caller's custom registry must not alter the demo's declared data profile.
    env["PRIMESCORE_REGISTRY_PATH"] = str(ROOT / "does-not-exist.yaml")
    env["FRED_API_KEY"] = ""
    command = [sys.executable, str(ROOT / "scripts/demo.py"), "--port", str(port)]
    blocked_port = socket.socket()
    if unavailable:
        # A bound, non-listening port refuses TCP; the dashboard server stays up.
        blocked_port.bind(("127.0.0.1", 0))
        classifier_port = blocked_port.getsockname()[1]
    if classifier_port is not None:
        env["PRIMESCORE_REGISTRY_PATH"] = str(ROOT / "infra/registry.yaml")
        env["PYTHONPATH"] = os.pathsep.join([str(ROOT), str(ROOT / "apps/classification")])
        command = [sys.executable, "-c", (
            "import uvicorn; from apps.demo.replay_app import demo_app; "
            f"uvicorn.run(demo_app({classifier_port}), host='127.0.0.1', port={port})"
        )]
    try:
        with log_path.open("w", encoding="utf-8") as log:
            process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
            try:
                with httpx.Client(base_url=f"http://127.0.0.1:{port}", timeout=20, trust_env=False) as client:
                    deadline = time.monotonic() + 30
                    while time.monotonic() < deadline:
                        if process.poll() is not None:
                            pytest.fail(f"Demo exited during startup: {log_path.read_text(encoding='utf-8')}")
                        try:
                            if client.get("/demo/api/events").status_code == 200:
                                break
                        except httpx.ConnectError:
                            pass
                        time.sleep(0.1)
                    else:
                        pytest.fail(f"Demo startup timed out: {log_path.read_text(encoding='utf-8')}")
                    yield client
            finally:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
    finally:
        blocked_port.close()


@pytest.fixture(scope="module")
def client(tmp_path_factory):
    with running_demo(tmp_path_factory.mktemp("demo") / "server.log") as active_client:
        yield active_client


@pytest.mark.parametrize("severity,certainty,k,expected_iv,expected_gap", [
    (0.8, 0.5, 0.25, 22.0, 2.0),
    (-0.8, 0.5, 0.25, 18.0, -2.0),
    (-1.0, 1.0, 1.0, 0.0, -20.0),
    (1.0, 1.0, 1.0, 40.0, 20.0),
    (1.0, 1.0, 0.0, 20.0, 0.0),
    (1.0, 0.0, 1.0, 20.0, 0.0),
])
def test_hand_calculated_scenarios(severity, certainty, k, expected_iv, expected_gap):
    result = scenario_result(20, severity, certainty, k)
    assert result["scenario_iv"] == pytest.approx(expected_iv)
    assert result["gap"] == pytest.approx(expected_gap)


@pytest.mark.parametrize("values", [
    (-1, 0, 0, 0), (20, 1.1, 1, 1), (20, 1, -0.1, 1), (20, 1, 1, 1.01),
    (float("inf"), 0, 1, 1), (20, float("nan"), 1, 1),
])
def test_invalid_scenario_inputs_are_rejected(values):
    with pytest.raises(ValueError):
        scenario_result(*values)


def test_dashboard_assets_and_all_sourced_snapshots(client):
    assert client.get("/").status_code == 200
    assert "Experimental" in client.get("/").text
    assert client.get("/assets/dashboard.js").status_code == 200
    assert client.get("/assets/styles.css").status_code == 200
    events = client.get("/demo/api/events").json()["events"]
    assert len(events) == 8
    for event in events:
        assert event["history_count"] == 1260
        assert event["source"]["provider"] == "FRED"
        response = client.post("/demo/api/replay", json={"event_id": event["event_id"]})
        assert response.status_code == 200
        result = response.json()
        assert result["classifier_http_status"] == 200
        assert result["status"] == "classified"
        assert len(result["history"]) == 1260
        assert -1 <= result["classification"]["score"] <= 1
        assert result["scenario"]["scenario_iv"] >= 0
        assert result["classification"]["reasoning_trace"]


def test_reset_and_concurrent_replay_are_repeatable(client):
    request = {"event_id": EVENT, "sensitivity": 0.5}
    first = client.post("/demo/api/replay", json=request).json()
    assert first["classification"]["score"] > 0.99
    assert client.post("/demo/api/reset").json() == {"status": "reset"}
    assert client.get("/health").status_code == 503
    assert client.post("/demo/api/replay", json=request).json() == first
    other_request = {"event_id": "market_data_vix_low_vol_regime_2017_10_05", "sensitivity": 0.5}
    other = client.post("/demo/api/replay", json=other_request).json()
    with ThreadPoolExecutor(max_workers=4) as executor:
        responses = list(executor.map(
            lambda body: client.post("/demo/api/replay", json=body).json(),
            [request, other_request, request, other_request],
        ))
    assert responses == [first, other, first, other]


def test_degraded_history_withholds_scenario(client):
    response = client.post("/demo/api/replay", json={"event_id": EVENT, "history_mode": "one_prior_close"})
    assert response.status_code == 200
    result = response.json()
    assert result["classifier_http_status"] == 200
    assert result["status"] == "degraded"
    assert result["classification"]["certainty"] == 0
    assert result["scenario"] is None
    assert len(result["history"]) == 1
    assert "insufficient history" in result["classification"]["reasoning_trace"]


@pytest.mark.parametrize("invalid", [
    {"sensitivity": -0.1}, {"sensitivity": 1.1}, {"sensitivity": "nan"},
    {"sensitivity": "Infinity"}, {"history_mode": "future"}, {"extra": True},
])
def test_invalid_replay_inputs_are_rejected(client, invalid):
    assert client.post("/demo/api/replay", json={"event_id": EVENT, **invalid}).status_code == 422


def test_unknown_snapshot_and_direct_mutation_are_rejected(client):
    assert client.post("/demo/api/replay", json={"event_id": "../secret"}).status_code == 404
    assert client.post("/classify", json={}).status_code == 403


def test_classifier_unavailable_returns_error_without_scenario(tmp_path):
    with running_demo(tmp_path / "unavailable.log", unavailable=True) as unavailable_client:
        response = unavailable_client.post("/demo/api/replay", json={"event_id": EVENT})
        assert response.status_code == 502
        assert "no scenario produced" in response.json()["detail"]
        assert "scenario" not in response.json()
        assert unavailable_client.get("/health").status_code == 503


def test_positive_certainty_does_not_override_degraded_response_markers(tmp_path):
    """Consumer contract probe: controlled HTTP responses, not market-data anchors."""
    response_body = {
        "score": 0.8, "certainty": 0.5, "score_type": "ANOMALY_DETECTION",
        "history_sufficiency": 1.0, "temporal_relevance": 1.0,
        "classification_method": "RULE_BASED", "reasoning_trace": "Contract response probe.",
        "computed_metrics": {},
    }

    class ResponseHandler(BaseHTTPRequestHandler):
        def do_POST(self):
            self.rfile.read(int(self.headers.get("Content-Length", "0")))
            body = json.dumps(response_body).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):
            pass

    with ThreadingHTTPServer(("127.0.0.1", 0), ResponseHandler) as upstream:
        thread = threading.Thread(target=upstream.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        thread.start()
        try:
            with running_demo(tmp_path / "degraded-markers.log", classifier_port=upstream.server_port) as demo:
                for marker in ("window_degenerate", "fit_rejected", "unknown_indicator"):
                    response_body["computed_metrics"] = {marker: True}
                    response = demo.post("/demo/api/replay", json={"event_id": EVENT})
                    assert response.status_code == 200
                    result = response.json()
                    assert result["classifier_http_status"] == 200
                    assert result["classification"]["certainty"] == 0.5
                    assert result["status"] == "degraded", marker
                    assert result["scenario"] is None, marker
                response_body["computed_metrics"] = {
                    "window_degenerate": False, "fit_rejected": False, "unknown_indicator": False,
                }
                normal = demo.post("/demo/api/replay", json={"event_id": EVENT}).json()
                assert normal["status"] == "classified"
                assert normal["scenario"] is not None
        finally:
            upstream.shutdown()
            thread.join(timeout=5)
