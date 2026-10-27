#!/usr/bin/env bash
# ship-plan.sh: plan → worktree → implement (TDD) → verify → review/fix → docs → (PR)
# usage: ./ship-plan.sh docs/superpowers/plans/my-feature.md [branch]
# resume: FROM=<step> ./ship-plan.sh ...  steps: isolate implement verify review docs handoff
set -euo pipefail

PLAN=$(realpath "${1:?usage: ship-plan.sh <plan.md> [branch]}")
BRANCH=${2:-feat/$(basename "$PLAN" .md)}
BASE=${BASE:-main}
TEST_CMD=${TEST_CMD:-dotnet build && dotnet test}   # CLAUDE.md: both must pass
MAX_FIX_ROUNDS=${MAX_FIX_ROUNDS:-2}
CODER=(--model "${CODER_MODEL:-sonnet}")   # implement + fix
THINKER=(--model "${THINKER_MODEL:-opus}") # review + docs
STEPS=(isolate implement verify review docs handoff)
FROM=${FROM:-isolate}
[[ " ${STEPS[*]} " == *" $FROM "* ]] || { echo "✖ FROM must be one of: ${STEPS[*]}" >&2; exit 1; }
REPO=$(git rev-parse --show-toplevel)
WT="$REPO/../$(basename "$REPO")-${BRANCH//\//-}"
LOG="$WT.ship-log"   # outside the worktree so logs never get committed

