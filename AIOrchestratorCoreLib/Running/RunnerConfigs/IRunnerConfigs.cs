using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Running.RunnerConfigs;

/// <summary>
/// The whole runner configuration: one <see cref="IRoleRunnerConfig"/> per role, plus the limits
/// the print dispatcher works under. Read live through the config provider like every other
/// setting, so an edit to config.json applies to the next spawn and the next turn.
/// </summary>
public interface IRunnerConfigs
{
    IRoleRunnerConfig Get_ForRole(SessionRoles role);

    /// <summary>Print turns running at once across every orchestration (a `claude -p` is ~270 MB RSS while it runs).</summary>
    int MaxConcurrentTurns { get; }

    /// <summary>Print turns running at once inside one orchestration.</summary>
    int MaxConcurrentTurnsPerOrchestration { get; }

    /// <summary>A print turn that outlives this is killed (process tree) and re-queued.</summary>
    TimeSpan TurnTimeout { get; }

    /// <summary>Entries landing within this window of each other ride one turn.</summary>
    TimeSpan CoalesceWindow { get; }
}
