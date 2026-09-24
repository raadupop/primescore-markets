#!/usr/bin/env bash
# .NET engine stage of the shared gate: build (warnings are errors), then the structural,
# unit and API acceptance suites. Contract: harness/ORACLE.md.
#
# Failures are reported as `FAILED engine-build:<file>:<code>` and `FAILED engine:<test>`
# lines so the steering fingerprint (harness/STEERING.md) sees stable identifiers.
set -u

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 64
solution="$repo_root/apps/engine/PrimeScore.Engine.sln"

if ! command -v dotnet >/dev/null 2>&1; then
  printf 'FAILED dependency:dotnet\n'
  exit 1
fi

export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

report=$(mktemp)
trap 'rm -f "$report"' EXIT

printf 'CHECK engine-build\n'
dotnet build "$solution" --nologo -v quiet -clp:NoSummary >"$report" 2>&1
if [ $? -ne 0 ]; then
  cat "$report"
  # One identifier per distinct compiler or MSBuild error, e.g. Foo.cs:CS0103.
  grep -oE '[^\\/ ]+\.(cs|csproj|razor|props|targets)\([0-9]+,[0-9]+\): error [A-Z]+[0-9]+' "$report" \
    | sed -E 's/\([0-9]+,[0-9]+\): error /:/' | sort -u | sed 's/^/FAILED engine-build:/'
  printf 'FAILED engine-build\n'
  exit 1
fi

printf 'CHECK engine-tests\n'
# A hung test host is killed and reported as a failure instead of outlasting the Stop hook.
dotnet test "$solution" --no-build --nologo --blame-hang-timeout 4m >"$report" 2>&1
status=$?
# "  Failed <display name> [12 ms]": theory rows carry "(arg: value)" with spaces, so keep
# everything up to the trailing duration and turn whitespace into underscores (one token).
failed=$(sed -nE 's/^[[:space:]]+Failed (.+) \[[^]]*\][[:space:]]*$/\1/p' "$report" | sed -E 's/[[:space:]]+/_/g' | sort -u)
if [ "$status" -ne 0 ] || [ -n "$failed" ]; then
  cat "$report"
  for test in $failed; do printf 'FAILED engine:%s\n' "$test"; done
  [ -n "$failed" ] || printf 'FAILED engine-tests\n'
  exit 1
fi

grep -E '^(Passed|Failed)!' "$report" | sed -E 's/, Duration: [^-]+//'
exit 0
