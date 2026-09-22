"""Local snapshot harness: seed history, cross the HTTP boundary, show output."""
from __future__ import annotations

import asyncio
import json
import secrets
from collections import deque
from datetime import datetime
from pathlib import Path
from typing import Any, Literal

import httpx
from app.config import registry, settings
from app.models.responses import ClassifyResponse
from app.state import RollingWindow, state
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from main import app as classifier_app
from pydantic import BaseModel, ConfigDict, Field, ValidationError

from apps.demo.scenario_result import scenario_result

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "apps/classification/tests/acceptance/fixtures"
ASSETS = Path(__file__).parent / "static"


class ReplayRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    event_id: str
    sensitivity: float = Field(default=0.25, ge=0, le=1, allow_inf_nan=False)
    history_mode: Literal["full", "one_prior_close"] = "full"


def replay_snapshots() -> dict[str, dict[str, Any]]:
    """Load sourced independent snapshots, keeping observations before the event."""
    snapshots = {}
    for path in sorted(FIXTURES.glob("market_data_*.json")):
        fixture = json.loads(path.read_text(encoding="utf-8"))
        payload = fixture["request"]["structured_payload"]
        history = fixture["long_horizon_window"]
        indicator = registry.get_symbol(payload["symbol"]).indicator_class
        if len(history) != (indicator.N_L or indicator.N):
            raise ValueError(f"{path.name}: incomplete long-horizon snapshot.")
        if datetime.fromisoformat(fixture["last_update"]) >= datetime.fromisoformat(payload["timestamp"]):
            raise ValueError(f"{path.name}: prior history must end before the event.")
        snapshots[path.stem] = fixture
    if not snapshots:
        raise ValueError("No sourced market snapshots found.")
    return snapshots


def snapshot_summary(event_id: str, fixture: dict[str, Any]) -> dict[str, Any]:
    payload = fixture["request"]["structured_payload"]
    return {
        "event_id": event_id,
        "symbol": payload["symbol"],
        "timestamp": payload["timestamp"],
        "observed_iv": payload["current_value"],
        "history_count": len(fixture["long_horizon_window"]),
        "source": fixture["source"],
        "review_status": fixture["status"],
    }


def demo_app(port: int) -> FastAPI:
    """Add demo-only seed/reset routes to the classifier's unchanged HTTP route."""
    if settings.bootstrap_mode != "disabled":
        raise ValueError("Run this demo through scripts/demo.py with bootstrap disabled.")
    snapshots = replay_snapshots()
    replay_lock = asyncio.Lock()
    replay_token = secrets.token_urlsafe(32)
    app = classifier_app
    app.title = "PrimeScore AI market replay"
    app.mount("/assets", StaticFiles(directory=ASSETS), name="demo-assets")

    @app.middleware("http")
    async def isolated_classifier(request: Request, call_next: Any) -> Any:
        # Only the locked replay operation may mutate the demo's seeded windows.
        if request.url.path.rstrip("/") == "/classify" and request.method == "POST":
            if request.headers.get("X-PrimeScore-Replay") != replay_token:
                return JSONResponse(status_code=403, content={"detail": "Use /demo/api/replay in this local harness."})
        return await call_next(request)

    @app.get("/", include_in_schema=False)
    async def dashboard() -> FileResponse:
        return FileResponse(ASSETS / "index.html")

    @app.get("/demo/api/events")
    async def events() -> dict[str, Any]:
        tape = [snapshot_summary(event_id, fixture) for event_id, fixture in snapshots.items()]
        return {"mode": "independent historical snapshots", "events": sorted(tape, key=lambda event: event["timestamp"])}

    @app.post("/demo/api/reset")
    async def reset() -> dict[str, str]:
        async with replay_lock:
            state.windows.clear()
            state.is_ready = False
        return {"status": "reset"}

    @app.post("/demo/api/replay")
    async def replay(request: ReplayRequest) -> dict[str, Any]:
        fixture = snapshots.get(request.event_id)
        if fixture is None:
            raise HTTPException(status_code=404, detail="Unknown snapshot.")
        payload = fixture["request"]["structured_payload"]
        history = fixture["long_horizon_window"]
        if request.history_mode == "one_prior_close":
            history = history[-1:]
        async with replay_lock:
            state.is_ready = False
            state.windows.clear()
            state.windows[payload["symbol"]] = RollingWindow(
                indicator_class=registry.get_symbol(payload["symbol"]).indicator_class,
                values=deque(history),
                last_update=datetime.fromisoformat(fixture["last_update"]),
            )
            state.is_ready = True
            try:
                async with httpx.AsyncClient(base_url=f"http://127.0.0.1:{port}", timeout=15, trust_env=False) as client:
                    response = await client.post(
                        "/classify", json=fixture["request"],
                        headers={"X-PrimeScore-Replay": replay_token},
                    )
                    response.raise_for_status()
                    classification = ClassifyResponse.model_validate(response.json())
            except (httpx.HTTPError, ValidationError, ValueError) as exc:
                state.is_ready = False
                state.windows.clear()
                raise HTTPException(status_code=502, detail="Classifier unavailable or invalid; no scenario produced.") from exc
        degraded = (
            classification.certainty == 0
            or request.history_mode != "full"
            or any(classification.computed_metrics.get(marker) is True for marker in (
                "window_degenerate", "fit_rejected", "unknown_indicator",
            ))
        )
        return {
            "event": snapshot_summary(request.event_id, fixture),
            "status": "degraded" if degraded else "classified",
            "classifier_http_status": response.status_code,
            "history_mode": request.history_mode,
            "history": history,
            "last_update": fixture["last_update"],
            "classification": classification.model_dump(mode="json"),
            "scenario": None if degraded else scenario_result(
                payload["current_value"], classification.score,
                classification.certainty, request.sensitivity,
            ),
        }

    return app
