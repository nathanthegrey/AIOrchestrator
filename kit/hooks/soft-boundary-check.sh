#!/usr/bin/env bash
# AI Orchestrator — the SOFT BOUNDARY inside one long turn.
#
# WHY THIS EXISTS, measured on the production VPS on 2026-09-09. Members now run one FRESH session
# per turn, so the carry BETWEEN turns is gone and the growth INSIDE one turn is what is left. The
# median member turn is 10 model calls; the tail runs 60 to 92 and hits the 30-minute deadline, at
# which point the bridge kills it and asks it for a closing report. **41 % of that night's member
# tokens sat in turns that ran to the deadline**, and the context inside such a turn climbs to about
# 200 k — so every later call in it is dearer than the one before.
#
# The closing turn is the HARD net and it already exists (Running/ClosingTurn/*). What was missing is
# a chance to stop BEFORE the axe, at a point the session picks rather than one the clock picks. That
# is all this is: past a threshold, ONE advisory into the model's context saying "reach a stable
# point, commit what is safe, write your report".
#
# IT ADVISES, IT NEVER BLOCKS (decision 21: hooks advise, the app enforces at the point of effect).
# There is no permissionDecision in the output and no exit code but 0 on any path. Every unexpected
# condition ALLOWS the call and says nothing.
#
# HOW THE ADVISORY REACHES THE MODEL — VERIFIED, not assumed. `hookSpecificOutput.additionalContext`
# from a PreToolUse hook was probed against the installed CLI 2.1.266 on 2026-09-09: the transcript
# records a `hook_additional_context` attachment rendered as
# `<system-reminder>PreToolUse:Bash hook additional context: …</system-reminder>`, and the model
# quoted the probe's magic string back verbatim while the tool call still ran. The `session_id`,
# `agent_type` and `tool_use_id` fields this script reads were captured from the same live payloads.
#
# ONE CLI SESSION IS ONE TURN in fresh mode, which is what makes counting on `session_id` a count of
# THIS TURN's calls. If a role is ever run resumed instead, the count spans the whole session — which
# advises earlier, never later, and is the safe direction.
#
# THE COUNT INCLUDES SUB-AGENTS, THE ADVISORY DOES NOT GO TO THEM. Measured 2026-09-09: a sub-agent's
# tool call carries the PARENT's `session_id` (and the parent's `transcript_path`), with `agent_type`
# naming the sub-agent. Their calls cost real tokens so they count; but telling a read-only Explore
# agent to commit and write a report is nonsense, and it would burn the once-per-turn latch on the
# wrong recipient — which would silence the advisory in exactly the fan-out-heavy turns it exists for.
# The discriminator is derived rather than guessed: the FIRST tool call of a session is always the
# main agent's (a sub-agent can only exist because the main agent called the Agent tool), so its
# `agent_type` is recorded once and every later call is compared against it.
#
# NO md5, NO `date`, NO `stat`, and the latch is a `mkdir` — portability by absence rather than by
# fallback (`.claude/rules/kit-and-scripts.md`).

set -u

# THE ROLES THAT DO THE WORK, AND ONLY THOSE. The supervisor's turn is the owner's phone line and the
# general supervisor's is their concierge: advising either of them to stop and report mid-sentence
# would interrupt the one conversation this whole system exists to protect. AIORCH_ROLE is set by the
# spawner, so an unset value is a session outside an orchestration and gets nothing.
case "${AIORCH_ROLE:-}" in
  implementer|reviewer|solo) ;;
  *) exit 0 ;;
esac

# THE THRESHOLD, IN ONE PLACE.
#
# 35 calls. The distribution it is set against, both measured: the pre-fresh baseline was p50 2 /
# p80 9 / p90 18 / max 59 calls per turn, and on 2026-09-09 with fresh turns the median was 10 and
# the maximum 92. So 35 leaves roughly the top tenth of turns to hear anything at all and says
# nothing to the ordinary ten-call turn — which is the point: the risk being managed here is SHORT
# THINKING (an agent that knows it may be stopped taking the smaller decision), and a generous
# ceiling is the mitigation the design named for it.
#
# Overridable by the environment so the bridge can tune it per role or per stage later without a kit
# change. A value that is not a plain number falls back to the default rather than disabling the
# advisory silently — a typo must not turn a guard off. `0` is the explicit OFF.
SOFT_BOUNDARY_CALLS="${AIORCH_SOFT_BOUNDARY_CALLS:-35}"

