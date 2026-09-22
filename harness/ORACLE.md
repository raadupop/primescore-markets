# Validation gate contract

[`scripts/checks.py`](../scripts/checks.py) is the shared local, Stop-hook and
CI gate. [`check-suite.sh`](check-suite.sh) selects Python and delegates to it;
[`steer.sh`](steer.sh) owns retry decisions and session state.

## Guarantees and limits

- The gate requires its validation tools; missing dependencies are red, never a skipped fitness check.
- Test subprocesses use the checked-in registry and declared statistical defaults, disable provider bootstrap and live integration opt-in, clear provider keys and external pytest selection flags, and use a fixed hash seed. Live API tests run separately.
- Assertions produce repeatable verdicts for the same code, dependencies and supported runtime. Timing text, temporary test files and ignored tool caches can vary. This is not byte-identical stdout or a claim of filesystem purity.
- The gate does not edit tracked files or write `.harness-state/`. Its output is plain text; named `FAILED` markers identify failed dependencies, contracts and stages for the steering manager.
- Passing validates the implemented contracts and covered cases. It does not validate predictive usefulness, trading returns or unimplemented model behavior.

## Commands

From the repository root:

```sh
bash harness/check-suite.sh
# Equivalent after activating the intended Python environment:
python scripts/checks.py
```

The gate runs both OpenAPI validators, all six dependency-boundary contracts,
the classifier suite (including ruff, mypy, xenon and vulture), and root
`tests/` when present. It reports skip reasons and returns `0` only when all
stages pass; validation/setup failure returns `1`.

`--changed-only` skips with empty stdout only when `git status` shows no staged,
unstaged or untracked nonignored files anywhere in the checkout. Registry,
harness, workflow, documentation and new demo files all count as changes.
CI never uses this flag.

`bash harness/check-py.sh <file>` runs ruff and mypy for an existing Python file
inside `apps/classification/`. Paths outside that component are rejected.

`bash harness/fixture-reminder.sh <file>` prints the source-provenance and
independent-band review reminder for an acceptance fixture. It is advisory;
invalid invocation returns `64`.

## Runtime

Python 3.12 with
[`requirements-dev.txt`](../apps/classification/requirements-dev.txt); Git and
Bash. Claude adapters and the steering regression tests additionally require
`jq`. CI exercises Ubuntu and Windows. On Windows use Git Bash; the Windows
system `bash.exe` may launch WSL.

[`python.sh`](python.sh) selects `HARNESS_PYTHON` when set, then an activated
`VIRTUAL_ENV`, then the component `.venv`, then `python` on PATH. Tools execute
through that interpreter, avoiding console launchers tied to a moved venv.
