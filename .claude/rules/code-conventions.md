---
paths:
  - "AIOrchestratorCoreLib/**"
  - "AIOrchestrator.Daemon/**"
  - "AIOrchestrator/**"
  - "integrations/**"
  - "tools/**"
---
# C# conventions — inferred from the code, not from a document

The `CODING_PATTERNS.md` that `CLAUDE.md` cites is not in this repo. These are the conventions the CoreLib actually follows (measured 2026-09-06 on `Limits/`, `Channels/`, `Planning/`, `Spawning/`); `Limits/` is the reference module — 7 files, zero exceptions. When unsure, imitate `Limits/`.

- **The triple.** One folder per subject: `IXxx` (interface) + `internal sealed class XxxModel : IXxx` + `static Xxx_Factory` with `Create_*` methods (`Create_ForOwner`, `Create_Print` — never a bare `Create` when more than one shape exists). Only the factory calls `new XxxModel(...)`. One interface may back several models; the factory chooses.
- **Naming.** `Xxx_Yyy.cs` with the underscore only for static utilities whose second word is a role (`_Parser`, `_Builder`, `_Factory`, `_Reader`, `_Store`, `_Gate`, `_Tracker`, `_Translator`, `_Ladder`, `_Order`). Plain data or behaviour types have no underscore (`ChannelHistory`, `TurnOutcomes`). Methods: `Verb_Object[_Modifier]`; anything that may not resolve ends in `_OrNull` — never a bare nullable return.
- **Immutability.** Get-only properties set from a primary constructor. No `record` types. Ad-hoc multi-value returns are value tuples, not new model types.
- **Errors, by layer.** Parsers of external or untrusted data swallow and return null/empty with a comment saying why that is the safe direction; invariant violations throw, naming the bad value.
- **Logging.** Pure static `Describe_*` helpers build the human-readable line; `IOrchestrationLog` is injected only into stateful, long-lived components.
- **XML docs argue the why**, with dated incidents ("Observed 2026-08-11: …"). A comment that only restates the code is noise.
- **Tests.** xUnit `[Fact]`; test folders mirror production namespaces 1:1; class `<Subject>Tests` with the underscore stripped (`LimitData_Parser` → `LimitDataParserTests`); methods `Verb_Scenario_Outcome`. Stubs, not mocks (no Moq/NSubstitute): hand-rolled harnesses and the fake CLI in `tools/claude-contract/FakeClaude`. Live tests against the real `claude` use `[LiveFact]` (self-skips without `CLAUDE_CONTRACT_LIVE=1`), never `[Trait]`.
- **The suite is the capital**: on macOS 6 tests are red by construction (Windows file-lock semantics) plus one intermittent (`ChannelLock_Diagnostics.Set_Sink`); compare the *set of names* before and after, never the count. Zero new reds.
- **`BridgeEngineModel.cs` is ~12 000 lines.** Touch the minimum, name every line you change in the report, never reorganise it — splitting it is its own stage.
- **Three OS are first-class.** No `#if` that excludes an OS from the daemon; `Path.Combine`; `ProcessStartInfo` portable; no bash required from C#.
- **Drift to fix when you pass by, not to imitate**: `Running/TurnOutcomes.cs` and `SessionRoles.cs` lack the role suffix; `Bridge/` tests use scenario-sentence class names.
