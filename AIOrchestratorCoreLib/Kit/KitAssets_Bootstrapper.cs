using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// WHAT A HOST DOES ABOUT THE KIT AT STARTUP. It used to COPY it — six role protocols, a helper and
/// seven hooks, into ~/.claude, at every single start. It now CHECKS it, because the kit is a plugin
/// with a version on it and checking a version is a comparison where copying was an investigation
/// (decisions 17, 18, 23).
///
/// Four steps, in this order, and the order matters:
///
///   1. un-wire the hooks the old builds put in ~/.claude/settings.json — they now live in the role
///      frontmatter, and leaving both would fire the ledger check twice for a supervisor and once
///      for every unrelated session on the machine
///   2. remove the role protocols the old builds copied into ~/.claude/commands — MEASURED: a local
///      command of the same name WINS the slash word over a plugin skill, so a leftover file means
///      sessions read the old text while the version number says otherwise
///   3. install the status line, which is not a plugin component and still needs a script on disk
///   4. verify what is installed, record the verdict, and tell the owner ONCE if it is bad
///
/// Never throws. A failed check is logged and the host starts anyway: the bridge keeps tailing,
/// mirroring and answering the owner, which is HOW they are told. What a bad verdict stops is
/// SESSIONS — see <see cref="IPluginGate"/> for why that split honours both rules at once.
/// </summary>
public static class KitAssets_Bootstrapper
{
    public const string WINDOWS_STATUSLINE_SCRIPT = "statusline.ps1";
    public const string POSIX_STATUSLINE_SCRIPT = "statusline.sh";

    /// <summary>The subject of the one owner-facing entry a bad kit produces.</summary>
    public const string REFUSAL_SUBJECT = "kit check FAILED — no session will start";

    /// <summary>
    /// <paramref name="kitFolder"/> is the kit shipped beside the host binary
    /// (<c>AppContext.BaseDirectory/kit</c>); <paramref name="claudeHomeFolder"/> is where Claude
    /// Code keeps settings.json and plugins/.
    /// </summary>
    public static void Ensure_Installed(
        string kitFolder,
        string claudeHomeFolder,
        ISupervisionPaths paths,
        IOrchestrationLog log,
        IPluginGate? gate = null)
    {
        try
        {
            var buildStamp = Build.BuildStamp_Reader.Describe_RunningApp();
            log.Log_Info("", $"Running {buildStamp} — from {AppContext.BaseDirectory}");

            Unwire_LegacyHooks(claudeHomeFolder, log);
            Remove_LegacyKit(claudeHomeFolder, log);
            Install_StatusLine(kitFolder, claudeHomeFolder, paths, log, buildStamp);
        }
        catch (Exception ex)
        {
            log.Log_Error("", "Kit startup housekeeping failed", ex);
        }

        // ITS OWN TRY, AND ALWAYS REACHED. It used to be the last of four steps inside one try, so
        // anything the three before it threw — a legacy file that would not move, a locked status
        // line, an install record with a number where a string belongs — jumped past the one call
        // that records a verdict. The gate stayed Unchecked, Unchecked ALLOWS, and every session
        // spawned against a kit this host had never verified. A verdict is now taken on every path.
        try
        {
            Verify_Plugin(claudeHomeFolder, paths, log, gate);
        }
        catch (Exception ex)
        {
            var refusal = $"This host could not run its kit check at all, so it CANNOT TELL which protocols its sessions would follow: {ex.Message}";
            log.Log_Error("", refusal, ex);
            gate?.Record(PluginVerdicts.Unreadable, refusal);
            Tell_Owner_Once(paths, log, refusal);
        }
    }

    static void Unwire_LegacyHooks(string claudeHomeFolder, IOrchestrationLog log)
    {
        var settingsFile = Path.Combine(claudeHomeFolder, "settings.json");
        var hooksFolder = Path.Combine(claudeHomeFolder, "hooks");

        foreach (var hookFile in LegacyKit_Remover.LEGACY_HOOK_FILES)
        {
            switch (AgentHookSettings_Wirer.Ensure_Unwired(settingsFile, Path.Combine(hooksFolder, hookFile)))
            {
                case UnwireOutcomes.Unwired:
                    log.Log_Info("", $"Un-wired the legacy '{hookFile}' entry from {settingsFile} — it now travels with the role that owns it, in the plugin");
                    break;

                case UnwireOutcomes.Unreadable:
                    log.Log_Warning("", $"COULD NOT READ {settingsFile}, so the legacy '{hookFile}' entry (if it is in there) is STILL WIRED — it would fire for every session on this machine, on top of the role that now declares it. Fix that file by hand.");
                    break;
            }
        }
    }

