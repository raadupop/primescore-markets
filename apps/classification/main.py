"""
Application entry point — FastAPI app, lifecycle hooks, and endpoint definitions.

Bootstrap lives in `app.bootstrap`; this module wires it into the FastAPI
lifespan and exposes the HTTP surface (`/health`, `/classify`).
"""
import logging
from contextlib import asynccontextmanager
from datetime import datetime, timezone

from fastapi import FastAPI, Request, status
from fastapi.responses import JSONResponse

from app.bootstrap import populate_windows
from app.config import settings
from app.models.requests import ClassifyRequest
from app.models.responses import ClassifyResponse
from app.routing.dispatcher import dispatch
from app.state import state

logging.basicConfig(level=settings.log_level)
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Startup → yield → shutdown."""
    await populate_windows()
    yield
    logger.info("Shutting down.")


# ---------------------------------------------------------------------------
# App instance
# ---------------------------------------------------------------------------

app = FastAPI(
    title="PrimeScore AI Classification Service",
    version="0.2.0",
    description="HTTP classification engine for financial signal events.",
    lifespan=lifespan,
)

# ---------------------------------------------------------------------------
# Exception handlers
# ---------------------------------------------------------------------------


@app.exception_handler(ValueError)
async def value_error_handler(request: Request, exc: ValueError) -> JSONResponse:
    return JSONResponse(
        status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
        content={"detail": str(exc)},
    )


@app.exception_handler(NotImplementedError)
async def not_implemented_handler(request: Request, exc: NotImplementedError) -> JSONResponse:
    return JSONResponse(
        status_code=status.HTTP_501_NOT_IMPLEMENTED,
        content={"detail": str(exc)},
    )


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------


def _build_window_staleness() -> dict[str, dict[str, object]]:
    """Per-symbol staleness for /health. Keys are namespaced
    `<SOURCE_CATEGORY>/<symbol>` per the openapi contract."""
    now = datetime.now(timezone.utc)
    out: dict[str, dict[str, object]] = {}
    for symbol, rw in state.windows.items():
        indicator_class = rw.indicator_class
        key = f"{indicator_class.source_category}/{symbol}"
        entry: dict[str, object] = {
            "indicator_class": indicator_class.name,
            "values_count": len(rw.values),
        }
        if rw.last_update:
            entry["last_update"] = rw.last_update.isoformat()
            entry["staleness_seconds"] = round((now - rw.last_update).total_seconds(), 1)
        out[key] = entry
    return out


@app.get("/health")
async def health() -> JSONResponse:
    """Readiness probe. 200 once bootstrap completes; 503 during startup."""
    if state.is_ready:
        return JSONResponse(
            status_code=status.HTTP_200_OK,
            content={
                "status": "ready",
                "windows": _build_window_staleness(),
            },
        )
    return JSONResponse(
        status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
        content={"status": "not_ready"},
    )


@app.post("/classify", response_model=ClassifyResponse)
async def classify(request: ClassifyRequest) -> ClassifyResponse:
    """Main classification endpoint.

    Readiness concerns the process-local windows; a request that carries its own
    reference window does not use them and is served while bootstrapping (ADR-0005).
    """
    if not state.is_ready and request.reference_window is None:
        return JSONResponse(  # type: ignore[return-value]
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={"detail": "Service is bootstrapping — not ready yet."},
        )
    return await dispatch(request)
