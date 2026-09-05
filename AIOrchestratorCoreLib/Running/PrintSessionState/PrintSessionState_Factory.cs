using AIOrchestratorCoreLib.Running.ExecutedTurn;

namespace AIOrchestratorCoreLib.Running.PrintSessionState;

public static class PrintSessionState_Factory
{
    public static IPrintSessionState Create(
        string sessionId,
        SessionRoles role,
        string orchId,
        string memberId,
        string workingDirectory,
        string? model,
        string channelFilePath,
        int lastHandledEntryIndex,
        int nextTurnNumber,
        int failedAttempts,
        IReadOnlyList<IExecutedTurn> executedTurns)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException($"Session id must be non-empty ('{orchId}/{memberId}')");
        if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException($"Orchestration and member ids must be non-empty (got '{orchId}/{memberId}')");
        if (string.IsNullOrWhiteSpace(channelFilePath))
            throw new ArgumentException($"Channel file path must be non-empty ('{orchId}/{memberId}')");
        if (nextTurnNumber < 1)
            throw new ArgumentException($"Next turn number must be >= 1, got {nextTurnNumber} ('{orchId}/{memberId}')");
        if (failedAttempts < 0)
            throw new ArgumentException($"Failed attempts must be >= 0, got {failedAttempts} ('{orchId}/{memberId}')");

        return new PrintSessionStateModel(sessionId, role, orchId, memberId, workingDirectory, model, channelFilePath, lastHandledEntryIndex, nextTurnNumber, failedAttempts, executedTurns);
    }

    public static IPrintSessionState Create_New(string sessionId, SessionRoles role, string orchId, string memberId, string workingDirectory, string? model, string channelFilePath)
    {
        return Create(sessionId, role, orchId, memberId, workingDirectory, model, channelFilePath, 0, 1, 0, []);
    }

    /// <summary>A turn completed: recorded, the handled index advanced, attempts reset, the transcript id possibly replaced (Fresh mode).</summary>
    public static IPrintSessionState CreateFrom_Existing_TurnExecuted(IPrintSessionState source, IExecutedTurn executed, string sessionId)
    {
        return Create(
            sessionId,
            source.Role,
            source.OrchId,
            source.MemberId,
            source.WorkingDirectory,
            source.Model,
            source.ChannelFilePath,
            Math.Max(source.LastHandledEntryIndex, executed.LastEntryIndex),
            Math.Max(source.NextTurnNumber, executed.TurnNumber + 1),
            0,
            [.. source.ExecutedTurns, executed]);
    }

    /// <summary>A turn attempt failed (timeout or error): counted, nothing else moves — the same request id retries.</summary>
    public static IPrintSessionState CreateFrom_Existing_AttemptFailed(IPrintSessionState source)
    {
        return Create(source.SessionId, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.LastHandledEntryIndex, source.NextTurnNumber, source.FailedAttempts + 1, source.ExecutedTurns);
    }

    /// <summary>New traffic arrived after a stall: the attempt counter starts over for the next turn.</summary>
    public static IPrintSessionState CreateFrom_Existing_AttemptsReset(IPrintSessionState source)
    {
        return Create(source.SessionId, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.LastHandledEntryIndex, source.NextTurnNumber, 0, source.ExecutedTurns);
    }

    /// <summary>A request id found already executed: the turn number is skipped without running anything.</summary>
    public static IPrintSessionState CreateFrom_Existing_TurnSkipped(IPrintSessionState source)
    {
        return Create(source.SessionId, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.LastHandledEntryIndex, source.NextTurnNumber + 1, 0, source.ExecutedTurns);
    }
}
