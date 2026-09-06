using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnExecutor;

public static class TurnExecutor_Factory
{
    public static ITurnExecutor Create_Print(ISupervisionPaths paths, IPrintTurnRunner turnRunner)
    {
        return new PrintTurnExecutorModel(paths, turnRunner);
    }

    /// <summary>
    /// <paramref name="fallback"/> is the rung below on <see cref="RunnerFallback_Ladder"/> — the
    /// print executor in production. It is a parameter rather than something this factory looks up
    /// so that the ladder stays a decision made once, in the open, by whoever wires the app.
    /// </summary>
    public static ITurnExecutor Create_Stream(
        ISupervisionPaths paths,
        IClaudeInvocation invocation,
        ITurnExecutor? fallback,
        IOrchestrationLog log,
        IOrchestratorConfigProvider configProvider)
    {
        return new StreamTurnExecutorModel(paths, invocation, fallback, log, configProvider);
    }

    /// <summary>
    /// The production set: both bridge-driven transports, with the stream's fallback wired to the
    /// print one. One call so the two hosts and the tests cannot wire a different ladder from each
    /// other.
    /// </summary>
    public static IReadOnlyList<ITurnExecutor> Create_All(
        ISupervisionPaths paths,
        IClaudeInvocation invocation,
        IOrchestrationLog log,
        IOrchestratorConfigProvider configProvider)
    {
        var print = Create_Print(paths, PrintTurnRunner_Factory.Create(invocation));

        return [print, Create_Stream(paths, invocation, print, log, configProvider)];
    }
}
