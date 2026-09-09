# Token work — what this session changed, what it will change next

**For the other agents on this app.** Read this before touching anything it names: on 2026-09-09 two
lines of work ran in parallel on the same files and the same afternoon (speed: `stage/7*`; tokens:
`stage/4*`), and one feature was **built twice** — my `stage/4c` and its `stage/7f` are the same
closing turn. This file exists so that does not happen again.

**Written 2026-09-09 22:00 CEST.** Copies read: branch source `ours/integration` (Mac checkout), the
VPS clone and the running binary at `/opt/aiorchestrator`, the installed kit under
`~/.claude/plugins/cache/aiorch-local`. The plan and every measurement live in
`2026-09-08-token-efficiency-design.md` (§5b–§5g are today's status, gates and schedule).

---

## 1. Mine, merged and live

| branch | commit / merge | what it does | files it owns |
|---|---|---|---|
| `stage/4b-state-pack` | `bd6f942..0b7dec5`, merged `c9a36a8`, deployed 11:00 | **The state pack.** On a fresh turn the bridge writes `<orch>/<member>/pack.md` — pending entries, the brief (found across live channel **and** archive), the member's last own report, its `PLAN.md` lines, `git status/log -3`; for supervisor/solo also the whole `PLAN.md` and the last 5 owner entries. **Nothing on stdin** (measured: the CLI appends stdin into the slash command's `$ARGUMENTS`). Every print role's skill gained one boot sentence: "if `pack.md` exists, read it first". `executed_turns[]` gained `session_id`. | `Running/StatePack/*` (5 types), `Running/PrintTurnPrompt_Builder.cs` (two public renderers + the dead `Build_FreshSession`), `Running/TurnExecutor/PrintTurnExecutorModel.cs`, `Running/ExecutedTurn/*`, `Running/PrintSessionState/PrintSessionState_Store.cs`, `kit/skills/*/SKILL.md` (one sentence each), `tools/token-gate/*`, the live CLI contract test |
| `stage/4e-host-shutdown-timeout` | `ec631d2`, merged `abf04bd`, deployed 21:36 | **The drain is no longer cut at 30 s.** `HostOptions.ShutdownTimeout` was never set, so every stop killed in-flight turns 30 s after announcing a 31-minute drain (measured 17:24/17:25/17:29: 5 fresh sessions, **15.2 M tokens**, one turn restarted 4×). `ShutdownGrace_Rule` is now the single source for drain < engine grace < host timeout (39 min). | `Running/ShutdownGrace_Rule.cs` (new), `AIOrchestrator.Daemon/Program.cs`, `AIOrchestrator.Daemon/BridgeHost_Service.cs`, `deploy/systemd/aiorchestrator.service` (2400 s) |
| `stage/4f-general-model-refresh` | `c51068b`, merged `f5d6b76`, deploying 21:47 | **The general supervisor's model is re-read.** It was frozen at first registration: the watchdog exempts a print-run general (no process to check), so `Spawn_GeneralSupervisor` never runs twice and `BridgeDrivenRunnerModel`'s refresh path is unreachable — `config.json` said sonnet, the session ran haiku since the day before. The dispatcher reconciles it at the start of a turn. Members untouched (their model is a per-member choice). | `Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` (`Refresh_GeneralModel_IfChanged`), dispatcher tests, the harness's `Set_ConfigValue` |

**Config, not code (already applied):** VPS `config.json` — `implementer`/`reviewer` `resume: fresh`
(round 1, 8 Sep 20:34Z), `generalSupervisorModel: sonnet`, `communicatorModel: sonnet`; supervisor and
the implementer/reviewer fallback stay `opus`. Backups: `config.json.pre-models-*`.
**Recipe, not code:** `00_Infrastructure/aiorch-vps/03-aiorch.sh` step 7 wrote four `// "haiku"`
defaults that beat the app's own on a fresh box — now `opus/opus/sonnet/sonnet`, Mac and VPS copies
byte-identical. **Root, not code:** the VPS drop-in `drain.conf` 2100 → **2400 s** (backup kept).

## 2. Mine, parked — do not merge

