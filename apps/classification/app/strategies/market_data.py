"""Signed ECDF severity over retained market levels centered on their median.

The level-distribution approximation and empirical limits are in LIMITATIONS.md.
"""
from __future__ import annotations

from datetime import datetime
from statistics import median
from typing import Any

from app.config import settings
from app.math.deviation import level_vs_median_deviation
from app.math.ecdf import ecdf_rank, is_window_flat, signed_severity
from app.math.temporal import compute_temporal_relevance
from app.models.requests import ClassifyRequest, MarketDataPayload
from app.models.responses import ClassifyResponse, ScoreType
from app.state import UnknownSymbolError, state
from app.strategies.base import ClassificationStrategy

_MIN_HISTORY_FOR_RANK = 2


class MarketDataStrategy(ClassificationStrategy):
    async def classify(self, request: ClassifyRequest) -> ClassifyResponse:
        payload = MarketDataPayload(**(request.structured_payload or {}))
        symbol = payload.symbol
        current_value = payload.current_value
        signal_time = datetime.fromisoformat(payload.timestamp)

        try:
            window = state.get_or_create_window(symbol)
        except UnknownSymbolError:
            return _degraded(
                reason=f"{symbol} not in registry — CLS-009 degraded confidence",
                computed_metrics={
                    "deviation": None,
                    "ecdf_rank": None,
                    "unknown_indicator": True,
                },
            )

        indicator_class = window.indicator_class
        temporal_relevance = compute_temporal_relevance(
            signal_time, window.last_update, indicator_class.expected_frequency_seconds,
            cadence=indicator_class.cadence,
        )
        target_depth = indicator_class.N_L or indicator_class.N
        history_sufficiency = round(min(1.0, len(window.values) / target_depth), 4)
        certainty = round(history_sufficiency * temporal_relevance, 4)

        deviation = level_vs_median_deviation(current_value, window.values)

        # CLS-009 trip: too little history to rank against.
        if len(window.values) < _MIN_HISTORY_FOR_RANK:
            window.append(current_value, signal_time)
            return _degraded(
                reason=(
                    f"{symbol}={current_value} — insufficient history "
                    f"({len(window.values) - 1} prior values); ECDF undefined"
                ),
                temporal_relevance=temporal_relevance,
                computed_metrics={
                    "deviation": round(deviation, 4),
                    "ecdf_rank": None,
                    "history_length": len(window.values) - 1,
                },
            )

        # Rank |deviation| against historical |deviations| derived from the
        # same window (each historical level vs the current rolling median).
        # Rank BEFORE appending so the current observation doesn't bias its
        # own rank.
        levels = list(window.values)
        m = median(levels)
        history_devs = [abs(v - m) for v in levels]
        # CLS-009 checks the recent N observations independently of N_L.
        # Preserve the computed severity but reduce its certainty.
        window_degenerate = is_window_flat(history_devs[-indicator_class.N:])
        if window_degenerate:
            certainty = round(certainty * settings.degraded_certainty_factor, 4)
        rank = ecdf_rank(abs(deviation), history_devs)
        window.append(current_value, signal_time)
        return ClassifyResponse(
            score=signed_severity(deviation, rank),
            score_type=ScoreType.ANOMALY_DETECTION,
            certainty=certainty,
            history_sufficiency=history_sufficiency,
            temporal_relevance=temporal_relevance,
            event_taxonomy=None,
            classification_method="RULE_BASED",
            reasoning_trace=(
                f"{symbol}={current_value} vs history median={m:.4f}; "
                f"signed deviation={deviation:.4f}; ECDF percentile={rank:.4f} "
                f"(history_n={len(levels)}, target_n={target_depth}); "
                f"window_degenerate={window_degenerate}"
            ),
            computed_metrics={
                "deviation": round(abs(deviation), 4),
                "deviation_signed": round(deviation, 4),
                "ecdf_rank": round(rank, 4),
                "window_degenerate": window_degenerate,
            },
        )


def _degraded(
    *,
    reason: str,
    certainty: float = 0.0,
    history_sufficiency: float = 0.0,
    temporal_relevance: float = 0.0,
    computed_metrics: dict[str, Any],
) -> ClassifyResponse:
    """CLS-009 degraded-confidence response. Severity is 0; reason in trace."""
    return ClassifyResponse(
        score=0.0,
        score_type=ScoreType.ANOMALY_DETECTION,
        certainty=certainty,
        history_sufficiency=history_sufficiency,
        temporal_relevance=temporal_relevance,
        event_taxonomy=None,
        classification_method="RULE_BASED",
        reasoning_trace=reason,
        computed_metrics=computed_metrics,
    )
