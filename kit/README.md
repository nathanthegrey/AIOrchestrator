# aiorch — the AI Orchestrator kit, as a Claude Code plugin

This folder is a Claude Code **plugin** and, at the same time, the **local marketplace** that serves
it. Both manifests live in `.claude-plugin/`, so nothing about the plugin is added outside `kit/`.

```
kit/
  .claude-plugin/plugin.json        name: aiorch, version: the one the host asserts
  .claude-plugin/marketplace.json   name: aiorch-local, serving ./
  skills/<role>/SKILL.md            the six role protocols
  skills/<role>/reference/*.md      the parts each protocol tells the session to read
  hooks/*.sh                        the enforcement, declared by the role that owns each one
  bin/channel-append.sh             the locked channel writer, on the session's PATH
  statusline/                       NOT a plugin component — see below
  install.sh / install.ps1          machine setup
```

## Install

```bash
bash kit/install.sh                       # macOS / Linux
powershell -File kit\install.ps1          # Windows
```

Either one registers this checkout as the `aiorch-local` marketplace and installs the plugin at user
scope. From then on **every session loads it with no flag**: `claude "/supervisor <id>"` is exactly
the spawn it has always been.

## Update

```bash
claude plugin update aiorch
```

**The bridge requires it.** At startup the host reads which version is installed and compares it
against the one it was built with. On a mismatch — or a missing, disabled or unreadable install — it
starts normally, keeps tailing and mirroring, tells the owner on Telegram, and **refuses to start any
session** until the versions agree. The message names what was expected, what was found, where the
found copy lives, and the command to run.

## The six roles

`/supervisor`, `/implementer`, `/reviewer`, `/solo`, `/general-supervisor`, `/communicator` — bare,
exactly as `Running/SessionRoles.cs` composes them. `/aiorch:supervisor` also works.

They are **skills**, not commands, and that is load-bearing: a plugin COMMAND resolves only
namespaced (`/probe` → "Unknown command"), while a plugin SKILL resolves both bare and namespaced
with `$ARGUMENTS` interpolated. Measured on CLI 2.1.263, headless, with a negative control. Every one
carries `disable-model-invocation: true`, so a role is entered because the app spawned it and never
because a model liked the description.

## Hooks

Each enforcement hook is declared in the frontmatter of the role it belongs to, so it registers for
that role and for nothing else:

| role | event | hook |
|---|---|---|
| supervisor | Stop | `supervisor-ledger-check.sh`, `run-to-the-end-check.sh` |
| supervisor | PreToolUse `*` | `supervisor-awaiting-answer-check.sh` |
| solo | Stop | `supervisor-ledger-check.sh`, `run-to-the-end-check.sh` |
| reviewer | PreToolUse `Bash` | `reviewer-readonly-check.sh` |

There is deliberately **no `hooks/hooks.json`**: every hook here opens by gating on `AIORCH_ROLE`, so
none of them is everyone's. A global declaration would re-register exactly what these frontmatter
blocks scope, and would put the 870-line reviewer guard in front of every Bash call of every
unrelated session on the machine — which is what the old `settings.json` wiring did.

## What is NOT in the plugin

`statusline/` — `statusLine` is a key in `~/.claude/settings.json` pointing at a script path, so
something still has to put a script at a path. The host installs it and wires it, as it always did.

`self-write-suppression-check.sh`, `hooks/hook-behaviour-check.sh`, `hooks/watcher-behaviour-check.sh`
— harnesses, not hooks. Each refuses to run when it cannot find its subject.

## Upgrading from the pre-plugin kit

Both installers, and the host at startup, **delete the role protocols and hooks the old builds copied
into `~/.claude`**. That is not tidying. Measured: when `~/.claude/commands/supervisor.md` and the
plugin's supervisor skill both answer to `/supervisor`, **the local file wins** — so a leftover would
be read instead of the plugin while `claude plugin list` reported the new version. Only the exact
filenames this project ever shipped are removed; anything else in those folders is left alone. A file
that cannot be deleted becomes the `Shadowed` verdict, and sessions stay refused.
