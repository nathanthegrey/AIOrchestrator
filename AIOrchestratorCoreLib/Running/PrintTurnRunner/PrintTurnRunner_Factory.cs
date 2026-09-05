using AIOrchestratorCoreLib.Running.ClaudeInvocation;

namespace AIOrchestratorCoreLib.Running.PrintTurnRunner;

public static class PrintTurnRunner_Factory
{
    public static IPrintTurnRunner Create(IClaudeInvocation invocation)
    {
        return new PrintTurnRunnerModel(invocation);
    }
}
