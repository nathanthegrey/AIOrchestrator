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

    /// <summary>
    /// How long a LIVING stream process may say nothing before it is treated as hung, killed and
    /// resumed. Shorter than <see cref="TurnTimeout"/> on purpose: a process that is alive and mute
    /// is a different animal from a turn that is genuinely thinking for half an hour, and waiting
    /// out the turn timeout to notice it costs the owner a supervisor for that whole time.
    /// </summary>
    TimeSpan SilenceLimit { get; }

    /// <summary>
    /// Configurations the loader REFUSED, in the owner's own terms — one line each, logged once by
    /// the launcher. A refusal is not a parse error (config.json still loads, the role falls back to
    /// terminal): it is a setting that would have done something the app must not do, and the only
    /// unacceptable outcome is applying it silently.
    /// </summary>
    IReadOnlyList<string> Rejections { get; }
}
