"""
Shared fixtures for the acceptance suite.

Acceptance tests interact with the service ONLY via its public HTTP surface
(`POST /classify`, `GET /health`) and its contract (`doc/openapi.yaml`). They
must not import strategy internals or assert on implementation-specific
fields like `computed_metrics.z_score`.
"""
from __future__ import annotations

from collections import deque
from datetime import datetime
from pathlib import Path
from typing import Any, Iterator
from unittest.mock import patch

import pytest
import yaml
from fastapi.testclient import TestClient

from app.config import registry, settings
from app.state import RollingWindow, state
from main import app

CLASSIFICATION_ROOT = Path(__file__).resolve().parents[2]
OPENAPI_PATH = CLASSIFICATION_ROOT / "doc" / "openapi.yaml"
ACCEPTANCE_FIXTURES = Path(__file__).parent / "fixtures"


@pytest.fixture(scope="session")
def openapi_spec() -> dict[str, Any]:
    with OPENAPI_PATH.open("r", encoding="utf-8") as fh:
        return yaml.safe_load(fh)


@pytest.fixture(scope="module")
def client() -> Iterator[TestClient]:
    with patch.object(settings, "bootstrap_mode", "disabled"), TestClient(app) as c:
        yield c


@pytest.fixture(autouse=True)
def reset_state() -> Iterator[None]:
    """Clear windows between tests, keep bootstrap-ready flag."""
    state.windows.clear()
    state.is_ready = True
    yield
    state.windows.clear()


def _seed_window(
    symbol: str,
    values: list[float],
    last_update_iso: str,
    long_horizon_values: list[float] | None = None,
) -> None:
    """Seed a rolling window from registry-defined indicator class.

    Per SRS v2.3.3 (CLS-001), severity is computed over the long-horizon
    window `N_L` when set. Callers can pass `long_horizon_values` (length
    must equal `IndicatorClass.N_L`); they take precedence over `values`.
    """
    entry = registry.get_symbol(symbol)
    seed = long_horizon_values if long_horizon_values is not None else values
    state.windows[symbol] = RollingWindow(
        indicator_class=entry.indicator_class,
        values=deque(seed),
        last_update=datetime.fromisoformat(last_update_iso),
    )


def seed_market_data_window(
    symbol: str,
    values: list[float],
    last_update_iso: str,
    long_horizon_values: list[float] | None = None,
) -> None:
    _seed_window(symbol, values, last_update_iso, long_horizon_values)


def seed_macro_window(
    indicator: str,
    values: list[float],
    last_update_iso: str,
    long_horizon_values: list[float] | None = None,
) -> None:
    _seed_window(indicator, values, last_update_iso, long_horizon_values)
