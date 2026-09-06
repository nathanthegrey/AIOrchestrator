using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.SessionRunner;

public static class SessionRunner_Factory
{
    public static ISessionRunner Create_Terminal(ISessionSpawner spawner)
    {
        return new TerminalRunnerModel(spawner);
    }

    public static ISessionRunner Create_Print(ISupervisionPaths paths, IOrchestrationLog log)
    {
        return new BridgeDrivenRunnerModel(SessionRunners.Print, paths, log);
    }

    public static ISessionRunner Create_Stream(ISupervisionPaths paths, IOrchestrationLog log)
    {
        return new BridgeDrivenRunnerModel(SessionRunners.Stream, paths, log);
    }
}
