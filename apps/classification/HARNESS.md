# Classification harness

Implementation inventory for the five layers in
[project ADR-0001](../../doc/adr/0001-agent-harness-architecture.md).
The [validation gate contract](../../harness/ORACLE.md) owns invocation and
runtime details; [repository status](../../docs/STATUS.md) owns product state.

| Layer | Implemented control | Remaining boundary |
| --- | --- | --- |
| Context | [AGENTS.md](AGENTS.md), [OpenAPI](doc/openapi.yaml), [SRS](../../doc/srs/PrimeScore-SRS.md), [registry](../../infra/registry.yaml) | Authored context does not prove implementation or calibration. |
| Cognitive tools | Eight [project skills](../../.claude/skills/); fixture edits surface trader/statistician review reminders. | Strategy, registry, ADR, limitation and SRS reminder triggers remain unwired; [task registry](../../doc/todo/registry.yaml). |
| Permissions | [Claude settings](../../.claude/settings.json) and ignored local overrides | Runtime-specific configuration; no claim of a Codex adapter or sandbox enforcement across agents. |
| Feedback | PostToolUse lint/type check; Stop adapter; bounded steering manager; shared local/CI gate | Independent market-outcome validation remains unbuilt. |
| Decision durability | [Component ADRs](doc/adr/), [limitations](LIMITATIONS.md), this inventory | Accepted gaps must remain visible when the code passes. |

## Executable validation

| Check | Evidence / scope |
| --- | --- |
| HTTP and schema contract | [Contract tests](tests/acceptance/test_contract_shapes.py); both classifier and engine OpenAPI files validate in the shared gate. |
| Historical scenarios | [Anchor runner](tests/acceptance/test_anchor_events.py) and [fixture provenance](tests/acceptance/fixtures/ANCHORS.md). The three old macro fixtures remain skipped pending migration; market snapshots exercise signed scoring. |
| Mathematical axioms | [Signed-score tests](tests/acceptance/test_signed_score_axioms.py): bounds, direction, symmetry, zero-deviation, fallback and horizon sufficiency. |
| Structural fitness | [Architecture tests](tests/architecture/) invoke ruff, mypy, xenon and vulture on `app/`; [pyproject.toml](pyproject.toml) declares rules. The gate invokes import-linter separately for all six dependency contracts. |
| Bootstrap/readiness | [Health tests](tests/acceptance/test_health_acceptance.py), [bootstrap configuration tests](tests/integration/test_bootstrap_configuration.py). Normal gate execution disables providers; mocked fetchers test readiness failures. |
| Live bootstrap | [FRED integration](tests/integration/test_bootstrap.py), outside the normal gate: requires explicit `RUN_LIVE_BOOTSTRAP=1` and a FRED key. A skipped live test does not establish provider readiness. |
| Harness mechanics | [Repository tests](../../tests/harness/) exercise change detection, dependency preflight, provider isolation and the actual shell steering loop with a fixed test oracle. |

The gate preflights installed tools before pytest, so a missing validator
cannot produce a misleading green run through `importorskip`.
[CI](../../.github/workflows/checks.yml) runs the same gate on every push and PR
with Python 3.12 on Ubuntu and Windows. Test summaries retain intentional skips.

## Hook wiring

[Claude adapters](../../.claude/hooks/) parse the agent payload and call the
[agent-neutral scripts](../../harness/). PostToolUse checks only classifier
`app/**/*.py` and anchor fixture JSON. Stop checks the entire dirty checkout,
including staged edits and new files; a clean checkout skips.

The [steering manager](../../harness/STEERING.md) fingerprints failed tests and
named gate stages. The first red blocks; changed failures permit another
attempt up to the retry budget; unchanged failures or an exhausted budget
escalate to the operator. A Stop re-entry still runs the gate. A terminal
allow decision is not a green result when it contains an escalation notice.

Shell scripts use explicit Bash delegation and LF endings. They do not rely
on executable Git modes or venv console launchers from the repository's old
location. No CI or hook invokes specialist skills automatically.

## Unbuilt validation

[Component ADR-0003](doc/adr/0003-test-oracle-architecture.md) specifies an
independent classification backtest against post-event IV outcomes. No such
oracle exists. Source-tagged scenarios and mathematical checks establish
covered behavior, not forecast calibration or trade profitability.

Cross-asset and geopolitical historical anchors await those strategies.
Registry calibration and additional symbols per class remain in
[LIMITATIONS.md](LIMITATIONS.md); mutation testing and broader property-based
coverage remain deferred.

For red tests, fix implementation when the requirement is correct; identify
a harness bug when its assertion is wrong. For an externally observed escape,
record an ADR plus a new control, or an accepted limitation, per
[project ADR-0001](../../doc/adr/0001-agent-harness-architecture.md).
