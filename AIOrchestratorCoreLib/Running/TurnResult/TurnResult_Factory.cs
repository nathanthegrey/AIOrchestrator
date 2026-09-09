namespace AIOrchestratorCoreLib.Running.TurnResult;

public static class TurnResult_Factory
{
    public static ITurnResult Create(
        int exitCode,
        bool timedOut,
        bool isError,
        string? subtype,
        string? resultText,
        string? sessionId,
        double? totalCostUsd,
        long? durationMs,
        long? durationApiMs,
        int? numTurns,
        int? apiErrorStatus,
        string rawStdout,
        string rawStderr,
        TimeSpan elapsed,
        bool killedOnSilence = false)
    {
        return new TurnResultModel(exitCode, timedOut, isError, subtype, resultText, sessionId, totalCostUsd, durationMs, durationApiMs, numTurns, apiErrorStatus, rawStdout, rawStderr, elapsed, killedOnSilence);
    }

    /// <summary>
    /// The same result, marked as the HEARTBEAT's kill rather than the deadline's. Called by the
    /// stream executor, which is the only place that holds the distinction: the process was alive
    /// and mute, so there is no half-done work to close down and today's retry is the right answer
    /// (<see cref="ClosingTurn.ClosingTurn_Rule"/>).
    /// </summary>
    public static ITurnResult CreateFrom_SilenceKill(ITurnResult source)
    {
        return Create(
            source.ExitCode, source.TimedOut, source.IsError, source.Subtype, source.ResultText, source.SessionId,
            source.TotalCostUsd, source.DurationMs, source.DurationApiMs, source.NumTurns, source.ApiErrorStatus,
            source.RawStdout, source.RawStderr, source.Elapsed, killedOnSilence: true);
    }
}