    static void Remove_LegacyKit(string claudeHomeFolder, IOrchestrationLog log)
    {
        var (movedAside, stillThere) = LegacyKit_Remover.Remove(claudeHomeFolder);

        foreach (var file in movedAside)
            log.Log_Info("", $"Moved the hand-installed kit file '{file}' aside to '{file}{LegacyKit_Remover.MOVED_ASIDE_SUFFIX}' — the plugin ships it now, and NOTHING was deleted");

        foreach (var file in stillThere)
            log.Log_Warning("", $"COULD NOT move the hand-installed kit file '{file}' aside — while it is there, a session may read it INSTEAD of the plugin");
    }

    static void Install_StatusLine(string kitFolder, string claudeHomeFolder, ISupervisionPaths paths, IOrchestrationLog log, string buildStamp)
    {
        var scriptName = Pick_StatuslineScriptName(OperatingSystem.IsWindows());
        var kitStatuslineFile = Path.Combine(kitFolder, "statusline", scriptName);
        var targetFile = Path.Combine(paths.Root, scriptName);

        foreach (var installedFile in KitAssets_Installer.Ensure_Installed(kitStatuslineFile, targetFile, $"{buildStamp} — {AppContext.BaseDirectory}"))
            log.Log_Info("", $"Status line installed/updated: {installedFile}");

        if (!File.Exists(kitStatuslineFile))
            log.Log_Warning("", $"Kit status line script not found at {kitStatuslineFile} — status line NOT installed");

        var settingsFile = Path.Combine(claudeHomeFolder, "settings.json");

        if (StatusLineSettings_Wirer.Ensure_Wired(settingsFile, targetFile))
            log.Log_Info("", $"Status line wired into {settingsFile} (previous file backed up); active for newly spawned sessions");
    }

    static void Verify_Plugin(string claudeHomeFolder, ISupervisionPaths paths, IOrchestrationLog log, IPluginGate? gate)
    {
        var reading = InstalledPlugin_Reader.Read(claudeHomeFolder, KitPlugin.ID);
        var shadowing = LegacyKit_Remover.Find_ShadowingCommands(claudeHomeFolder);
        var buildCommit = Build.BuildCommit_Reader.Read_RunningBuildCommit_OrNull();

        var verdict = PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, shadowing, buildCommit);
        var refusal = PluginVersion_Verifier.Describe(verdict, reading, KitPlugin.EXPECTED_VERSION, KitPlugin.ID, shadowing, buildCommit);

        gate?.Record(verdict, refusal);

        if (refusal == null)
        {
            // THE OK LINE NAMES THE COMMIT, or names what it could not compare. "Kit check OK" over a
            // cache holding a different commit is the exact sentence that cost the 2026-09-07 evening
            // on the VPS, and it was true of the only thing it had checked: the number.
            var content =
                buildCommit == null ? "content NOT VERIFIED — this build carries no commit stamp"
                : reading.CommitSha == null ? $"content NOT VERIFIED — the install record has no gitCommitSha (this host is {buildCommit[..7]})"
                : $"content verified — commit {reading.CommitSha[..Math.Min(7, reading.CommitSha.Length)]}";

            log.Log_Info("", $"Kit check OK — {KitPlugin.ID} {reading.Version} at {reading.InstallPath} · {content}");
            return;
        }

        log.Log_Error("", refusal, null);
        Tell_Owner_Once(paths, log, refusal);
    }

    /// <summary>
    /// ONE entry, on the general supervisor's own channel, which the bridge mirrors to the General
    /// topic — so the owner is told on their phone without this reaching into the bridge at all.
    ///
    /// It is written ONCE per host start and never repeated. Decision 14 asks that an owner-facing
    /// repeat EDITS rather than stacks; editing is private state inside the bridge engine and there
    /// is no way to it from here, so the rule is honoured the other way it can be — by not repeating.
    /// A startup fact does not change while the host runs, and one line is not a waterfall.
    /// </summary>
    static void Tell_Owner_Once(ISupervisionPaths paths, IOrchestrationLog log, string refusal)
    {
        try
        {
            if (!File.Exists(paths.GeneralChannelFile))
                return;

            ChannelAppender.Append_AppEntry(
                paths.GeneralChannelFile,
                AppEntryAudiences.Owner,
                REFUSAL_SUBJECT,
                refusal,
                DateTime.Now);
        }
        catch (Exception ex)
        {
            log.Log_Warning("", $"Could not put the kit refusal on the owner's channel: {ex.Message}");
        }
    }

    /// <summary>
    /// Which status line script a machine gets: the PowerShell one on Windows, its bash twin
    /// everywhere else. The OS is a parameter so both branches are asserted on one machine.
    /// </summary>
    public static string Pick_StatuslineScriptName(bool isWindows)
    {
        return isWindows ? WINDOWS_STATUSLINE_SCRIPT : POSIX_STATUSLINE_SCRIPT;
    }
}
