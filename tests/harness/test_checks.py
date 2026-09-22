"""Regression controls for missing checks and Stop change-detection gaps."""

from pathlib import Path
import os
import shutil
import subprocess

import pytest

from scripts import checks


def git(root: Path, *args: str) -> None:
    subprocess.run(["git", *args], cwd=root, check=True, capture_output=True)


@pytest.fixture
def checkout(tmp_path: Path) -> Path:
    git(tmp_path, "init")
    (tmp_path / "tracked.txt").write_text("baseline\n", encoding="utf-8")
    git(tmp_path, "add", "tracked.txt")
    git(tmp_path, "-c", "user.name=Harness Test", "-c", "user.email=harness@example.invalid", "commit", "-m", "baseline")
    assert not checks.has_changes(tmp_path)
    return tmp_path


@pytest.mark.parametrize("path", [
    "apps/classification/new_strategy.py", "infra/registry.yaml", "harness/new-check.sh",
    "scripts/demo.py", "apps/demo/index.html", "tests/demo/test_replay.py", ".github/workflows/checks.yml",
])
def test_untracked_inputs_trigger_stop_gate(checkout: Path, path: str) -> None:
    changed = checkout / path
    changed.parent.mkdir(parents=True, exist_ok=True)
    changed.write_text("new input\n", encoding="utf-8")
    assert checks.has_changes(checkout)


def test_staged_changes_and_deletions_trigger_stop_gate(checkout: Path) -> None:
    tracked = checkout / "tracked.txt"
    tracked.write_text("updated\n", encoding="utf-8")
    git(checkout, "add", "tracked.txt")
    assert checks.has_changes(checkout)
    git(checkout, "-c", "user.name=Harness Test", "-c", "user.email=harness@example.invalid", "commit", "-m", "update")
    tracked.unlink()
    assert checks.has_changes(checkout)


def test_missing_fitness_tool_fails_before_pytest_can_skip(monkeypatch, capsys) -> None:
    original = checks.importlib.util.find_spec
    monkeypatch.setattr(checks.importlib.util, "find_spec", lambda name: None if name == "ruff" else original(name))
    assert checks.main([]) == 1
    assert "FAILED dependency:ruff" in capsys.readouterr().out


def test_gate_blocks_inherited_live_credentials_and_test_filters(monkeypatch, tmp_path: Path) -> None:
    for name in ("FRED_API_KEY", "RUN_LIVE_BOOTSTRAP", "PYTEST_ADDOPTS"):
        monkeypatch.setenv(name, "test-sentinel")
    probe = tmp_path / "probe.py"
    probe.write_text(
        "import os\n"
        "assert os.environ['BOOTSTRAP_MODE'] == 'disabled'\n"
        "assert all(os.environ[name] == '' for name in "
        "('FRED_API_KEY', 'RUN_LIVE_BOOTSTRAP', 'PYTEST_ADDOPTS'))\n",
        encoding="utf-8",
    )
    assert checks.run_check("isolation-probe", [checks.sys.executable, str(probe)], tmp_path, checks.check_environment())


def test_gate_uses_declared_settings_despite_environment_and_dotenv(monkeypatch, tmp_path: Path) -> None:
    overrides = {
        "PRIMESCORE_REGISTRY_PATH": "missing-primary.yaml",
        "REGISTRY_PATH": "missing-legacy.yaml",
        "GOODNESS_OF_FIT_ALPHA": "0.9",
        "DEGRADED_CERTAINTY_FACTOR": "0.95",
        "LOG_LEVEL": "invalid-log-level",
    }
    for name, value in overrides.items():
        monkeypatch.setenv(name, value)
    (tmp_path / ".env").write_text(
        "\n".join(f"{name}={value}" for name, value in overrides.items()), encoding="utf-8",
    )
    probe = tmp_path / "settings_probe.py"
    probe.write_text(
        "import sys\n"
        "from pathlib import Path\n"
        "from app.config import settings\n"
        "assert settings.registry_path == Path(sys.argv[1])\n"
        "assert settings.goodness_of_fit_alpha == 0.05\n"
        "assert settings.degraded_certainty_factor == 0.5\n"
        "assert settings.log_level == 'INFO'\n",
        encoding="utf-8",
    )
    assert checks.run_check(
        "settings-isolation-probe",
        [checks.sys.executable, str(probe), str(checks.ROOT / "infra/registry.yaml")],
        tmp_path, checks.check_environment(),
    )


def test_invalid_engine_contract_fails_gate(monkeypatch, tmp_path: Path, capsys) -> None:
    monkeypatch.setattr(checks, "ROOT", tmp_path)
    contract = tmp_path / "openapi.yaml"
    contract.write_text(
        "openapi: 3.0.3\ninfo: {title: Test, version: 1.0.0}\npaths: {}\n"
        "components:\n  schemas:\n    Payload:\n      type: object\n"
        "      properties: {value: {type: number}}\n      required: [baseline_value]\n",
        encoding="utf-8",
    )
    assert not checks.validate_contract(contract)
    assert "FAILED contract:openapi.yaml" in capsys.readouterr().out


def test_invalid_ruff_configuration_cannot_pass_fitness_check(tmp_path: Path) -> None:
    architecture = tmp_path / "tests/architecture"
    architecture.mkdir(parents=True)
    for package in (tmp_path / "tests", architecture):
        (package / "__init__.py").write_text("", encoding="utf-8")
    for name in ("conftest.py", "test_code_hygiene.py"):
        shutil.copyfile(checks.CLASSIFICATION / "tests/architecture" / name, architecture / name)
    (tmp_path / "app").mkdir()
    (tmp_path / "app/example.py").write_text("value = 1\n", encoding="utf-8")
    (tmp_path / "pyproject.toml").write_text(
        '[tool.ruff.lint]\nselect = ["NOT_A_REAL_RULE"]\n', encoding="utf-8",
    )
    result = subprocess.run(
        [checks.sys.executable, "-m", "pytest", "tests/architecture/test_code_hygiene.py",
         "-q", "--tb=short", "-o", "asyncio_default_fixture_loop_scope=function"],
        cwd=tmp_path, env=checks.check_environment(), capture_output=True, text=True, check=False,
    )
    assert result.returncode == 1, result.stdout + result.stderr
    assert "ruff failed with exit code 2" in result.stdout


@pytest.mark.parametrize("override,expected", [(False, "active"), (True, "override")])
def test_interpreter_selection_honors_active_environment(checkout: Path, override: bool, expected: str) -> None:
    bash = (
        str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe")
        if os.name == "nt" else shutil.which("bash")
    )
    assert bash and Path(bash).exists(), "Bash is required for the interpreter regression test"
    shutil.copyfile(checks.ROOT / "harness/python.sh", checkout / "python.sh")
    for directory, label in (("active/bin", "active"), ("apps/classification/.venv/bin", "fallback"), ("explicit", "override")):
        interpreter = checkout / directory / "python"
        interpreter.parent.mkdir(parents=True, exist_ok=True)
        interpreter.write_text(f"#!/usr/bin/env bash\nprintf '{label}'\n", encoding="utf-8", newline="\n")
        interpreter.chmod(0o755)
    env = os.environ.copy()
    env.pop("HARNESS_PYTHON", None)
    env["VIRTUAL_ENV"] = (checkout / "active").as_posix()
    if override:
        env["HARNESS_PYTHON"] = (checkout / "explicit/python").as_posix()
    result = subprocess.run([bash, "python.sh"], cwd=checkout, env=env, capture_output=True, text=True, check=False)
    assert result.returncode == 0, result.stderr
    assert result.stdout == expected
