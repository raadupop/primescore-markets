"""Run the offline replay dashboard: python scripts/demo.py [--port 8080]."""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8080)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("port must be in [1, 65535]")

    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root))
    sys.path.insert(0, str(root / "apps/classification"))
    # Set before importing classifier settings. This overrides .env/provider setup.
    os.environ["BOOTSTRAP_MODE"] = "disabled"
    os.environ["PRIMESCORE_REGISTRY_PATH"] = str(root / "infra/registry.yaml")
    os.environ["DEGRADED_CERTAINTY_FACTOR"] = "0.5"

    import uvicorn

    from apps.demo.replay_app import demo_app

    print(f"PrimeScore AI local replay: http://127.0.0.1:{args.port}", flush=True)
    uvicorn.run(demo_app(args.port), host="127.0.0.1", port=args.port, workers=1)


if __name__ == "__main__":
    main()
