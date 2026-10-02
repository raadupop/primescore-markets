"""The local, Stop-hook and CI validation gate. Run with Python 3.12."""

from __future__ import annotations

import argparse
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
CLASSIFICATION = ROOT / "apps" / "classification"
TOOLS = (
    "pytest", "pytest_asyncio", "yaml", "jsonschema", "openapi_spec_validator",
    "importlinter", "ruff", "mypy", "xenon", "vulture",
)
ENGINE_SETTING_PREFIXES = ("engine__", "auth__", "fred__", "sources__", "classifier__", "registry__")


def has_changes(root: Path) -> bool:
    """Include staged edits, deletions, renames and untracked nonignored files."""
    result = subprocess.run(
        ["git", "status", "--porcelain", "-z", "--untracked-files=normal"],
        cwd=root, capture_output=True, check=True,
    )
    return bool(result.stdout)


def check_environment() -> dict[str, str]:
    """Use declared test settings, independent of operator environment or .env."""
    env = {
        key: value for key, value in os.environ.items()
        if not key.lower().startswith(ENGINE_SETTING_PREFIXES)
    }
    env.update(
        BOOTSTRAP_MODE="disabled",
        RUN_LIVE_BOOTSTRAP="",
        FRED_API_KEY="",
        TWELVE_DATA_API_KEY="",
        FINNHUB_API_KEY="",
        ANTHROPIC_API_KEY="",
        PRIMESCORE_REGISTRY_PATH=str(ROOT / "infra" / "registry.yaml"),
        REGISTRY_PATH=str(ROOT / "infra" / "registry.yaml"),
        GOODNESS_OF_FIT_ALPHA="0.05",
        DEGRADED_CERTAINTY_FACTOR="0.5",
        LOG_LEVEL="INFO",
        PYTEST_ADDOPTS="",
        PYTHONHASHSEED="0",
        PYTHONUTF8="1",
        PYTHONPATH=os.pathsep.join((str(ROOT), str(CLASSIFICATION))),
        PRIMESCORE_PYTHON=sys.executable,
        DOTNET_NOLOGO="1",
        DOTNET_CLI_TELEMETRY_OPTOUT="1",
    )
    return env


def require_tools(engine: bool = True) -> bool:
    missing = [name for name in TOOLS if importlib.util.find_spec(name) is None]
    for name in missing:
        print(f"FAILED dependency:{name}", flush=True)
    if missing:
        print("Install apps/classification/requirements-dev.txt with this interpreter.", flush=True)
    if engine and shutil.which("dotnet") is None:
        missing.append("dotnet")
        print("FAILED dependency:dotnet", flush=True)
        print("Install the .NET 10 SDK (apps/engine/global.json).", flush=True)
    return not missing


def bash_executable() -> str:
    """Git Bash on Windows, where the system bash.exe may launch WSL (ORACLE.md)."""
    if os.name == "nt":
        for base in (os.environ.get("ProgramFiles"), os.environ.get("ProgramW6432")):
            candidate = Path(base or "C:/Program Files") / "Git" / "bin" / "bash.exe"
            if candidate.is_file():
                return str(candidate)
    return shutil.which("bash") or "bash"


def run_check(name: str, args: list[str], cwd: Path, env: dict[str, str]) -> bool:
    print(f"CHECK {name}", flush=True)
    result = subprocess.run(args, cwd=cwd, env=env, check=False)
    if result.returncode:
        print(f"FAILED {name}", flush=True)
    return result.returncode == 0


def validate_contract(path: Path) -> bool:
    import yaml
    from openapi_spec_validator import validate

    name = f"contract:{path.relative_to(ROOT).as_posix()}"
    print(f"CHECK {name}", flush=True)
    try:
        validate(yaml.safe_load(path.read_text(encoding="utf-8")))
    except Exception as exc:
        print(f"{type(exc).__name__}: {exc}\nFAILED {name}", flush=True)
        return False
    return True


def check_file(file: str, env: dict[str, str]) -> bool:
    path = Path(file)
    if not path.is_absolute():
        path = ROOT / path
    path = path.resolve()
    if not path.is_relative_to(CLASSIFICATION) or path.suffix != ".py" or not path.is_file():
        raise ValueError("file must be an existing Python file under apps/classification/")
    relative = str(path.relative_to(CLASSIFICATION))
    results = [
        run_check("ruff", [sys.executable, "-m", "ruff", "check", relative], CLASSIFICATION, env),
        run_check("mypy", [sys.executable, "-m", "mypy", relative], CLASSIFICATION, env),
    ]
    return all(results)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--changed-only", action="store_true", help="skip only when the entire checkout is clean")
    mode.add_argument("--file", help="run the PostToolUse lint/type check on one classifier file")
    args = parser.parse_args(argv)
    if args.changed_only and not has_changes(ROOT):
        return 0
    if not require_tools(engine=not args.file):
        return 1
    env = check_environment()
    if args.file:
        return int(not check_file(args.file, env))
    results = [
        validate_contract(ROOT / "doc" / "PrimeScore-API-v1.yaml"),
        validate_contract(CLASSIFICATION / "doc" / "openapi.yaml"),
        run_check(
            "import-contracts",
            [sys.executable, "-c", "from importlinter.cli import lint_imports; lint_imports()"],
            CLASSIFICATION, env,
        ),
        run_check(
            "classification-tests",
            [sys.executable, "-m", "pytest", "tests/", "--tb=short", "-ra"],
            CLASSIFICATION, env,
        ),
    ]
    if any((ROOT / "tests").rglob("test_*.py")):
        results.append(run_check(
            "repository-tests",
            [sys.executable, "-m", "pytest", "tests/", "--tb=short", "-ra",
             "-o", "asyncio_default_fixture_loop_scope=function"],
            ROOT, env,
        ))
    results.append(run_check(
        "engine", [bash_executable(), str(ROOT / "harness" / "check-engine.sh")], ROOT, env,
    ))
    return int(not all(results))


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        print(f"{type(exc).__name__}: {exc}\nFAILED gate-setup", file=sys.stderr)
        raise SystemExit(1) from exc
