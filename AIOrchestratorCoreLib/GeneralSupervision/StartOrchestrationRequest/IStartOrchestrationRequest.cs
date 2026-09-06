namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

/// <summary>
/// A request dropped by the GENERAL supervisor (as a .json file under .requests/) asking the app
/// to start a new orchestration. The app is the executor; the general supervisor is the brain.
/// The orchestration id is ALLOCATED BY THE APP (repo-slug-n, incremental) — never requested.
/// </summary>
public interface IStartOrchestrationRequest
{
    /// <summary>The repo the general supervisor resolved (it maps the owner's colloquial phrasing first).</summary>
    string RepoQuery { get; }

    /// <summary>
    /// FULL (supervisor + imp-1) or BASIC (one solo session, no supervisor). Absent means FULL, so
    /// every request written before this field existed keeps working unchanged.
    ///
    /// The capability was built and wired to a UI button but unreachable from the request protocol,
    /// so the owner could not get a basic session by asking the concierge — which is the one route
    /// they actually use from their phone.
    /// </summary>
    bool IsBasic { get; }

    /// <summary>
    /// THE OWNER'S OWN WORDS, carried with the request so the orchestration is born with something to
    /// do. Optional and usually present: the concierge writes the file the moment the owner says what
    /// they want, and dropping the "what" on the floor is how a full crew came up on 2026-09-06 with a
    /// supervisor, an implementer and a reviewer and nothing to work on.
    ///
    /// <para>
    /// IT IS NOT THE GENERAL SUPERVISOR'S TO WRITE, and this field is what keeps that true. The new
    /// orchestration's owner-channel.md stays READ-ONLY to it, as its protocol says; the APP appends
    /// the task there as a <c>FROM owner</c> entry, which is the same thing it already does with every
    /// message the owner types into a topic. One writer, one attribution, no new licence.
    /// </para>
    /// </summary>
    string? Task { get; }

    /// <summary>The request file, deleted after processing (success or failure) so it never loops.</summary>
    string SourceFilePath { get; }
}
