using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Running.RunnerConfigs;

internal sealed class RunnerConfigsModel(
    IReadOnlyDictionary<SessionRoles, IRoleRunnerConfig> roles,
    int maxConcurrentTurns,
    int maxConcurrentTurnsPerOrchestration,
    TimeSpan turnTimeout,
    TimeSpan coalesceWindow) : IRunnerConfigs
{
    readonly IReadOnlyDictionary<SessionRoles, IRoleRunnerConfig> _roles = roles;

    public int MaxConcurrentTurns { get; } = maxConcurrentTurns;
    public int MaxConcurrentTurnsPerOrchestration { get; } = maxConcurrentTurnsPerOrchestration;
    public TimeSpan TurnTimeout { get; } = turnTimeout;
    public TimeSpan CoalesceWindow { get; } = coalesceWindow;

    public IRoleRunnerConfig Get_ForRole(SessionRoles role)
    {
        return _roles.TryGetValue(role, out var config) ? config : RoleRunnerConfig_Factory.Create_Default(role);
    }
}
