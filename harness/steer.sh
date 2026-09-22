#!/usr/bin/env bash
# Steering manager: agent-agnostic decision layer.
# Owns per-turn state, runs the oracle scripts, decides block vs. allow vs.
# escalate. Contract: harness/STEERING.md.

set -u

cmd=${1:-}
[ "$cmd" = "stop" ] || { printf 'usage: steer.sh stop --session=<id>\n' >&2; exit 64; }
shift

session=""
for arg in "$@"; do
  case "$arg" in
    --session=*) session=${arg#--session=} ;;
    *) printf 'unknown arg: %s\n' "$arg" >&2; exit 64 ;;
  esac
done
[ -n "$session" ] || { printf 'missing --session\n' >&2; exit 64; }

# Sanitize session id for filesystem use (alnum, dash, underscore only).
safe_session=$(printf '%s' "$session" | tr -c 'A-Za-z0-9_-' '_')

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || {
  printf 'not inside a git repo\n' >&2; exit 64
}

state_dir="$repo_root/.harness-state"
mkdir -p "$state_dir"
state_file="$state_dir/$safe_session.json"
log_file="$state_dir/decisions.log"

max_attempts=${HARNESS_MAX_ATTEMPTS:-3}
case "$max_attempts" in
  ''|*[!0-9]*|0) printf 'HARNESS_MAX_ATTEMPTS must be a positive integer\n' >&2; exit 64 ;;
esac

now() { date -u +%Y-%m-%dT%H:%M:%SZ; }

log_decision() {
  local decision=$1 attempts=${2:-0}
  jq -cn --arg ts "$(now)" --arg session "$session" --arg decision "$decision" \
    --argjson attempts "$attempts" \
    '{ts: $ts, session: $session, decision: $decision, attempts: $attempts}' >> "$log_file"
}

clear_state() { rm -f "$state_file"; }

load_attempts() {
  [ -f "$state_file" ] || { printf '0'; return; }
  jq -r '.attempts // 0' "$state_file" 2>/dev/null || printf '0'
}

load_fingerprint() {
  [ -f "$state_file" ] || { printf ''; return; }
  jq -r '.prior_fingerprint // ""' "$state_file" 2>/dev/null || printf ''
}

save_state() {
  local attempts=$1 fp=$2
  jq -n --argjson a "$attempts" --arg fp "$fp" \
    '{attempts: $a, prior_fingerprint: $fp}' > "$state_file"
}

fingerprint() {
  # Stable pytest IDs plus named contract/tool stages. Fall back for setup errors.
  local failures
  failures=$(printf '%s' "$1" | grep -E '^(FAILED|ERROR) ' | awk '{print $2}' | sort -u)
  if [ -z "$failures" ]; then failures=$1; fi
  printf '%s' "$failures" | sha256sum | awk '{print $1}'
}

# Run the oracle. --changed-only short-circuits when nothing changed.
report=$(bash "$repo_root/harness/check-suite.sh" --changed-only 2>&1)
oracle_rc=$?

# Skip path: --changed-only returned 0 with empty output.
if [ "$oracle_rc" -eq 0 ] && [ -z "$report" ]; then
  log_decision "skip:no_edits" "$(load_attempts)"
  clear_state
  exit 0
fi

# Green path.
if [ "$oracle_rc" -eq 0 ]; then
  log_decision "green" "$(load_attempts)"
  clear_state
  exit 0
fi

# Red path. Compute fingerprint and consult state.
fp=$(fingerprint "$report")
attempts=$(load_attempts)
prior_fp=$(load_fingerprint)

if [ "$attempts" -eq 0 ]; then
  save_state 1 "$fp"
  log_decision "block:first_attempt" 1
  printf '%s\n' "$report"
  exit 2
fi

if [ "$fp" = "$prior_fp" ]; then
  log_decision "escalate:stuck" "$attempts"
  clear_state
  printf 'Convergence: failing tests unchanged after retry. Surfacing to operator.\n\n%s\n' "$report"
  exit 0
fi

next=$((attempts + 1))
if [ "$next" -gt "$max_attempts" ]; then
  log_decision "escalate:budget_exhausted" "$attempts"
  clear_state
  printf 'Budget exhausted (%s attempts). Surfacing to operator.\n\n%s\n' "$attempts" "$report"
  exit 0
fi

save_state "$next" "$fp"
log_decision "block:progress" "$next"
printf '%s\n' "$report"
exit 2
