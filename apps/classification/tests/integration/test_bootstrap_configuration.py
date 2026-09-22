"""Bootstrap invariants with an in-memory provider; no external requests."""
from datetime import datetime, timezone

import pytest
from fastapi.testclient import TestClient

from app import bootstrap
from app.bootstrap.provider_fetcher import WindowSeed
from app.config import settings
from app.state import state
from main import app


class SnapshotProvider:
    def __init__(self, missing=None, short=None):
        self.missing = missing
        self.short = short

    async def fetch_window(self, entry):
        if entry.symbol == self.missing:
            return None
        target = entry.indicator_class.N_L or entry.indicator_class.N
        depth = target - 1 if entry.symbol == self.short else target
        return WindowSeed(
            values=[10.0 + (i % 5) for i in range(depth)],
            last_update=datetime(2026, 9, 21, tzinfo=timezone.utc),
        )


@pytest.fixture(autouse=True)
def isolated_bootstrap(monkeypatch):
    state.windows.clear()
    state.is_ready = False
    monkeypatch.setattr(settings, "bootstrap_mode", "live")
    yield
    state.windows.clear()
    state.is_ready = False


@pytest.mark.parametrize("missing,short,expected_status", [
    (None, None, 200), ("CPI_YOY", None, 503), (None, "VIX", 503),
])
def test_readiness_requires_every_verified_window_at_full_depth(monkeypatch, missing, short, expected_status):
    monkeypatch.setattr(bootstrap, "_FETCHERS", {"fred": SnapshotProvider(missing, short)})
    with TestClient(app) as client:
        assert client.get("/health").status_code == expected_status
        assert len(state.windows["OVX"].values) == 1260


def test_no_api_key_does_not_report_ready(monkeypatch):
    monkeypatch.setattr(settings, "fred_api_key", "")
    with TestClient(app) as client:
        assert client.get("/health").status_code == 503
        assert not state.windows


def test_disabled_bootstrap_never_calls_provider(monkeypatch):
    class ForbiddenProvider:
        async def fetch_window(self, entry):
            pytest.fail("Disabled bootstrap attempted a provider request")

    monkeypatch.setattr(settings, "bootstrap_mode", "disabled")
    monkeypatch.setattr(bootstrap, "_FETCHERS", {"fred": ForbiddenProvider()})
    with TestClient(app) as client:
        assert client.get("/health").status_code == 503
