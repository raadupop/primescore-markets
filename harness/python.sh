#!/usr/bin/env bash
# Resolve Python without relying on console launchers from a moved virtualenv.
set -u
repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 64
if [ -n "${HARNESS_PYTHON:-}" ]; then
  interpreter=$HARNESS_PYTHON
elif [ -n "${VIRTUAL_ENV:-}" ] && [ -f "$VIRTUAL_ENV/Scripts/python.exe" ]; then
  interpreter="$VIRTUAL_ENV/Scripts/python.exe"
elif [ -n "${VIRTUAL_ENV:-}" ] && [ -f "$VIRTUAL_ENV/bin/python" ]; then
  interpreter="$VIRTUAL_ENV/bin/python"
elif [ -f "$repo_root/apps/classification/.venv/Scripts/python.exe" ]; then
  interpreter="$repo_root/apps/classification/.venv/Scripts/python.exe"
elif [ -f "$repo_root/apps/classification/.venv/bin/python" ]; then
  interpreter="$repo_root/apps/classification/.venv/bin/python"
else
  interpreter=python
fi
exec "$interpreter" "$@"
