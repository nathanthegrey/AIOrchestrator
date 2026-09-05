using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Running.RunnerConfigs;

public static class RunnerConfigs_Factory
{
    public const int DEFAULT_MAX_CONCURRENT_TURNS = 10;
    public const int DEFAULT_MAX_CONCURRENT_TURNS_PER_ORCHESTRATION = 3;
    public static readonly TimeSpan DEFAULT_TURN_TIMEOUT = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DEFAULT_COALESCE_WINDOW = TimeSpan.FromSeconds(3);

    public static IRunnerConfigs Create(
        IReadOnlyDictionary<SessionRoles, IRoleRunnerConfig> roles,
        int maxConcurrentTurns,
        int maxConcurrentTurnsPerOrchestration,
        TimeSpan turnTimeout,
        TimeSpan coalesceWindow)
    {
        if (maxConcurrentTurns < 1)
            throw new ArgumentException($"maxConcurrentTurns must be >= 1, got {maxConcurrentTurns}");
        if (maxConcurrentTurnsPerOrchestration < 1)
            throw new ArgumentException($"maxConcurrentTurnsPerOrchestration must be >= 1, got {maxConcurrentTurnsPerOrchestration}");
        if (turnTimeout <= TimeSpan.Zero)
            throw new ArgumentException($"turnTimeout must be positive, got {turnTimeout}");
        if (coalesceWindow < TimeSpan.Zero)
            throw new ArgumentException($"coalesceWindow must not be negative, got {coalesceWindow}");

        return new RunnerConfigsModel(roles, maxConcurrentTurns, maxConcurrentTurnsPerOrchestration, turnTimeout, coalesceWindow);
    }

    /// <summary>Terminal for every role, default limits — what an absent block means.</summary>
    public static IRunnerConfigs Create_Default()
    {
        return Create(new Dictionary<SessionRoles, IRoleRunnerConfig>(), DEFAULT_MAX_CONCURRENT_TURNS, DEFAULT_MAX_CONCURRENT_TURNS_PER_ORCHESTRATION, DEFAULT_TURN_TIMEOUT, DEFAULT_COALESCE_WINDOW);
    }

    /// <summary>The same limits with one role's block replaced.</summary>
    public static IRunnerConfigs Create_WithRole(IRunnerConfigs source, SessionRoles role, IRoleRunnerConfig roleConfig)
    {
        Dictionary<SessionRoles, IRoleRunnerConfig> roles = [];

        foreach (var known in SessionRole_Names.ALL)
            roles[known] = source.Get_ForRole(known);

        roles[role] = roleConfig;

        return Create(roles, source.MaxConcurrentTurns, source.MaxConcurrentTurnsPerOrchestration, source.TurnTimeout, source.CoalesceWindow);
    }

    /// <summary>The same roles with the limits replaced.</summary>
    public static IRunnerConfigs Create_WithLimits(IRunnerConfigs source, int maxConcurrentTurns, int maxConcurrentTurnsPerOrchestration, TimeSpan turnTimeout, TimeSpan coalesceWindow)
    {
        Dictionary<SessionRoles, IRoleRunnerConfig> roles = [];

        foreach (var known in SessionRole_Names.ALL)
            roles[known] = source.Get_ForRole(known);

        return Create(roles, maxConcurrentTurns, maxConcurrentTurnsPerOrchestration, turnTimeout, coalesceWindow);
    }
}
