# Plan backend — the seam between PLAN.md and a planning system elsewhere

**PLAN.md stays the execution ledger.** Sections, markers, the turn-end hook, the progress bar and the
role commands are unchanged. A backend synchronises **one section — `## OWNER REQUESTS`** — and nothing
else. `## PARKED` is never synchronised: a discovery nobody asked for is local (CLAUDE.md decision 22).

## The contract — `Planning/PlanBackend/IPlanBackend.cs`

| Call | Direction | Meaning |
|---|---|---|
| `List_ApprovedRequests(orchId)` | in | What the owner approved upstream and this plan does not have yet. |
| `Acknowledge_Request(orchId, requestId, ledgerRowRef)` | out | It is now a ledger line, at that text. |
| `Report_RowClosed(orchId, ledgerRowRef, evidence)` | out | That line reached `[x]`. A **report**, not a closure. |
| `Report_OrchestrationClosed(orchId, summary)` | out | The orchestration is over; one line of what its ledger said. |

`PlanMdBackend` is the default and does nothing — configure no backend and the app behaves exactly as
it did before this existed (`PlanMdBackendIsTodayTests`).

## What the app does with it

Once a minute (`BridgeEngineModel.Sync_PlanBackends`, above the DND gate — it sends the owner nothing),
per orchestration, `PlanBackend_Step.Sync`:

1. **Ingests.** Each approved request gets a row in the `## OWNER REQUESTS` table (their own words,
   appended, never renumbered) **and** a `- [ ]` line at the end of the ledger. Both: the table records
   the ask, the ledger line is the only thing that can later close — a prose "status" cell is not a
   signal anything can read — and it traces to an owner request by construction (decision 22).
2. **Reports.** When that line's marker turns `x`, the backend is told once, with the conversation
   entry live at the time as evidence.
3. **Remembers**, in `.plan-backend.json` beside PLAN.md. The app restarts daily; without it every
   launch would re-ingest and re-report.

PLAN.md is written first (`Atomic_FileWriter`), the record of it last — so an interruption costs a
**repeated call**, never a duplicated row in the owner's plan. Implementations must therefore be
idempotent per `requestId` / row reference: at-least-once is the wire, exactly-once is both sides.

## Configuring one

```json
"planBackend": { "kind": "external", "assembly": "/opt/adapters/Adapter.dll", "type": "Adapter.PlanBackend" }
```

`kind: "plan-md"`, or no key, is the default. External types load by reflection and need a public
parameterless constructor. **A failed load falls back to PLAN.md alone and logs why** — never silently.
The key is hand-edited; the app never writes it, and `Save` now keeps every key it does not own.

## Writing an adapter

One .NET class implementing `IPlanBackend`, **outside this repository**, reading its own configuration
from its own place. This repo holds no client, no URL, no upstream vocabulary.

- **Read readiness with every blocker, never a lone "ready" flag.** A row can be flagged ready and still
  be held by an unmet condition; handed over early it becomes a line nobody can finish.
- **Send only what the owner approved** — ideas, questions and backlog belong upstream.
- **On close, submit for verification; do not close the row.** "Merged is not verified" holds across the
  boundary: report the evidence, leave the decision to whoever can make it.
- **Be idempotent, and be quick** — every call runs on the bridge's tick.

## Known limits, stated

- The link between a request and its line is the line's **text**. Rewrite or split that line — the normal
  way ledgers evolve — and the link is lost, so the closure is never reported.
- Evidence is an **observation** (`PlanRowEvidence`): a marker changed and this is what the channel said.
  It is not proof the work happened.
- A config built in code rather than loaded carries `PlanBackend == null` (the Settings window has no
  field for it); nothing is lost on disk, because `Save` never writes the key.
