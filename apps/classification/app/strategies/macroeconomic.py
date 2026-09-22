"""Signed surprise severity using the registry's ECDF or fitted percentile.

Rejected fits retain an explicitly degraded diagnostic ECDF (ADR-0004).
"""
from __future__ import annotations

from datetime import datetime
from typing import Any

from app.config import settings
from app.math.deviation import surprise_deviation
from app.math.ecdf import ecdf_rank, is_window_flat, signed_severity
from app.math.parametric_estimate import ParametricEstimate, parametric_estimate
from app.math.temporal import compute_temporal_relevance
from app.models.requests import ClassifyRequest, MacroeconomicPayload
from app.models.responses import ClassifyResponse, ScoreType
from app.registry import IndicatorClass
from app.state import UnknownSymbolError, state
from app.strategies.base import ClassificationStrategy

_MIN_HISTORY_FOR_RANK = 2


class MacroeconomicStrategy(ClassificationStrategy):
    async def classify(self, request: ClassifyRequest) -> ClassifyResponse:
        payload = MacroeconomicPayload(**(request.structured_payload or {}))
        symbol = payload.indicator
        actual = payload.actual
        expected = payload.expected
        signal_time = datetime.fromisoformat(payload.release_timestamp)

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

        deviation = surprise_deviation(actual, expected)

        if len(window.values) < _MIN_HISTORY_FOR_RANK:
            window.append(abs(deviation), signal_time)
            return _degraded(
                reason=(
                    f"{symbol} actual={actual} expected={expected} "
                    f"|deviation|={deviation} — insufficient history "
                    f"({len(window.values) - 1} prior surprises); ECDF undefined"
                ),
                temporal_relevance=temporal_relevance,
                computed_metrics={
                    "deviation": round(deviation, 4),
                    "ecdf_rank": None,
                    "history_length": len(window.values) - 1,
                },
            )

        # Rank current |deviation| against the stored |deviation| history.
        # Stored history contains surprise magnitudes, not indicator levels.
        # Rank BEFORE appending so the current observation doesn't bias its rank.
        # Recent degeneracy and fit rejection reduce certainty once.
        history = [abs(value) for value in window.values]
        window_degenerate = is_window_flat(history[-indicator_class.N:])
        estimate = _magnitude_estimate(abs(deviation), history, indicator_class)
        rank = estimate.percentile
        fit_rejected = estimate.rejection_reason is not None
        if window_degenerate or fit_rejected:
            certainty = round(certainty * settings.degraded_certainty_factor, 4)
        window.append(abs(deviation), signal_time)

        return ClassifyResponse(
            score=signed_severity(deviation, rank),
            score_type=ScoreType.ANOMALY_DETECTION,
            certainty=certainty,
            history_sufficiency=history_sufficiency,
            temporal_relevance=temporal_relevance,
            event_taxonomy=None,
            classification_method="RULE_BASED",
            reasoning_trace=(
                f"{symbol} actual={actual} expected={expected} "
                f"signed deviation={deviation:.4f}; percentile={rank:.4f} "
                f"(history_n={len(history)}, target_n={target_depth}); "
                f"window_degenerate={window_degenerate}; "
                f"fit_rejection={estimate.rejection_reason}"
            ),
            computed_metrics={
                "deviation": round(abs(deviation), 4),
                "deviation_signed": round(deviation, 4),
                "percentile": round(rank, 4),
                "window_degenerate": window_degenerate,
                "parametric_fit_used": indicator_class.N_L is None and not fit_rejected,
                "fit_pvalue": estimate.pvalue,
                "fit_rejected": fit_rejected,
            },
        )


def _magnitude_estimate(
    magnitude: float, history: list[float], indicator_class: IndicatorClass,
) -> ParametricEstimate:
    if indicator_class.N_L is None:
        return parametric_estimate(
            magnitude, history, indicator_class.severity_fallback_family,
            settings.goodness_of_fit_alpha,
        )
    return ParametricEstimate(ecdf_rank(magnitude, history))


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
