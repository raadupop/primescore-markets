"""Exercise the real shell decision loop using a fixed oracle, never pytest recursively."""

import json
import os
from pathlib import Path
import shutil
import subprocess

import pytest


ROOT = Path(__file__).resolve().parents[2]


@pytest.fixture
def steering(tmp_path: Path):
    if os.name == "nt":
        bash = str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe")
    else:
        bash = shutil.which("bash")
    assert bash and Path(bash).exists(), "Bash is required for the hook regression suite"
    assert subprocess.run([bash, "-c", "command -v jq"], capture_output=True).returncode == 0, "jq is required"
    subprocess.run(["git", "init", str(tmp_path)], check=True, capture_output=True)
    (tmp_path / "harness").mkdir()
    (tmp_path / ".claude/hooks").mkdir(parents=True)
    shutil.copyfile(ROOT / "harness/steer.sh", tmp_path / "harness/steer.sh")
    shutil.copyfile(ROOT / ".claude/hooks/stop-pytest.sh", tmp_path / ".claude/hooks/stop-pytest.sh")
    (tmp_path / "harness/check-suite.sh").write_text(
        '#!/usr/bin/env bash\ncat oracle-output\nexit "$(cat oracle-exit)"\n',
        encoding="utf-8", newline="\n",
    )
    (tmp_path / "invoke.sh").write_text(
        'bash harness/steer.sh stop --session="$HARNESS_TEST_SESSION"\n',
        encoding="utf-8", newline="\n",
    )
    env = os.environ.copy()
    env["HARNESS_MAX_ATTEMPTS"] = "3"

    def invoke(report: str, code: int = 1, session: str = "test-session", adapter: bool = False, **overrides):
        (tmp_path / "oracle-output").write_text(report, encoding="utf-8")
        (tmp_path / "oracle-exit").write_text(str(code), encoding="utf-8")
        return subprocess.run(
            [bash, ".claude/hooks/stop-pytest.sh" if adapter else "invoke.sh"], cwd=tmp_path,
            env=env | {"HARNESS_TEST_SESSION": session} | overrides, capture_output=True, text=True, check=False,
            input=json.dumps({"session_id": session, "stop_hook_active": True}) if adapter else None,
        )

    return tmp_path, invoke


def decisions(root: Path) -> list[str]:
    return [json.loads(line)["decision"] for line in (root / ".harness-state/decisions.log").read_text().splitlines()]


def test_unchanged_failure_escalates_and_clears_state(steering) -> None:
    root, invoke = steering
    assert invoke("FAILED test_a - first assertion\n").returncode == 2
    result = invoke("FAILED test_a - different assertion\n")
    assert result.returncode == 0 and "Convergence" in result.stdout
    assert decisions(root) == ["block:first_attempt", "escalate:stuck"]
    assert not (root / ".harness-state/test-session.json").exists()


def test_progress_is_bounded_even_when_failures_change(steering) -> None:
    root, invoke = steering
    for failure in ("test_a", "test_b", "test_c"):
        assert invoke(f"FAILED {failure}\n").returncode == 2
    result = invoke("FAILED test_d\n")
    assert result.returncode == 0 and "Budget exhausted (3 attempts)" in result.stdout
    assert decisions(root)[-1] == "escalate:budget_exhausted"
    assert not (root / ".harness-state/test-session.json").exists()


def test_contract_and_collection_failures_are_distinct_progress(steering) -> None:
    root, invoke = steering
    assert invoke("FAILED contract:engine\n").returncode == 2
    assert invoke("ERROR tests/broken_import.py\n").returncode == 2
    assert decisions(root) == ["block:first_attempt", "block:progress"]


@pytest.mark.parametrize("report,decision", [("", "skip:no_edits"), ("3 passed\n", "green")])
def test_green_and_no_edits_clear_prior_failure(steering, report: str, decision: str) -> None:
    root, invoke = steering
    assert invoke("FAILED test_a\n").returncode == 2
    assert invoke(report, code=0).returncode == 0
    assert decisions(root)[-1] == decision
    assert not (root / ".harness-state/test-session.json").exists()


def test_session_is_escaped_in_json_and_safe_for_paths(steering) -> None:
    root, invoke = steering
    session = '../quoted"session'
    assert invoke("FAILED test_a\n", session=session).returncode == 2
    entry = json.loads((root / ".harness-state/decisions.log").read_text())
    assert entry["session"] == session
    states = list((root / ".harness-state").glob("*.json"))
    assert len(states) == 1 and states[0].parent == root / ".harness-state"


def test_invalid_retry_budget_fails_with_usage_error(steering) -> None:
    _, invoke = steering
    result = invoke("FAILED test_a\n", HARNESS_MAX_ATTEMPTS="0")
    assert result.returncode == 64
    assert "positive integer" in result.stderr


def test_stop_reentry_still_validates_and_escalates(steering) -> None:
    root, invoke = steering
    result = invoke("FAILED test_a\n", adapter=True)
    assert result.returncode == 0
    assert json.loads(result.stdout)["decision"] == "block"
    result = invoke("FAILED test_a\n", adapter=True)
    assert result.returncode == 0 and "Convergence" in result.stdout
    assert decisions(root) == ["block:first_attempt", "escalate:stuck"]
