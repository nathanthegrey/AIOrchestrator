# The brake that watches work, not the clock — and the advisory that does not arrive

**Date:** 2026-09-11 · **Status:** PROPOSED, nothing built · **Owner:** Nathan
**Provenance:** measured on the VPS on 2026-09-11 while verifying the night's consumption, as a
follow-on to `2026-09-10-independent-review/REPORT.md`. Not part of the token plan
(`2026-09-08-token-efficiency-design.md`) — it is a **consequence** of it that nobody priced.

**Copies read.** Code: branch source at `ours/integration` HEAD `c14945d`, which is also what the
VPS serves (`/home/orch/AIOrchestrator`) and what the running daemon was published from
(`/opt/aiorchestrator`, DLL built 2026-09-11 09:33, service active since 09:33:17). Live data: VPS
`orch@159.195.254.120`, **read-only, metadata only** — `usage`, timestamps, tool names, field
presence, `orchestrator.log.jsonl`, `turns.jsonl`. Never the text of a message.

Every figure is tagged `[measured]` or `[estimate]`.

---

## 1. What is happening

The state pack made a member's turn roughly eight times longer — from about two model calls to a
median of sixteen [measured]. The turn deadline did not move with it, and a third of member turns
now die on it.

| implementer turns ending in error | share |
|---|---|
| 2026-09-07 (before the pack) | 18.6 % [measured] |
| 2026-09-08 (before the pack) | 19.0 % [measured] |
| 2026-09-09 (the pack lands) | 30.7 % [measured] |
| 2026-09-10 | 35.1 % [measured] |
| the night of 09-10 → 09-11 | 33.3 % [measured] |

The composition changed, not just the rate. Before, most failures were usage-limit refusals; after,
most are the deadline:

| failure | before the cut | after the cut |
|---|---|---|
| HTTP 429, usage limit | 26 | **18** [measured] |
| no status, no result, `exit_code = -1` | 12 | **63** [measured] |

**[measured]** `journalctl -u aiorchestrator --since 2026-09-09` names them: 57 lines
`Turn <orch>/<member>/<n> was killed at the deadline after 30.0 min`, and **57** matching
`Closing turn … ended — success`.

Two things follow immediately. **The brake fails safely** — every killed turn got its salvage
report, 57 out of 57. And **`duration_ms` is absent on these records, not zero**: a first reading of
this data turned the missing field into `0.0` and concluded the turns never started. They run the
full thirty minutes. The record is kept here because the same mistake is available to the next
reader.

---

## 2. The brake is measuring the wrong thing

**The question: were they stuck, or working?** Model calls in the thirty minutes before each kill,
in five-minute blocks, counting the turn's own calls **and its sub-agents'** [measured, 57 kills]:

| window before the kill | model calls |
|---|---|
| −30 to −25 min | 2,705 |
| −25 to −20 min | 1,035 |
| −20 to −15 min | 789 |
| −15 to −10 min | 998 |
| −10 to −5 min | 974 |
| **−5 to 0 min** | **911** |

A stuck turn decays toward zero before the guillotine. This does not decay. **The 57 turns were
working at the moment they were killed.** (The first block is inflated: it catches the turn's
opening burst, and the aggregation is per member, so a neighbouring turn's calls can land in it.
The claim rests on the absence of decay, which no aggregation artefact produces.)

So: in three days the deadline has cut productive work 57 times and caught a genuine hang **zero**
times. There is no case in this data where it did the job it exists for.

**And the threshold is now mis-set against the work.** Successful turns: median 1.7 min, p90 18
min, max 28.5 min [measured]. A distribution whose maximum sits 1.5 minutes under the ceiling is a
censored distribution, not a comfortable one.

**Raising the ceiling is not free, and the code already says why.**
`ShutdownGrace_Rule.DEFAULT_TURN_TIMEOUT` is 30 minutes, `printRunner.turnTimeoutMinutes` overrides
it, and `Describe_Mismatch_OrNull` refuses silence: the host's `TimeoutStopSec` must cover
`turn + closing turn + margins`, or a service stop during a long turn cuts the drain. Raising the
number therefore touches the systemd unit too — and only moves the wall. The work grew once; it will
grow again.

---

## 3. The other half: the gentle brake does not arrive

The system already has a work-aware brake. `kit/hooks/soft-boundary-check.sh` counts a turn's tool
calls and, past `SOFT_BOUNDARY_CALLS` (default **35**, `:140`), injects one advisory through
`hookSpecificOutput.additionalContext` — reach a stable point, save, report. It advises, never
blocks, which is decision 21 correctly applied.

**[measured]** The turns that later die on the deadline make a **median of 74 tool calls** in their
final thirty minutes (min 20, max 132) — twice the threshold. And **only 25 of the 57 received the
advisory at all.**

That is not a threshold to tune. Fewer than half the turns that most need the gentle brake never
hear it, while the blunt one kills them. The two brakes are not connected.

**Why it misses is not diagnosed.** Candidates, none verified: the counter directory under
`${TMPDIR:-/tmp}` differing inside the session's systemd scope; the once-per-turn latch keyed on
`prompt_id` with a `session_id` fallback; the daily sweep of stale turn directories; sub-agent calls
counting toward the total while only a parent call can carry the delivery. **Diagnose before
changing the number** — a threshold moved under an undiagnosed delivery fault buys nothing.

---

## 4. The design

