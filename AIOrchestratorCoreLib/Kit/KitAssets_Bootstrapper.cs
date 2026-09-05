using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Launching a host must be enough: every role command in the shipped kit, plus the status line
/// script and the hooks, self-install/refresh from the host's output folder — no install script
/// prerequisite for them. This is THE delivery path: editing kit/commands in the repo changes
/// nothing until a rebuild refreshes that output folder. Moved here from the WPF App so the
/// daemon runs the identical sequence rather than a second copy of it.
///
/// Never throws: a failed self-install is logged and the host starts anyway — the file protocol
/// works without the kit, and a bridge that will not start is worse than stale commands.
/// </summary>
public static class KitAssets_Bootstrapper
{
    public const string WINDOWS_STATUSLINE_SCRIPT = "statusline.ps1";
    public const string POSIX_STATUSLINE_SCRIPT = "statusline.sh";

    /// <summary>
    /// <paramref name="kitFolder"/> is the shipped kit beside the host binary (<c>AppContext.BaseDirectory/kit</c>);
    /// <paramref name="claudeHomeFolder"/> is where Claude Code reads commands/, hooks/ and settings.json.
    /// </summary>
    public static void Ensure_Installed(string kitFolder, string claudeHomeFolder, ISupervisionPaths paths, IOrchestrationLog log)
    {
        try
        {
            var kitCommandsFolder = Path.Combine(kitFolder, "commands");
            var kitHooksFolder = Path.Combine(kitFolder, "hooks");
            var claudeCommandsFolder = Path.Combine(claudeHomeFolder, "commands");
            var claudeHooksFolder = Path.Combine(claudeHomeFolder, "hooks");

            var statuslineScriptName = Pick_StatuslineScriptName(OperatingSystem.IsWindows());
            var kitStatuslineFile = Path.Combine(kitFolder, "statusline", statuslineScriptName);
            var statuslineTargetFile = Path.Combine(paths.Root, statuslineScriptName);

            // The stamp goes in the log FIRST and into the installed folder second: which app is
            // running, and which app owns the commands the sessions read. Both were guesswork.
            var buildStamp = Build.BuildStamp_Reader.Describe_RunningApp();
            log.Log_Info("", $"Running {buildStamp} — from {AppContext.BaseDirectory}");

            var installedFiles = KitAssets_Installer.Ensure_Installed(
                kitCommandsFolder, kitStatuslineFile, claudeCommandsFolder, statuslineTargetFile,
                kitHooksFolder, claudeHooksFolder, $"{buildStamp} — {AppContext.BaseDirectory}");

            foreach (var installedFile in installedFiles)
                log.Log_Info("", $"Kit asset installed/updated: {installedFile}");

            if (!Directory.Exists(kitCommandsFolder))
                log.Log_Warning("", $"Kit commands folder not found at {kitCommandsFolder} — role commands NOT installed");

            if (!File.Exists(kitStatuslineFile))
                log.Log_Warning("", $"Kit status line script not found at {kitStatuslineFile} — status line NOT installed");

            var settingsFile = Path.Combine(claudeHomeFolder, "settings.json");

            if (StatusLineSettings_Wirer.Ensure_Wired(settingsFile, statuslineTargetFile))
                log.Log_Info("", $"Status line wired into {settingsFile} (previous file backed up); active for newly spawned sessions");

            // Turn-end enforcement for the task ledger: prose in a role command gets skipped, a
            // Stop hook does not.
            var ledgerHookFile = Path.Combine(claudeHooksFolder, "supervisor-ledger-check.sh");

            if (AgentHookSettings_Wirer.Ensure_Wired(settingsFile, ledgerHookFile, AgentHookSettings_Wirer.STOP_EVENT, null))
                log.Log_Info("", $"Ledger Stop hook wired into {settingsFile}; supervisors spawned from now on cannot end a turn owing a PLAN.md update");

            // Turn-end enforcement for "run to the end". The owner told sessions not to stop
            // mid-endeavour and they kept stopping — "no matter how many times i tell it not to get
            // stuck and keep going, it will keep getting stuck" (2026-08-20). Prose is what had
            // already failed; this is the same lever the ledger got, for the same reason.
            var runToTheEndHookFile = Path.Combine(claudeHooksFolder, "run-to-the-end-check.sh");

            if (AgentHookSettings_Wirer.Ensure_Wired(settingsFile, runToTheEndHookFile, AgentHookSettings_Wirer.STOP_EVENT, null))
                log.Log_Info("", $"Run-to-the-end Stop hook wired into {settingsFile}; a session with open ledger work and nothing blocked on the owner cannot end its turn");

            // Read-only enforcement for reviewers: the CLI already withholds Write/Edit, but Bash
            // could mutate the repo just as effectively — this closes that route.
            var reviewerHookFile = Path.Combine(claudeHooksFolder, "reviewer-readonly-check.sh");

            // A question stops the supervisor dead: no tool runs while the owner's answer is
            // pending, so their answer can never arrive against a world that moved meanwhile.
            var awaitingAnswerHookFile = Path.Combine(claudeHooksFolder, "supervisor-awaiting-answer-check.sh");

            if (AgentHookSettings_Wirer.Ensure_Wired(settingsFile, awaitingAnswerHookFile, AgentHookSettings_Wirer.PRE_TOOL_USE_EVENT, "*"))
                log.Log_Info("", $"Awaiting-answer PreToolUse hook wired into {settingsFile}; a supervisor that asked a question cannot act until it is answered");

            if (AgentHookSettings_Wirer.Ensure_Wired(settingsFile, reviewerHookFile, AgentHookSettings_Wirer.PRE_TOOL_USE_EVENT, "Bash"))
                log.Log_Info("", $"Reviewer read-only PreToolUse hook wired into {settingsFile}; reviewers spawned from now on cannot mutate the repo through Bash");
        }
        catch (Exception ex)
        {
            log.Log_Error("", "Kit asset self-install failed", ex);
        }
    }

    /// <summary>
    /// Which status line script a machine gets: the PowerShell one on Windows (unchanged), its
    /// bash twin everywhere else. The OS is a parameter so both branches are asserted on one
    /// machine.
    /// </summary>
    public static string Pick_StatuslineScriptName(bool isWindows)
    {
        return isWindows ? WINDOWS_STATUSLINE_SCRIPT : POSIX_STATUSLINE_SCRIPT;
    }
}
