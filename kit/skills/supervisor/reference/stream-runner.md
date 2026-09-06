# `AIORCH_RUNNER=stream` — the bridge holds one process open and talks to it

Your boot command printed `AIORCH_RUNNER`. This file applies when it says `stream`. You have no
terminal and no window: the bridge started ONE `claude` process for you and wakes you by writing on
its stdin. Everything below replaces the corresponding rule in the main protocol.

## THE SHAPE OF YOUR FINAL MESSAGE — read this before anything else

The bridge takes your final message and writes it into the channel AS THE ENTRY. So:

```
supervisor online — probe — /tmp/probe-repo        <- line 1 IS the subject
                                                   <- blank
Ready. Text me what you need.                      <- the body
```

**NO preamble, ever.** Not "Responding to the owner's greeting:", not "Here is my update:", not a
heading. Whatever occupies line 1 BECOMES the subject the owner reads in their topic list — measured
2026-09-06, on the first live stream turn against this file: the session wrote a lead-in sentence
above its own subject, and that sentence is what would have been filed as the entry's subject.

The same applies to anything a machine prints before you: a `SessionStart` banner lands in your
first message, and a session that repeats it makes the banner its subject.

## 1. The environment is resolved by a command, first, always

This is not different for stream, it is the same rule the main protocol opens with, and it is
repeated here because it is the one whose absence is silent:

```bash
echo "ROOT=${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"; env | grep '^AIORCH_' | sort
```

The Read tool does not expand variables. A path typed from memory is the DEFAULT root, which under a
bridge started with `--root` does not exist. In the stage-1b live run the first version of a test
role protocol omitted this line and a member lost its first brief.

## 2. NO Monitor, no watcher, no `sleep`

Do not arm anything and do not poll. You are woken by the bridge writing to your stdin. The whole
"definition of done" of `reference/watcher.md` does not apply in this mode — that file is for a
session that owns its own process.

## 3. Your FINAL MESSAGE is your entry — and you never write your channel yourself

First line is the SUBJECT, then a blank line, then the body. The bridge writes the entry from that
message.

**Do NOT also append it with `channel-append.sh`.** In the stage-1b live run a member that had not
been told this wrote its report TWICE — once with Bash, once from the bridge.

**The first line is the subject and nothing else.** Any `SessionStart` banner the machine prints
lands in your first message, and a session that repeats it makes the banner the subject of its own
entry.

## 4. A question ENDS the turn, exactly as an answer does

Ask, and stop. There is no tool-denial hook holding you: the turn simply finishes, and the bridge
does not send you another line until the owner has answered. "One question closes the turn" is a
property of the transport here, not a rule you have to remember to obey.

## 5. THE LIMIT OF THIS MODE, said plainly: only the OWNER channel wakes you

A member writing in its spoke does NOT start a turn for you. Until a later stage brings a
multi-source trigger, **read your members' spokes at the end of EVERY turn** and treat whatever you
find there as incoming traffic:

```bash
cat "${AIORCH_SUPERVISION_ROOT}/$ARGUMENTS"/imp-*/channel.md "${AIORCH_SUPERVISION_ROOT}/$ARGUMENTS"/rev-*/channel.md 2>/dev/null
```

The app says so itself, at every registration, in its log: *"'sup' is stream-run and it is woken by
the OWNER channel only…"*. A report you do not read at the end of your turn waits until the owner
happens to write to you.
