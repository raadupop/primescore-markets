#!/usr/bin/env bash
# Shared local / CI / Stop-hook gate. Contract: harness/ORACLE.md.
set -u
repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 64
exec bash "$repo_root/harness/python.sh" "$repo_root/scripts/checks.py" "$@"