PLAN_REL=${PLAN#"$REPO"/}
[[ $PLAN_REL != "$PLAN" ]] || { echo "✖ plan must live inside $REPO" >&2; exit 1; }

# One fresh headless session per phase; fails the script if the agent errors.
# Streams the agent's text and tool calls to stderr live; stdout gets only the final result.
agent() {
  local phase=$1 prompt=$2; shift 2
  echo "▶ $phase" >&2
  claude -p "$prompt" --output-format stream-json --verbose --max-turns 80 "$@" \
    | tee -a "$LOG/$phase.json" \
    | jq -rj --unbuffered 'select(.type == "assistant") | .message.content[]
        | if .type == "text" then .text + "\n"
          elif .type == "tool_use" then "  · \(.name) \(.input.command // .input.file_path // .input.pattern // "")\n"
          else empty end' >&2 || true
  local res
  res=$(jq -c 'select(.type == "result")' "$LOG/$phase.json" | tail -n 1)   # log appends across resumed runs
  [[ -n $res && $(jq -r .is_error <<<"$res") != true ]] || { echo "✖ $phase: agent error" >&2; exit 1; }
  jq -r .result <<<"$res"
}

# Deterministic gate: the script, not the model, decides if build and tests pass.
gate() {
  echo "▶ gate: $1" >&2
  bash -c "$TEST_CMD" >"$LOG/gate-$1.txt" 2>&1 \
    || { echo "✖ build/tests failed after $1, see $LOG/gate-$1.txt" >&2; exit 1; }
}

EDIT=(--permission-mode acceptEdits
      --allowedTools "Bash(dotnet build:*)" "Bash(dotnet test:*)" "Bash(dotnet ef:*)" "Bash(dotnet tool restore)"
                     "Bash(dotnet sln:*)" "Bash(dotnet add:*)"
                     "Bash(git status)" "Bash(git log:*)" "Bash(git grep:*)" "Bash(git mv:*)" "Bash(git rm:*)"
                     "Bash(git diff:*)" "Bash(git add:*)" "Bash(git commit:*)")
READONLY=(--allowedTools Read Grep Glob "Bash(git diff:*)" "Bash(git log:*)" --disallowedTools Edit Write)

# True if step $1 is at or after FROM, i.e. it should run this time.
run() {
  local s
  for s in "${STEPS[@]}"; do
    [[ $s == "$FROM" ]] && return 0
    [[ $s == "$1" ]] && return 1
  done
}

# ── 3. Isolate ─────────────────────────────────────────────
if run isolate; then
  git -C "$REPO" worktree add -b "$BRANCH" "$WT" "$BASE"
  mkdir -p "$LOG"
  cd "$WT"
  bash -c "${SETUP_CMD:-dotnet tool restore}"   # dotnet-ef, for migrations
  gate baseline   # don't start on a red baseline

  # CLAUDE.md: the plan is committed with its changes. Bring it over if BASE doesn't have it.
  if [[ ! -f $PLAN_REL ]]; then
    mkdir -p "$(dirname "$PLAN_REL")"
    cp "$PLAN" "$PLAN_REL"
    git add "$PLAN_REL"
    git commit -m "docs(plans): add $(basename "$PLAN_REL" .md)"
  fi
else
  [[ -d $WT ]] || { echo "✖ can't resume from $FROM: no worktree at $WT" >&2; exit 1; }
  mkdir -p "$LOG"
  cd "$WT"
  echo "↻ resuming from $FROM in $WT" >&2
fi

# ── 4. Implement ───────────────────────────────────────────
run implement && agent implement "Execute the implementation plan at $PLAN_REL, task by task, following CLAUDE.md.
Some tasks may already be committed by an earlier run: check git log and continue from the first unfinished task.
For each task use superpowers:test-driven-development: write the failing test, watch it fail,
write the minimal code, watch it pass, commit with a conventional-commit message (e.g. feat(vouchers): ...).
Run dotnet build and dotnet test before each commit; warnings are errors.
Don't edit the plan. If the plan is wrong or blocked, stop and explain why." "${EDIT[@]}" "${CODER[@]}" >/dev/null

# ── 6. Verify ──────────────────────────────────────────────
if run verify; then
  gate implement
  # SMOKE_CMD: optional, e.g. a script that runs the API and curls an endpoint
  if [[ -n ${SMOKE_CMD:-} ]]; then
    bash -c "$SMOKE_CMD" >"$LOG/smoke.txt" 2>&1 || { echo "✖ smoke failed" >&2; exit 1; }
  fi
fi

# ── 7. Review → fix loop (reviewer is read-only, fresh context) ───
UNRESOLVED=""   # shortcut: not recovered when resuming past review; read $LOG/review-*.json if needed
if run review; then
  for round in $(seq 1 "$MAX_FIX_ROUNDS"); do
    findings=$(agent "review-$round" "Review this branch: git diff $BASE...HEAD.
Look for correctness bugs, security issues (input validation, secrets, auth), missing tests
for risky logic, code that isn't needed, and breaks of the CLAUDE.md rules (layer dependency
direction, thin endpoints, CancellationToken passed through, a test for every behaviour change).
Read surrounding code, not only the diff.
Reply with a numbered list of concrete findings (file:line, problem, failing scenario),
or exactly NO_FINDINGS." "${READONLY[@]}" "${THINKER[@]}")
    [[ $findings == *NO_FINDINGS* ]] && { UNRESOLVED=""; break; }
    UNRESOLVED=$findings
    agent "fix-$round" "Address these review findings. Check each one against the code first;
skip and explain any that are wrong. Add a test for each real bug. Commit the fixes.

$findings" "${EDIT[@]}" "${CODER[@]}" >/dev/null
    gate "fix-$round"
  done
fi

# ── + Docs ─────────────────────────────────────────────────
if run docs; then
  before=$(git rev-parse HEAD)
  agent docs "Update the documentation to match git diff $BASE...$before:
README.md (domain rules, use cases, endpoint table, configuration, testing, commands) and CLAUDE.md
if a rule changed. Edit Markdown docs only, no code, and don't edit docs/superpowers/.
Commit as 'docs: ...'. If nothing needs updating, change nothing." "${EDIT[@]}" "${THINKER[@]}" >/dev/null
  if git diff --name-only "$before" HEAD | grep -vE '\.md$'; then
    echo "✖ docs phase touched non-doc files (listed above)" >&2; exit 1
  fi
  gate docs
fi

# ── Hand-off (merging stays human) ─────────────────────────
cost=$(jq -s 'map(.total_cost_usd // 0) | add' "$LOG"/*.json)
echo "✔ $BRANCH ready in $WT  (agent cost: \$$cost, logs: $LOG)"
[[ -n $UNRESOLVED ]] && echo "⚠ review findings still open after $MAX_FIX_ROUNDS rounds:"$'\n'"$UNRESOLVED"
if [[ ${OPEN_PR:-0} == 1 ]]; then   # needs an origin remote; this repo has none yet
  git push -u origin "$BRANCH"
  gh pr create --base "$BASE" --head "$BRANCH" --fill \
    --body "Automated from \`$PLAN_REL\`.${UNRESOLVED:+$'\n\n'**Open review findings:**$'\n'$UNRESOLVED}"
fi
