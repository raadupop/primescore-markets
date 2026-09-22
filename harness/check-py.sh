#!/usr/bin/env bash
# PostToolUse lint and typing check. Contract: harness/ORACLE.md.
set -u
[ "$#" -eq 1 ] || { printf 'usage: check-py.sh <file>\n' >&2; exit 64; }
repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 64
exec bash "$repo_root/harness/python.sh" "$repo_root/scripts/checks.py" --file "$1"
