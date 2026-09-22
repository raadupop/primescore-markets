"""
Indicator registry — single source of truth for calibration parameters,
symbol → class membership, and bootstrap provider mapping.

Loaded once at process startup from `infra/registry.yaml` (path overridable
via `Settings.registry_path`). Reload semantics: restart-only.

Schema is documented in `infra/registry.yaml` and `infra/README.md`.
"""
from __future__ import annotations

from pathlib import Path
from typing import Literal

import yaml  # type: ignore[import-untyped]
from pydantic import BaseModel, ConfigDict, Field, model_validator

DeviationKind = Literal["pct_change", "surprise_yoy", "corr_delta"]
Provider = Literal["fred", "finnhub", "twelve_data"]
DeriveKind = Literal["pct_change_yoy", "none"]
SourceCategory = Literal["MARKET_DATA", "MACROECONOMIC", "CROSS_ASSET_FLOW", "GEOPOLITICAL"]
Cadence = Literal["business_day", "calendar_day"]
SeverityFallbackFamily = Literal["gaussian", "log_gaussian", "none"]
_MIN_LONG_HORIZON = 278


class IndicatorClass(BaseModel):
    """Per-class calibration parameters. Shared across all member symbols.

    SRS v2.3.3 (CLS-001): `N_L` is the long-horizon ECDF window; `None`
    selects the parametric fallback path. `severity_fallback_family` is
    the parametric family used when `N_L is None`.
    """
    model_config = ConfigDict(extra="forbid", frozen=True)

    name: str
    source_category: SourceCategory
    N: int = Field(gt=0)
    N_L: int | None = None
    deviation_kind: DeviationKind
    severity_fallback_family: SeverityFallbackFamily = "none"
    expected_frequency_seconds: int = Field(gt=0)
    cadence: Cadence = "calendar_day"

    @model_validator(mode="after")
    def _validate_long_horizon(self) -> "IndicatorClass":
        if self.N_L is not None:
            if self.N_L < _MIN_LONG_HORIZON:
                raise ValueError(
                    f"IndicatorClass {self.name!r}: N_L={self.N_L} below SRS v2.3.3 "
                    "floor of 278 (|severity|=1 resolution threshold)."
                )
            if self.severity_fallback_family != "none":
                raise ValueError(
                    f"IndicatorClass {self.name!r}: severity_fallback_family must be "
                    "'none' when N_L is set (ECDF is the primary path)."
                )
        elif self.severity_fallback_family == "none":
            raise ValueError(
                f"IndicatorClass {self.name!r}: severity_fallback_family must be "
                "'gaussian' or 'log_gaussian' when N_L is None."
            )
        return self


class BootstrapSpec(BaseModel):
    """How to fetch initial history for a symbol at startup."""
    model_config = ConfigDict(extra="forbid", frozen=True)

    provider: Provider
    series_id: str
    derive: DeriveKind = "none"
    verified: bool = False


class SymbolEntry(BaseModel):
    """A registered symbol — its class membership and (optional) bootstrap spec."""
    model_config = ConfigDict(extra="forbid", frozen=True)

    symbol: str
    indicator_class: IndicatorClass
    bootstrap: BootstrapSpec | None = None


class Registry(BaseModel):
    """Loaded registry. Keys: class name → IndicatorClass; symbol → SymbolEntry."""
    model_config = ConfigDict(extra="forbid", frozen=True)

    classes: dict[str, IndicatorClass]
    symbols: dict[str, SymbolEntry]

    def get_symbol(self, symbol: str) -> SymbolEntry:
        """Look up a symbol or raise UnknownSymbolError."""
        try:
            return self.symbols[symbol]
        except KeyError as exc:
            raise UnknownSymbolError(symbol) from exc


class UnknownSymbolError(KeyError):
    """Raised when a /classify request names a symbol absent from the registry.

    Triggers the CLS-009 degraded-confidence fallback at the strategy layer.
    """

    def __init__(self, symbol: str) -> None:
        super().__init__(symbol)
        self.symbol = symbol


def load_registry(path: Path) -> Registry:
    """Read and validate the registry YAML. Raises on schema violation."""
    raw = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ValueError(f"Registry root must be a mapping, got {type(raw).__name__}")

    raw_classes = raw.get("classes") or {}
    raw_symbols = raw.get("symbols") or {}

    classes: dict[str, IndicatorClass] = {}
    for class_name, class_body in raw_classes.items():
        classes[class_name] = IndicatorClass(name=class_name, **class_body)

    symbols: dict[str, SymbolEntry] = {}
    for symbol, body in raw_symbols.items():
        class_name = body["class"]
        if class_name not in classes:
            raise ValueError(
                f"Symbol {symbol!r} references unknown class {class_name!r}"
            )
        bootstrap_body = body.get("bootstrap")
        bootstrap = BootstrapSpec(**bootstrap_body) if bootstrap_body else None
        symbols[symbol] = SymbolEntry(
            symbol=symbol,
            indicator_class=classes[class_name],
            bootstrap=bootstrap,
        )

    return Registry(classes=classes, symbols=symbols)
