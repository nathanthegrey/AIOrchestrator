using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.TurnCursor;

namespace AIOrchestratorCoreLib.Running.PrintSessionState;

public static class PrintSessionState_Factory
{
    public static IPrintSessionState Create(
        string sessionId,
        bool sessionStarted,
        SessionRoles role,
        string orchId,
        string memberId,
        string workingDirectory,
        string? model,
        string channelFilePath,
        IReadOnlyList<ITurnCursor> cursors,
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
        if (cursors.Select(cursor => cursor.SourceKey).Distinct(StringComparer.Ordinal).Count() != cursors.Count)
            throw new ArgumentException($"Two cursors share a source key ('{orchId}/{memberId}') — a source is delivered under exactly one cursor or it is delivered twice");

        return new PrintSessionStateModel(sessionId, sessionStarted, role, orchId, memberId, workingDirectory, model, channelFilePath, cursors, nextTurnNumber, failedAttempts, executedTurns);
    }

    /// <summary>
    /// A session the bridge has just registered. <paramref name="cursors"/> is the baseline of every
    /// source the roster knows at this instant — see <see cref="TurnCursor_Factory.Create_Baseline"/> for
    /// why registration and not the first tick is the moment history stops and traffic starts.
    /// </summary>
    public static IPrintSessionState Create_New(string sessionId, SessionRoles role, string orchId, string memberId, string workingDirectory, string? model, string channelFilePath, IReadOnlyList<ITurnCursor> cursors)
    {
        return Create(sessionId, false, role, orchId, memberId, workingDirectory, model, channelFilePath, cursors, 1, 0, []);
    }

    /// <summary>A turn completed: recorded, the cursors advanced, attempts reset, the transcript id possibly replaced (Fresh mode).</summary>
    public static IPrintSessionState CreateFrom_Existing_TurnExecuted(IPrintSessionState source, IExecutedTurn executed, string sessionId, IReadOnlyList<ITurnCursor> cursors)
    {
        return Create(
            sessionId,
            true,
            source.Role,
            source.OrchId,
            source.MemberId,
            source.WorkingDirectory,
            source.Model,
            source.ChannelFilePath,
            cursors,
            Math.Max(source.NextTurnNumber, executed.TurnNumber + 1),
            0,
            [.. source.ExecutedTurns, executed]);
    }

    /// <summary>
    /// A source was seen for the first time (or one disappeared with its member): the cursor set is
    /// replaced and NOTHING else moves. It is its own transition because it happens outside a turn — a
    /// baseline taken on a tick that starts no turn still has to survive a restart, or the same history
    /// is absorbed again and announced again on every tick.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_Cursors(IPrintSessionState source, IReadOnlyList<ITurnCursor> cursors)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, cursors, source.NextTurnNumber, source.FailedAttempts, source.ExecutedTurns);
    }

    /// <summary>
    /// The id this session is about to hand the CLI as <c>--session-id</c>, marked as claimed
    /// BEFORE the process starts. Every later attempt then resumes it instead of re-claiming it,
    /// which the CLI refuses.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_SessionClaimed(IPrintSessionState source, string sessionId)
    {
        return Create(sessionId, true, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, source.FailedAttempts, source.ExecutedTurns);
    }

    /// <summary>A turn attempt failed (timeout or error): counted, nothing else moves — the same request id retries.</summary>
    public static IPrintSessionState CreateFrom_Existing_AttemptFailed(IPrintSessionState source)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, source.FailedAttempts + 1, source.ExecutedTurns);
    }

    /// <summary>New traffic arrived after a stall: the attempt counter starts over for the next turn.</summary>
    public static IPrintSessionState CreateFrom_Existing_AttemptsReset(IPrintSessionState source)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, 0, source.ExecutedTurns);
    }

    /// <summary>A request id found already executed: the turn number is skipped without running anything.</summary>
    public static IPrintSessionState CreateFrom_Existing_TurnSkipped(IPrintSessionState source)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber + 1, 0, source.ExecutedTurns);
    }
}
