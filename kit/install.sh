#!/usr/bin/env bash
# AI Orchestrator — machine setup for macOS and Linux, the twin of install.ps1 (which stays the
# Windows path, unchanged). Does the SAME FOUR THINGS: the supervision home and its folders, the
# role commands (+ the channel append helper), the status line, and the statusLine entry in
# ~/.claude/settings.json — then the optional Telegram configuration, kept unless you type new values.
# Safe to re-run: every copy is content-compared (md5) and only changed files are rewritten.
# Run from the repo root:  bash kit/install.sh
#
# This is BOOTSTRAP ONLY. The running host (WPF app or daemon) re-installs the kit from its own
# output folder at every start (decision 17) — this script is for a machine that has not built yet.
# Dependencies: bash, jq (settings.json is merged, never rewritten blind), md5sum or md5.

set -euo pipefail

kit_folder="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
claude_folder="$HOME/.claude"
commands_folder="$claude_folder/commands"
supervision_folder="$claude_folder/supervision"
config_file="$supervision_folder/config.json"
secrets_file="$supervision_folder/secrets.json"
settings_file="$claude_folder/settings.json"
status_line_target="$supervision_folder/statusline.sh"

say()  { printf '%s\n' "$*"; }
ok()   { printf '\033[32m%s\033[0m\n' "$*"; }
warn() { printf '\033[33m%s\033[0m\n' "$*"; }
head() { printf '\033[36m%s\033[0m\n' "$*"; }

# md5sum (GNU, Linux) or md5 -q (BSD, macOS): the same digest, two spellings.
file_md5() {
    if command -v md5sum >/dev/null 2>&1; then md5sum "$1" | cut -d' ' -f1
    elif command -v md5 >/dev/null 2>&1; then md5 -q "$1"
    else cat "$1" | cksum | cut -d' ' -f1
    fi
}

# Copies only when the content differs; prints 1 when it copied, 0 when it was already current.
install_file() {
    local source="$1" target="$2"
    if [ -f "$target" ] && [ "$(file_md5 "$source")" = "$(file_md5 "$target")" ]; then
        printf '0'; return 0
    fi
    cp "$source" "$target"
    printf '1'
}

say ''
head '=== AI Orchestrator setup ==='

# --- 0. Prerequisites -------------------------------------------------------------------------
if ! command -v jq >/dev/null 2>&1; then
    warn 'ERROR: jq is required (it merges ~/.claude/settings.json and writes config.json). Install it: brew install jq / apt install jq'
    exit 1
fi
if ! command -v claude >/dev/null 2>&1; then
    warn 'WARNING: the "claude" CLI was not found on PATH. Install Claude Code first.'
fi

# --- 1. Folders + role commands + status line -------------------------------------------------
mkdir -p "$commands_folder" "$supervision_folder" "$supervision_folder/.requests"

