#!/usr/bin/env bash
# Adapter (Claude Code): PostToolUse on Edit|Write.
# Reads CC's stdin payload, scope-filters to apps/classification/app/*.py,
# delegates to harness/check-py.sh, wraps red as CC block JSON.
#
# Wired by .claude/settings.json. See harness/ORACLE.md.

set -u

f=$(jq -r '.tool_input.file_path // empty')
[ -n "$f" ] || exit 0

norm=$(printf '%s' "$f" | tr '\\' '/')
case "$norm" in
  */apps/classification/app/*.py) ;;
  apps/classification/app/*.py) ;;
  *) exit 0 ;;
esac

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0

out=$(bash "$repo_root/harness/check-py.sh" "$f" 2>&1); rc=$?
[ "$rc" -eq 0 ] && exit 0

printf '%s' "$out" \
  | jq -Rs --arg f "$f" '{
      decision: "block",
      reason: ("Lint/type errors in " + $f + ":\n" + .)
    }'
