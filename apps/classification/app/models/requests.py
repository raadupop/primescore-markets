"""
Inbound contract models — ClassifyRequest and its nested payload types.
"""
from typing import Annotated, Any, Literal

from pydantic import AwareDatetime, BaseModel, ConfigDict, Field

# ---------------------------------------------------------------------------
# Enums expressed as Literal types (common Python idiom for small closed sets)
# ---------------------------------------------------------------------------

SourceCategory = Literal["MARKET_DATA", "MACROECONOMIC", "GEOPOLITICAL", "CROSS_ASSET_FLOW"]
PayloadType = Literal["STRUCTURED", "UNSTRUCTURED"]


# ---------------------------------------------------------------------------
# Structured payload schemas — one per source_category
# ---------------------------------------------------------------------------

# ToDo: find better name for MarketDataPayload. MarketDataPoint? MarketSignal? MarketObservation? The challenge is that "payload" is a bit too generic and doesn't convey the financial context, but we also want to avoid overly technical terms that might be confusing. Open to suggestions! 
class MarketDataPayload(BaseModel):
    symbol: str                          # e.g. "VIX"
    current_value: float
    timestamp: str                       # ISO-8601


class MacroeconomicPayload(BaseModel):
    """FRED release: actual vs. analyst estimate."""
    indicator: str                       # e.g. "CPI_YOY"
    actual: float
    expected: float
    release_timestamp: str


class GeopoliticalPayload(BaseModel):
    """GDELT structured event."""
    event_type: str                      # e.g. "MILITARY_ACTION"
    region: str
    severity_estimate: float = Field(ge=0.0, le=1.0)
    source_url: str | None = None


class CrossAssetFlowPayload(BaseModel):
    """Basket prices for pairwise correlation computation."""
    # Dict of ticker → price, e.g. {"SPY": 412.5, "TLT": 88.3, ...}
    basket_prices: dict[str, float]
    timestamp: str


class UnstructuredPayload(BaseModel):
    """Raw news / analyst text from GDELT DOC API."""
    text: str
    language: str = "en"
    source_url: str | None = None


# ---------------------------------------------------------------------------
# Top-level request
# ---------------------------------------------------------------------------

class ReferenceWindow(BaseModel):
    """Caller-supplied history for one request (ADR-0005).

    `values` are what the process-local window would hold for the symbol: prior
    levels for MARKET_DATA, prior surprise magnitudes for MACROECONOMIC. When
    present, the strategy computes against a transient copy and neither reads
    nor mutates process state.
    """
    model_config = ConfigDict(extra="forbid")

    values: list[Annotated[float, Field(allow_inf_nan=False)]]
    last_update: AwareDatetime | None = None  # ISO-8601 with an offset; None when no prior value


class ClassifyRequest(BaseModel):
    """
    The single inbound contract for POST /classify.

    Note: structured_payload is typed as `dict[str, Any]` here — the dispatcher
    re-parses it into the concrete payload class once it knows the source_category.
    """
    source_category: SourceCategory
    payload_type: PayloadType
    structured_payload: dict[str, Any] | None = None
    unstructured_payload: UnstructuredPayload | None = None
    reference_window: ReferenceWindow | None = None