`stage/4c-closing-turn` (`b87e87d`, pushed, **not merged**): the same closing turn as `stage/7f`, built
in parallel before either of us knew. `7f` won on merit (bounded budget, timeout derived from the turn
timeout, the attempt not spent, every fallback logged) and is live. `4c` is kept only as a record;
**if you touch the closing turn, work from `7f`'s `Running/ClosingTurn/*`, never from `4c`.**

## 3. What I intend to build next — claim before you start

Schedule and rationale in the spec's §5g; ownership below so nobody builds it twice. **If you want any
of these, say so in the owner's channel first and I will drop it.**

| when | mine | what I will touch |
|---|---|---|
| 10 Sep morning | **C9 — soft boundary inside a turn** (advice near call ~40: "reach a stable point, commit, report"; the deadline stays the hard net). 41 % of tonight's member tokens are in turns that ran to the deadline. | `kit/hooks/` (a new PreToolUse advisory), `kit/skills/implementer|reviewer/SKILL.md`, possibly `Running/` for the sensor if the hook cannot read its own transcript |
| 10 Sep after Gate 3 | **C4 — wake-up digest for the supervisor** (owner immediate; member entries at stage end or every 5 min; mechanical acknowledgements handled by the app) | `Running/PrintTurnDispatcher/*`, `Running/PendingTraffic/*`, `Running/TurnSource/*` |
| 11 Sep morning | **C6 — `MODEL:` read from the brief**, and the reviewer's model key split from the implementer's (they share `implementerModel` today) | `Configuration/OrchestratorConfig*`, `Launching/OrchestrationLauncher*`, `Running/PrintTurnDispatcher/*` |
| 11 Sep afternoon | **Supervisor fresh with the pack** (the pack for it is already built and inert) | VPS `config.json` + verification; no new code expected |
| later | **C3** app bookkeeping out of the channel · **C7** the token counter inside the app · **`STATE:`** only if a gate shows rework | `Channels/*`, `Usage/*`, `Running/StatePack/*` |

## 4. Where we will collide, and the rule I am following

- **`PrintTurnDispatcherModel.cs`** is the shared road: 4c/7f, 7b, 7e and my 4f all landed in it today.
  Rebase onto `origin/ours/integration` **before** writing, and re-run a trial merge before you commit.
- **`kit/skills/*/SKILL.md`**: one sentence per skill was added for the pack. The kit rule holds —
  reorganise, never rewrite a rule; a test counts normative sentences.
- **The deploy path**: use the recipe `~/aiorch-vps/03-aiorch.sh`. It reinstalls the plugin when the
  installed commit differs; the manual `git pull` + `cp -a` route in the speed report does **not**, so
  sessions keep reading stale skills. One procedure, not two.
- **Restarting the VPS is no longer free but it is now safe**: the running binary drains in-flight
  turns (up to 36 min) instead of killing them. Do not add a shorter grace anywhere.
- **Two corrections to `2026-09-09-report-lavoro-e-runbook.md`**, both verified: its open item #1 blamed
  `TimeoutStopSec` = 90 s — that is the **`--user`** unit; the system unit had 35 min and the real cut
  was the .NET host (§1, `stage/4e`). And `--max-budget-usd` not capping spend is real but harmless for
  the closing turn, whose real bound is the derived timeout.

## 5. What is measured, and how to measure it yourself

`tools/token-gate/` (read-only, metadata only — `usage`, timestamps, model, tool names; never message
text). `gate_turns.py` compares calls per turn / first-call context / tokens per turn across the
experiment windows; `gate_delivery.py` splits the bill into members / supervisors / sub-agents and
reports production vs orientation calls per closed ledger line. Run them on the VPS from `/tmp`.
Headline so far (spec §5f): **tokens per call for members 345 k → ~100 k and stable**; per delivery
4.7× on short tasks; the remaining cost has moved **inside** long turns, which is what C9 attacks.

*Nothing here is certified by its author: the suite numbers are `dotnet test AIOrchestratorCoreLib.Tests`
(2 756 passed / 9 skipped / 0 red on `f5d6b76`), the VPS facts are reads of the running box, and the
before/after that proves the plan is the gate in §5f–§5g.*
