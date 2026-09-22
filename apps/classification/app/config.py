"""
Typed settings loaded from environment variables / .env file.
"""
from pathlib import Path

from pydantic_settings import BaseSettings, SettingsConfigDict

from app.registry import Registry, load_registry

# Default registry location: <repo_root>/infra/registry.yaml.
# This file lives at apps/classification/app/config.py, so the repo root is
# three parents up.
_DEFAULT_REGISTRY_PATH = (
    Path(__file__).resolve().parents[3] / "infra" / "registry.yaml"
)


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8")

    anthropic_api_key: str = ""
    twelve_data_api_key: str = ""
    fred_api_key: str = ""
    finnhub_api_key: str = ""
    log_level: str = "INFO"

    # Path to the indicator registry. Override via PRIMESCORE_REGISTRY_PATH.
    registry_path: Path = _DEFAULT_REGISTRY_PATH


# Singleton — import `settings` from anywhere in the app.
settings = Settings()

# Singleton — import `registry` from anywhere in the app.
# Loaded eagerly at import time so schema errors fail loud at startup,
# not on the first /classify request.
registry: Registry = load_registry(settings.registry_path)