case "$SOFT_BOUNDARY_CALLS" in
  '' | *[!0-9]* ) SOFT_BOUNDARY_CALLS=35 ;;
esac

if [ "$SOFT_BOUNDARY_CALLS" -le 0 ] 2>/dev/null; then
  exit 0
fi

# A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS — see hook-log.sh for both halves.
# DEFINED FIRST, UNCONDITIONALLY, then overridden by the real one: a helper that EXISTS but is
# truncated (KitAssets_Installer overwrites the installed copy at every app start, so a hook firing
# during that copy sees a partial file) would otherwise leave the function undefined and the call
# would fail to stderr, the stream nobody reads.
aiorch_log_undecidable() { return 0; }

if [ -f "$(dirname "$0")/hook-log.sh" ]; then
  . "$(dirname "$0")/hook-log.sh" 2>/dev/null || true
fi

if ! INPUT=$(cat 2>/dev/null); then
  aiorch_log_undecidable "how many calls this turn has made" "the payload could not be read from stdin"
  exit 0
fi

# ONE EXTRACTION, THREE FIELDS, WRITTEN AS BYTES AND SEPARATED BY TABS.
#
# python3 is what every other hook here uses for its payload, and the bytes matter: on a mixed
# machine python3 is native Windows python, whose text-mode stdout turns every newline it writes into
# CRLF — a multi-line `print` would hand back values with a `\r` glued to the end of all but the
# last, and those values are compared for equality. `sys.stdout.buffer.write` with no trailing
# newline cannot be translated at all.
RAW=$(printf '%s' "$INPUT" | python3 -c 'import json,sys
d = json.load(sys.stdin)
sys.stdout.buffer.write("\t".join([
    str(d.get("session_id") or ""),
    str(d.get("agent_type") or ""),
    str(d.get("tool_use_id") or ""),
]).encode("utf-8"))' 2>/dev/null)

if [ -z "$RAW" ]; then
  aiorch_log_undecidable "which session this call belongs to" "the payload could not be parsed, so no session_id was extracted"
  exit 0
fi

IFS=$'\t' read -r SESSION AGENT CALL_ID <<< "$RAW"

# THE SESSION ID BECOMES A PATH, so it is validated rather than trusted. A CLI session id is a UUID;
# anything outside that alphabet cannot be counted safely and is not guessed at.
case "${SESSION:-}" in
  '' | *[!0-9A-Za-z-]* )
    aiorch_log_undecidable "which session this call belongs to" "the payload carried no usable session_id"
    exit 0
    ;;
esac

# ONE FILE PER CALL, NAMED BY tool_use_id — so the count needs no lock at all: the id is unique per
# call, a retry of the same call rewrites the same name, and parallel calls in one batch cannot
# collide. Only the LATCH has to be atomic, and that is a mkdir.
case "${CALL_ID:-}" in
  '' | *[!0-9A-Za-z_-]* ) CALL_ID="anon-$$-${RANDOM:-0}" ;;
esac

STATE_ROOT="${TMPDIR:-/tmp}/aiorch-soft-boundary"
CALLS_DIR="$STATE_ROOT/$SESSION.calls"
AGENT_FILE="$STATE_ROOT/$SESSION.agent"
FIRED_DIR="$STATE_ROOT/$SESSION.fired"

