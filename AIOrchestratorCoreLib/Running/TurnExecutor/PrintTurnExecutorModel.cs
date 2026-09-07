using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnExecutor;

/// <summary>
/// The transient transport, unchanged: one <c>claude -p</c> per turn, the role command as the
/// positional prompt on the first turn and the pending entries on stdin after. Everything here was
/// the dispatcher's own body before the seam existed — moved, not rewritten.
///
/// The one addition is the turn log: the result JSON is appended as one line per turn, so /log and
/// /tail can answer for a print session too instead of only for a stream.
/// </summary>
internal sealed class PrintTurnExecutorModel(ISupervisionPaths paths, IPrintTurnRunner turnRunner) : ITurnExecutor
{
    readonly ISupervisionPaths _paths = paths;
    readonly IPrintTurnRunner _turnRunner = turnRunner;

    public SessionRunners Kind => SessionRunners.Print;

    public async Task<ITurnResult> Execute_Async(
        IPrintSessionState state,
        IRoleRunnerConfig roleConfig,
        string sessionId,
        bool resumeTranscript,
        string requestId,
        IReadOnlyList<PendingEntry> pending,
        IReadOnlyList<TurnSource.ITurnSource> sources,
        IReadOnlyList<int> alreadyExecutedTurns,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var arguments = PrintTurnCommand_Builder.Build_Arguments(state, roleConfig, sessionId, resumeTranscript, null);
        var prompt = resumeTranscript ? PrintTurnPrompt_Builder.Build_FollowUp(requestId, pending, alreadyExecutedTurns, sources) : null;

        var result = await _turnRunner.Run_Async(arguments, prompt, state.WorkingDirectory, environment, timeout, cancellationToken);

        TurnLog_Store.Append_TurnResult(TurnLog_Store.Get_File(_paths, state.Role, state.OrchId, state.MemberId), requestId, result);

        return result;
    }

    public void Release(string orchId, string memberId)
    {
        // Nothing is held between turns: that is what "print" means.
    }

    public bool Consume_RunnerChange(string orchId, string memberId)
    {
        // The bottom rung: there is nothing below to change to.
        return false;
    }

    public Task Stop_Async()
    {
        return Task.CompletedTask;
    }
}
