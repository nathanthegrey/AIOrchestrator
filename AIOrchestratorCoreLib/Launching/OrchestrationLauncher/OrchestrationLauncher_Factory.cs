using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Running.SessionRunner;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Launching.OrchestrationLauncher;

public static class OrchestrationLauncher_Factory
{
    /// <summary>The production shape: the terminal runner wraps the spawner, the print runner registers against the paths.</summary>
    public static IOrchestrationLauncher Create(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        ISessionSpawner spawner,
        IOrchestrationLog log)
    {
        return Create(paths, configProvider, store, SessionRunner_Factory.Create_Terminal(spawner), SessionRunner_Factory.Create_Print(paths, log), log);
    }

    public static IOrchestrationLauncher Create(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        ISessionRunner terminalRunner,
        ISessionRunner printRunner,
        IOrchestrationLog log)
    {
        return new OrchestrationLauncherModel(paths, configProvider, store, terminalRunner, printRunner, log);
    }
}