# Every .md in kit/commands is a role command — copy them all, never a hand-written list, so this
# bootstrap path cannot disagree with the host's own installer (KitAssets_Installer), which globs.
installed=0; total=0; names=""
for command_file in "$kit_folder"/commands/*.md; do
    [ -f "$command_file" ] || continue
    total=$((total + 1))
    installed=$((installed + $(install_file "$command_file" "$commands_folder/$(basename "$command_file")")))
    names="$names /$(basename "$command_file" .md)"
done
ok "Role commands: $total present, $installed updated:${names}"

# The append helper ships INTO the commands folder, because that is the path every role command
# tells a session to run. Not optional: all roles mandate it for every channel write.
append_helper="$kit_folder/channel-append.sh"
if [ ! -f "$append_helper" ]; then
    warn "kit/channel-append.sh is missing from the kit at '$append_helper'. Every role command mandates it for channel writes; installing the instructions without the script would leave every session pointing at a dead path."
    exit 1
fi
if [ "$(install_file "$append_helper" "$commands_folder/channel-append.sh")" = 1 ]; then
    ok 'Installed the channel append helper (channel-append.sh).'
else
    ok 'Channel append helper already current.'
fi
chmod +x "$commands_folder/channel-append.sh"

if [ "$(install_file "$kit_folder/statusline/statusline.sh" "$status_line_target")" = 1 ]; then
    ok 'Installed status line script.'
else
    ok 'Status line script already current.'
fi
chmod +x "$status_line_target"

# --- 2. Status line in settings.json (backup first) -------------------------------------------
# Same command shape StatusLineSettings_Wirer.Build_Command writes for a .sh script.
status_line_command="bash \"$status_line_target\""
if [ -f "$settings_file" ]; then
    if ! jq -e . "$settings_file" >/dev/null 2>&1; then
        # Unlike install.ps1, which starts over from an empty object, an unreadable settings file
        # is refused here: it is the user's to fix, and overwriting it would destroy their hooks.
        warn "ERROR: $settings_file is not valid JSON — refusing to rewrite it. Fix it and re-run."
        exit 1
    fi
    cp "$settings_file" "$settings_file.aiorch-backup"
    settings_json="$(cat "$settings_file")"
else
    settings_json='{}'
fi
printf '%s' "$settings_json" \
    | jq --arg command "$status_line_command" '.statusLine = {type: "command", command: $command}' \
    > "$settings_file.tmp" && mv "$settings_file.tmp" "$settings_file"
ok 'Configured Claude Code status line (previous settings backed up).'

# --- 3. Telegram configuration (skippable) -----------------------------------------------------
say ''
head 'Telegram setup (press Enter on any prompt to keep the current value / skip):'
say '  One-time manual steps, if not done yet:'
say '   1. Create a bot with @BotFather -> /newbot -> copy the token.'
say '   2. Create a NEW GROUP in Telegram, then in group settings enable "Topics".'
say '   3. Add your bot to the group as ADMIN (needs "Manage topics").'
say '   4. Group chat id: add @getidsbot to the group, it prints the -100... id (then remove it).'
say '   5. Your user id: message @userinfobot in a private chat.'
say ''

existing_config='{}'
[ -f "$config_file" ] && jq -e . "$config_file" >/dev/null 2>&1 && existing_config="$(cat "$config_file")"
existing_secrets='{}'
[ -f "$secrets_file" ] && jq -e . "$secrets_file" >/dev/null 2>&1 && existing_secrets="$(cat "$secrets_file")"

current_token="$(printf '%s' "$existing_secrets" | jq -r '.telegramBotToken // empty')"
current_chat_id="$(printf '%s' "$existing_config" | jq -r '.telegramSupergroupChatId // empty')"
current_owner_id="$(printf '%s' "$existing_config" | jq -r '.telegramOwnerUserId // empty')"

# Prompts only on a terminal; a piped or scripted run keeps every current value.
if [ -t 0 ]; then
    read -r -p "Bot token [$([ -n "$current_token" ] && echo kept || echo 'not set')]: " token_input || token_input=""
    read -r -p "Supergroup chat id (-100...) [${current_chat_id:-not set}]: " chat_id_input || chat_id_input=""
    read -r -p "Your Telegram user id [${current_owner_id:-not set}]: " owner_id_input || owner_id_input=""
    [ -n "$token_input" ] && current_token="$token_input"
    [ -n "$chat_id_input" ] && current_chat_id="$chat_id_input"
    [ -n "$owner_id_input" ] && current_owner_id="$owner_id_input"
fi

# BOTH IDS ARE CHECKED BEFORE ANYTHING IS WRITTEN, exactly where install.ps1 checks them with
# [long]. They reach jq as `tonumber`, and jq exiting on a bad literal used to leave config.json
# TRUNCATED TO ZERO BYTES with no backup — repos, models and every Telegram setting gone, and the
# next start silently in file-only mode. A stray space or a `+` typed at the prompt was enough.
check_whole_number() {
    # $1 = human name, $2 = value. An empty value means "not set" and is fine; anything else must be
    # a whole number, optionally negative (supergroup ids are -100...).
    [ -n "$2" ] || return 0

    case "$2" in
        -*) [ -n "${2#-}" ] && [ -z "$(printf '%s' "${2#-}" | tr -d '0-9')" ] && return 0 ;;
        *)  [ -z "$(printf '%s' "$2" | tr -d '0-9')" ] && return 0 ;;
    esac

    warn "ERROR: the Telegram $1 must be a whole number, got '$2'. Nothing was written."
    exit 1
}

check_whole_number 'supergroup chat id' "$current_chat_id"
check_whole_number 'user id' "$current_owner_id"

# --- 4. Write config.json (preserving repos) + secrets.json ------------------------------------
repos="$(printf '%s' "$existing_config" | jq -c '.repos // []')"
if [ "$(printf '%s' "$repos" | jq 'length')" = 0 ] && [ -t 0 ]; then
    say ''
    head 'No repos configured yet. Add them now (empty name to finish):'
    while true; do
        read -r -p 'Repo friendly name: ' repo_name || repo_name=""
        [ -n "$repo_name" ] || break
        read -r -p "Path of '$repo_name': " repo_path || repo_path=""
        if [ ! -d "$repo_path" ]; then
            warn '  Path does not exist, skipped.'
            continue
        fi
        repos="$(printf '%s' "$repos" | jq -c --arg name "$repo_name" --arg path "$repo_path" '. + [{name: $name, path: $path}]')"
    done
fi

# MERGED ONTO WHAT IS THERE, NEVER REBUILT FROM A FIELD LIST. The previous shape named six keys and
# silently dropped every other one the app persists — communicatorModel, voiceTranscribeCommand,
# orchestrationTokenBudget, telegramStatusScreenshots, and telegramItalianLayer, which is the setting
# the owner toggles from their phone (CLAUDE.md decision 11). A bootstrap re-run turned the Italian
# layer off and said nothing. Written to a temp file and moved into place, after a backup, so a jq
# that fails for any reason leaves the existing config untouched rather than truncated.
[ -f "$config_file" ] && cp "$config_file" "$config_file.aiorch-backup"

printf '%s' "$existing_config" | jq \
    --argjson repos "$repos" \
    --arg chatId "$current_chat_id" \
    --arg ownerId "$current_owner_id" \
    '. + {
        repos: $repos,
        generalSupervisorModel: (.generalSupervisorModel // "sonnet"),
        telegramSupergroupChatId: (if $chatId == "" then null else ($chatId | tonumber) end),
        telegramOwnerUserId: (if $ownerId == "" then null else ($ownerId | tonumber) end)
    }' > "$config_file.tmp" && mv "$config_file.tmp" "$config_file" || {
        rm -f "$config_file.tmp"
        warn "ERROR: could not write $config_file — the existing file was left untouched."
        exit 1
    }

jq -n --arg token "$current_token" '{telegramBotToken: (if $token == "" then null else $token end)}' > "$secrets_file.tmp" \
    && mv "$secrets_file.tmp" "$secrets_file"
chmod 600 "$secrets_file"

say ''
ok 'Config written:'
say "  $config_file"
say "  $secrets_file  (bot token — never commit this anywhere)"
if [ -z "$current_token" ] || [ -z "$current_chat_id" ] || [ -z "$current_owner_id" ]; then
    warn 'Telegram is NOT fully configured — the orchestrator runs in file-only mode until it is.'
fi

# --- 5. Build reminder -------------------------------------------------------------------------
say ''
head 'To build the headless host this machine needs the .NET 10 SDK. Then:'
say '  dotnet publish AIOrchestrator.Daemon -c Release -r <osx-arm64|linux-x64> --self-contained -o <install dir>'
say '  and install the service from deploy/launchd (macOS) or deploy/systemd (Linux) — see README-daemon.md'
say ''
head '=== Setup complete ==='
