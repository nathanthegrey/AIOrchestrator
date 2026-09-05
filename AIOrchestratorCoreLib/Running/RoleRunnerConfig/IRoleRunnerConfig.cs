namespace AIOrchestratorCoreLib.Running.RoleRunnerConfig;

/// <summary>How one role's sessions are run — the per-role block under <c>runners</c> in config.json.</summary>
public interface IRoleRunnerConfig
{
    SessionRunners Runner { get; }
    ResumeModes Resume { get; }

    /// <summary>
    /// Passed as <c>--permission-mode</c> to a print-run session. Null keeps today's behaviour,
    /// <c>--dangerously-skip-permissions</c> (owner directive: an unattended session must never
    /// hang on a prompt). Only the print runner reads it in this stage.
    /// </summary>
    string? PermissionMode { get; }
}
