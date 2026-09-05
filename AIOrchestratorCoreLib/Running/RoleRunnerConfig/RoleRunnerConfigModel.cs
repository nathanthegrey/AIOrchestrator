namespace AIOrchestratorCoreLib.Running.RoleRunnerConfig;

internal sealed class RoleRunnerConfigModel(SessionRunners runner, ResumeModes resume, string? permissionMode) : IRoleRunnerConfig
{
    public SessionRunners Runner { get; } = runner;
    public ResumeModes Resume { get; } = resume;
    public string? PermissionMode { get; } = permissionMode;
}
