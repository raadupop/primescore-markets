# Steering manager contract

[`steer.sh`](steer.sh) owns retry decisions and transient session state.
The [oracle](ORACLE.md) owns validation; [Claude adapters](../.claude/hooks/)
translate runtime payloads into this interface.

```sh
bash harness/steer.sh stop --session=<id>
```

`HARNESS_MAX_ATTEMPTS` defaults to `3` and must be a positive integer.
Bash, Git and `jq` are required. The oracle selects Python as documented in
[ORACLE.md](ORACLE.md).

| Exit | Meaning |
| --- | --- |
| `0` | Allow stop: green, clean checkout, or escalation. Read advisory output before claiming completion. |
| `2` | Block stop; stdout contains the failed validation report. |
| `64` | Invalid invocation/configuration. |
| Other nonzero | Internal failure; adapter reports the error and allows operator intervention. |

## State and decisions

A sanitized session ID names `.harness-state/<session>.json`:

```json
{"attempts": 1, "prior_fingerprint": "<sha256>"}
```

`attempts` counts prior blocks. Terminal states remove this file. Append-only
`.harness-state/decisions.log` records UTC time, original session ID, decision
and attempt count as JSON Lines. These artifacts are ignored by Git.

| Oracle / prior state | Decision | Action |
| --- | --- | --- |
| Success with empty output | `skip:no_edits` | Clear state; allow. |
| Success with output | `green` | Clear state; allow. |
| First failure | `block:first_attempt` | Save attempt 1 and fingerprint; block. |
| Same failure fingerprint | `escalate:stuck` | Clear state; allow with unresolved failure report. |
| Changed failures, retry budget remains | `block:progress` | Increment attempts; block. |
| Changed failures, retry budget exhausted | `escalate:budget_exhausted` | Clear state; allow with unresolved failure report. |

The oracle receives `--changed-only`; its scope is the entire checkout,
including staged and untracked nonignored files. The Stop adapter validates
re-entry after a prior block; it does not treat `stop_hook_active` as proof
that the code passed. Across at most `HARNESS_MAX_ATTEMPTS + 1` consecutive
Stop decisions, the manager allows or escalates.

## Failure fingerprint

SHA-256 of sorted unique identifiers from `FAILED` and `ERROR` output lines.
Pytest IDs distinguish assertion and collection failures; named gate stages
cover invalid contracts, missing tools and other check failures. Assertion
text and line numbers do not turn the same failing test into progress.
If no identifier is available, hash the full diagnostic so distinct setup
errors do not all look like the same failure.

Changing the assertion message while the same test fails still escalates.
Fixing a failure and exposing another test permits another bounded retry.
An escalation is an unresolved result, never a green suite.

[Regression tests](../tests/harness/test_steering.py) run the real scripts
against a fixed test oracle in temporary Git repositories. They never invoke
the full pytest gate recursively.
