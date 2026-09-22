"""FRED implementation of `ProviderFetcher`.

Owns all FRED-specific concerns: API-key check, date-range selection,
`fredapi` import, response decoding, and the surprise_yoy guard rails
(requires `derive: pct_change_yoy` plus a static consensus). Produces a
provider-agnostic `WindowSeed` via `window_builders`.
"""
import logging
from datetime import datetime, timedelta, timezone

from app.bootstrap import window_builders
from app.bootstrap.cpi_consensus import CPI_CONSENSUS_YOY
from app.bootstrap.provider_fetcher import WindowSeed
from app.config import settings
from app.registry import SymbolEntry

logger = logging.getLogger(__name__)


class FredFetcher:
    """Bootstrap rolling windows from the FRED time-series API."""

    async def fetch_window(self, entry: SymbolEntry) -> WindowSeed | None:
        if entry.bootstrap is None:
            return None
        if not settings.fred_api_key:
            logger.warning("FRED_API_KEY not set — skipping bootstrap for %s", entry.symbol)
            return None

        kind = entry.indicator_class.deviation_kind
        try:
            if kind == "pct_change":
                return self._fetch_level_series(entry)
            if kind == "surprise_yoy":
                return self._fetch_yoy_surprises(entry)
        except Exception as exc:  # noqa: BLE001 — bootstrap must not crash startup
            logger.warning("Bootstrap failed for %s: %s", entry.symbol, exc)
            return None

        logger.warning(
            "%s: deviation_kind=%s not supported by FRED bootstrap — skipping",
            entry.symbol, kind,
        )
        return None

    def _fetch_level_series(self, entry: SymbolEntry) -> WindowSeed | None:
        assert entry.bootstrap is not None  # noqa: S101 — narrowed at call site
        n = entry.indicator_class.N_L or entry.indicator_class.N
        end = datetime.now()
        start = end - timedelta(days=max(45, n * 2))
        decoded = self._download_series(entry.bootstrap.series_id, start, end)
        if decoded is None:
            return None
        levels, dates = decoded
        seed = window_builders.build_level_window(levels, dates, n)
        if seed is None:
            logger.warning(
                "FRED %s returned no data for %s",
                entry.bootstrap.series_id, entry.symbol,
            )
            return None
        logger.info(
            "Bootstrap %s (%s): %d levels from FRED %s",
            entry.symbol, entry.indicator_class.name, len(seed.values),
            entry.bootstrap.series_id,
        )
        return seed

    def _fetch_yoy_surprises(self, entry: SymbolEntry) -> WindowSeed | None:
        assert entry.bootstrap is not None  # noqa: S101 — narrowed at call site
        if entry.bootstrap.derive != "pct_change_yoy":
            logger.warning(
                "%s: deviation_kind=surprise_yoy with no derive=pct_change_yoy; "
                "FRED bootstrap can't construct surprises — skipping",
                entry.symbol,
            )
            return None
        if entry.symbol != "CPI_YOY":
            logger.warning(
                "%s: surprise_yoy bootstrap requires consensus data; "
                "Finnhub unavailable and no static consensus for this series — skipping",
                entry.symbol,
            )
            return None

        n = entry.indicator_class.N_L or entry.indicator_class.N
        lookback_years = 5
        end = datetime.now()
        start = end - timedelta(days=365 * lookback_years)
        decoded = self._download_series(entry.bootstrap.series_id, start, end)
        if decoded is None:
            return None
        levels, dates = decoded
        seed = window_builders.build_yoy_surprise_window(
            levels, dates, CPI_CONSENSUS_YOY, n,
        )
        if seed is None:
            logger.warning("No surprise data computed for %s", entry.symbol)
            return None
        logger.info(
            "Bootstrap %s (%s): %d surprises (FRED + static consensus)",
            entry.symbol, entry.indicator_class.name, len(seed.values),
        )
        return seed

    def _download_series(
        self, series_id: str, start: datetime, end: datetime,
    ) -> tuple[list[float], list[datetime]] | None:
        """Fetch one series from FRED and decode it to plain Python lists."""
        from fredapi import Fred  # local import — keeps module import cheap when no key set

        fred = Fred(api_key=settings.fred_api_key)
        series = fred.get_series(series_id, start, end).dropna()
        if series.empty:
            return None
        levels = [float(v) for v in series.values]
        dates = [
            idx.to_pydatetime().replace(tzinfo=timezone.utc)
            for idx in series.index
        ]
        return levels, dates
