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
        TimeSpan elapsed)
    {
        return new TurnResultModel(exitCode, timedOut, isError, subtype, resultText, sessionId, totalCostUsd, durationMs, durationApiMs, numTurns, apiErrorStatus, rawStdout, rawStderr, elapsed);
    }
}
