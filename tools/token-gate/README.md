# token-gate — read-only measurement of the fresh-turn experiment

Two Python 3 scripts (stdlib only) that run ON THE VPS against `~/.claude/supervision` and
`~/.claude/projects`. They read metadata only — `usage`, timestamps, model, tool names and
file-path arguments of tool calls — never message text. Nothing is written outside `/tmp`.

Both compare a BASELINE (turns before the cut) with ROUND 1 (turns after it); the cut is the
constant `CUT` at the top of each file (`2026-09-08T20:34:00Z` = the round-1 deploy).

| script | answers | source |
|---|---|---|
| `gate_turns.py` | per role: calls per turn, first-call context, tokens per turn, new tokens, output, duration, errors; one line per fresh turn; tool mix of the fresh sessions and how often they still open channel/archive/PLAN files | `turns.jsonl` (`usage.iterations[0]` = the turn's first call), transcripts (tool names + path args) |
| `gate_delivery.py` | tokens since the cut split members / supervisors / sub-agents; production calls (Edit/Write + commits) vs orientation calls per closed ledger line; review rounds per day; merges per day; the reviewer entries for the owner's blind read | `turns.jsonl`, transcripts (dedupe by `requestId`), `PLAN.md` `- [x]` counts, channel HEADERS only, `git log` of the target repo |

Run: `scp tools/token-gate/*.py orch@<vps>:/tmp/ && ssh orch@<vps> 'python3 /tmp/gate_turns.py; python3 /tmp/gate_delivery.py'`.

Known limits (measured 2026-09-09): `num_turns` in the CLI result over-counts API calls by ~1.5–2×;
`result.usage` excludes sub-agents and resets at background-task notifications, so the transcript
figures are the ones to trust for totals; ledger lines are uneven, so tokens-per-line needs ≥ 10
lines per arm before it means anything. The gate's criteria live in
`docs/superpowers/specs/2026-09-08-token-efficiency-design.md` §6 (revision 2).
