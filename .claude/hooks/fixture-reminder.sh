#!/usr/bin/env bash
# Adapter (Claude Code): PostToolUse on Edit|Write of anchor fixtures.
# Reads CC's stdin payload, scope-filters, delegates to
# harness/fixture-reminder.sh, wraps output as CC additionalContext.
# Advisory only; never blocks.
#
# Wired by .claude/settings.json. See harness/ORACLE.md.

set -u

f=$(jq -r '.tool_input.file_path // empty')
[ -n "$f" ] || exit 0

norm=$(printf '%s' "$f" | tr '\\' '/')
case "$norm" in
  */apps/classification/tests/acceptance/fixtures/*.json) ;;
  apps/classification/tests/acceptance/fixtures/*.json) ;;
  *) exit 0 ;;
esac

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0

text=$(bash "$repo_root/harness/fixture-reminder.sh" "$f" 2>&1) || exit 0

jq -n --arg t "$text" '{
  hookSpecificOutput: {
    hookEventName: "PostToolUse",
    additionalContext: $t
  }
}'
