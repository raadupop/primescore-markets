"""
Fitness function: ruff reports zero violations against the configured rule set.

Lineage: Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017) — one
fitness function per architectural dimension, configuration-driven, not
bug-driven. Rule families (refactoring-catalogue, PEP 8, McCabe, Pylint-R,
flake8-simplify/bugbear) and scope are declared in `pyproject.toml` under
`[tool.ruff]`. This test is agnostic to which rules are enabled; it asserts
only that the project-configured set is clean.

Accepted-risk exemptions are expressed as `per-file-ignores` entries in
`pyproject.toml`, each annotated with the ADR that tracks their removal.
"""
from __future__ import annotations

import json

import pytest

from .conftest import run_python_module


def test_ruff_reports_zero_violations() -> None:
    """Project-configured ruff rule set must be clean on `app/`."""
    pytest.importorskip("ruff")
    result = run_python_module(
        "ruff",
        ["check", "--output-format=json", "app"],
    )
    assert result.returncode == 0, (
        f"ruff failed with exit code {result.returncode}.\n"
        f"STDOUT:\n{result.stdout}\nSTDERR:\n{result.stderr}"
    )
    findings = json.loads(result.stdout) if result.stdout.strip() else []
    assert findings == [], "ruff violations:\n" + "\n".join(
        f"  {f['filename']}:{f['location']['row']} {f['code']} {f['message']}"
        for f in findings
    )