> Not "has it been quiet for N minutes", but **"is anything alive under this turn, and is it waiting
> for something the system itself told it to wait for"**.

### 4.1 Scope: members only, never supervisors

A supervisor is idle by design. The longest silence inside one session, own calls plus sub-agents'
[measured, all sessions since 09-07]:

| role | sessions | median | p90 | p95 | p99 | max |
|---|---|---|---|---|---|---|
| implementer | 232 | 1.4 min | 3.4 | **10.0** | 419.6 | 419.8 |
| reviewer | 128 | 0.5 min | 2.1 | 3.2 | 227.4 | 418.6 |
| **supervisor** | 7 | **412.5 min** | 909.3 | 909.3 | 909.3 | **909.3** |

A naive inactivity brake kills every supervisor, every night. All 57 kills to date were members and
none was a supervisor [measured], so the brake is already members-only in practice — this makes it
explicit rather than lucky.

### 4.2 Threshold: the members' own numbers

**95 % of member turns never go quiet for more than 10 minutes** [measured]. A silence threshold in
the 15-minute region leaves essentially every healthy turn alone, against a clock that today kills a
third of them. 30 of 367 sessions have a silence of 5 minutes or more (8.2 %), 24 of them members.

The absolute ceiling stays as a backstop, well above today's 30 minutes, for a turn that emits calls
forever without converging.

### 4.3 Liveness: what counts as "alive"

In order of strength:

1. **A child process of the turn is running.** A build or a test suite emits no model call for
   minutes while being entirely healthy. The p95 of 10 minutes for implementers is consistent with
   exactly this [estimate — the gaps were not attributed to a cause].
2. **A model call by the turn OR by one of its sub-agents.** The parent blocks while a sub-agent
   works; issue #74318 measures a median parent-blocked gap of about nine minutes on comparable
   traffic. Counting only the parent would read a working turn as dead. Every table in this spec
   already counts both, and must.

### 4.4 The three legitimate waits — the app already knows all three

The owner's objection, in his words: *how do you tell "doing nothing" from "waiting for me while I
sleep"?* Measured answer: **members do not wait for the owner — supervisors do**, and the system
already represents every one of these states.

| the turn is waiting for | what the app already holds |
|---|---|
| the owner to answer a question | the `.awaiting-answer` flag, app-managed, self-expiring, already enforced by `supervisor-awaiting-answer-check.sh` |
| a usage limit to reset | the retry appointment (`RetryNotBeforeUtc`), already a dispatcher gate |
| its own long tool call | the child process, observable |

The clock **pauses** on the first two and does not run at all while the third holds. Nothing new has
to be invented or inferred; three facts the system already maintains have to be consulted by one
more reader.

This also explains the p99 of about seven hours in the member rows above: those are usage-limit
waits, not stalls, and a brake that does not consult the appointment would kill exactly the turns
that were correctly parked.

---

## 5. Order of work, and why this order

1. **Diagnose the advisory's 25-of-57 delivery.** Read-only; no production change. It comes first
   because if the gentle brake reaches every long turn, the hard one stops firing on its own and §2
   becomes a much smaller change.
2. **Replace elapsed-time with liveness, members only, with the three pauses.**
3. **Only then** consider the ceiling, which by that point is a backstop and not a policy.

---

## 6. Done when

- No turn is killed while a call of its own or of a sub-agent landed within the silence threshold —
  measured on the same window shape as §2, not asserted.
- A deliberately hung member turn is still killed. **A brake that has never been shown to catch the
  thing it exists for is the situation this spec is written about**; the new one must be shown, with
  a hang staged on purpose.
- No supervisor is ever killed by it.
- A turn parked on a usage-limit appointment, or on an unanswered owner question, survives a window
  longer than the threshold.
- Implementer turns ending in error return toward the pre-pack 19 % [measured baseline], with the
  429 share unchanged — the deadline share is what must fall.

---

## 7. What NOT to do

- **Raise the 30 minutes and stop there.** It moves the wall, touches the systemd unit
  (`Describe_Mismatch_OrNull` will say so), and the work will grow into it again.
- **Add a brake to sub-agents.** They are the largest bucket — 3,913 calls on the night against
  2,894 for implementers [measured] — and that argues for a return contract in the role prompts,
  not a third brake. One mis-set brake is the current problem; two would be worse. This retracts a
  proposal made in the independent review's §6 in that form.
- **Move the advisory threshold before §5.1.** Tuning a number whose delivery is broken changes
  nothing and hides the fault.

---

## 8. What is NOT measured, explicitly

- **Why the advisory misses.** §3 lists candidates; none is verified. This is the first task, not a
  finding.
- **What the 10-minute silences are.** The build/test hypothesis in §4.3 is an estimate; the gaps
  were never attributed to a cause.
- **Whether killed turns lose work.** The closing turn succeeds 57 times out of 57 [measured], so a
  report is always produced. Whether that report carries what the killed turn had learned is
  unexamined, and it is the difference between a nuisance and a real loss.
- **The cost of the kills.** 57 turns at no less than the 253 k median context of a successful turn
  is about 14 M tokens over three days [estimate, and a floor — a killed turn ran far longer than
  the median], plus 57 closing turns. It was not measured directly because the killed turns'
  records carry no usage at all.
- **Nobody has reviewed this spec.** On this line of work, six consecutive changes were found
  defective by review after their author had read the diff and seen green. Including this author's.