if [ ! -d "$CALLS_DIR" ]; then
  if ! mkdir -p "$CALLS_DIR" 2>/dev/null; then
    aiorch_log_undecidable "how many calls this turn has made" "the call counter under $STATE_ROOT could not be created"
    exit 0
  fi

  # THE FIRST CALL OF A SESSION IS THE MAIN AGENT'S, always — a sub-agent exists only because the
  # main agent called the Agent tool, which is itself a counted call that arrives here first. So this
  # is recorded once and never rewritten; it is the derived discriminator the header describes.
  printf '%s' "$AGENT" > "$AGENT_FILE" 2>/dev/null || true

  # Swept on a new session rather than on every call: one traversal per turn, of a folder holding a
  # handful of empty files per session. `find -mtime` is POSIX; `-mindepth 1` keeps the root itself
  # out of the reckoning, which is what would otherwise delete the folder being created.
  find "$STATE_ROOT" -mindepth 1 -maxdepth 1 -mtime +1 -exec rm -rf {} + 2>/dev/null || true
fi

: > "$CALLS_DIR/$CALL_ID" 2>/dev/null || true

COUNT=$(ls -1 "$CALLS_DIR" 2>/dev/null | wc -l | tr -d '[:space:]')

case "${COUNT:-}" in
  '' | *[!0-9]* )
    aiorch_log_undecidable "how many calls this turn has made" "the call counter under $CALLS_DIR could not be read"
    exit 0
    ;;
esac

if [ "$COUNT" -lt "$SOFT_BOUNDARY_CALLS" ]; then
  exit 0
fi

# A SUB-AGENT'S CALL COUNTS BUT IS NOT ADVISED — see the header. A missing marker file means the
# question cannot be answered, and there the advisory is DELIVERED and the inability recorded:
# advising the main agent one turn too eagerly costs nothing, and silently never advising anyone is
# the failure this whole hook exists to remove.
if [ -f "$AGENT_FILE" ]; then
  FIRST_AGENT=$(cat "$AGENT_FILE" 2>/dev/null || printf '')

  if [ "$AGENT" != "$FIRST_AGENT" ]; then
    exit 0
  fi
else
  aiorch_log_undecidable "whether this call is the main agent's or a sub-agent's" "the first-agent marker beside the call counter is missing"
fi

# ONCE PER SESSION, NOT ONCE PER CALL PAST THE THRESHOLD. A line repeated on all 57 remaining calls
# of a 92-call turn is a waterfall, which is the thing this system exists to prevent (decision 14) —
# and it would drown the advice it is trying to give. mkdir fails on an existing directory in a single
# atomic syscall, so two parallel calls crossing the threshold together cannot both fire.
if ! mkdir "$FIRED_DIR" 2>/dev/null; then
  exit 0
fi

# WORDING IS HALF THE MECHANISM. The named risk is SHORT THINKING: an agent that knows it may be
# stopped takes the smaller decision. So the advisory spends more words forbidding that than asking
# for the stop — continuing is always allowed, stopping is never rewarded, and the ask is only that
# the session choose its own stopping point instead of letting the clock choose one.
cat <<JSON
{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"SOFT BOUNDARY — you are $COUNT tool calls into this turn (the advisory point is $SOFT_BOUNDARY_CALLS). This is ADVICE: nothing is blocked, and it is said once per turn and never repeated.\\nWHY: this turn is killed at a hard 30-minute deadline and then asked for a closing report. Measured 2026-09-09: 41 % of member tokens were spent in turns that ran to that deadline, and the context inside such a turn climbs to around 200 k, so every further call in it costs more than the last. A turn that closes at a point YOU chose is cheaper and safer than one the clock cuts.\\nWHAT TO DO — both halves, and the second matters more:\\n  • If you are AT OR NEAR a stable point (a change that is complete and verifiable): reach it, save your work the way your role does — an implementer commits, a reviewer only reports — and write your report in your channel now. Your next turn resumes with a pack of what you left, so stopping here loses nothing.\\n  • If you are MID-CHANGE: FINISH THAT CHANGE FIRST. Do not abandon work, do not leave the tree half-edited, and never stop before something is verifiable. Carrying on is always allowed and costs you nothing.\\nDO NOT TAKE A SMALLER DECISION BECAUSE OF THIS LINE. Do not narrow what you were asked to do, do not skip a check, do not drop a verification, and never report work you have not run. Stopping short of the task is a worse outcome than a turn that ran long — all that is being asked is that you pick the stopping point rather than the deadline picking it for you."}}
JSON
