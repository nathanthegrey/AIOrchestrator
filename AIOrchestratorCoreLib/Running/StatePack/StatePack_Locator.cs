using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// Where a role's pack lives — beside the files that role already reads at boot, so the skill's
/// one sentence ("if `pack.md` exists in your folder, read it first") resolves the same way for
/// everyone: members and the solo in `<orch>/<member>/pack.md`, the supervisor next to its state
/// file as `<orch>/.supervisor.pack.md`, the general in its own folder.
/// </summary>
public static class StatePack_Locator
{
    public const string FILE_NAME = "pack.md";
    public const string SUPERVISOR_FILE_NAME = ".supervisor.pack.md";

    /// <summary>
    /// THE PROGRESS NOTE a member keeps WHILE it works, beside its pack — one line per verified step,
    /// what is done and what is next. A print turn cannot append to its channel mid-turn (its final
    /// message is its only entry), so when the silence brake, the loop detector or the ceiling cuts
    /// it, this file and its commits are the only record of how far it got. Anthropic's long-running
    /// harness keeps exactly this pair — a progress file and git commits — so a fresh session "reads
    /// the git logs and progress files to get up to speed" instead of re-exploring (research
    /// 2026-09-11). Written by the member, read by the bridge into the next pack.
    /// </summary>
    public const string PROGRESS_FILE_NAME = "progress.md";

    /// <summary>The member's progress note, or null for a role that has no member folder (supervisor, general).</summary>
    public static string? Get_ProgressFile_OrNull(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role is SessionRoles.General or SessionRoles.Supervisor
            ? null
            : Path.Combine(paths.Get_OrchestrationFolder(orchId), memberId, PROGRESS_FILE_NAME);
    }

    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.General => Path.Combine(paths.GeneralFolder, FILE_NAME),
            SessionRoles.Supervisor => Path.Combine(paths.Get_OrchestrationFolder(orchId), SUPERVISOR_FILE_NAME),
            _ => Path.Combine(paths.Get_OrchestrationFolder(orchId), memberId, FILE_NAME),
        };
    }
}
