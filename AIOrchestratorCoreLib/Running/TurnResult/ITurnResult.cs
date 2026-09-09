namespace AIOrchestratorCoreLib.Running.TurnResult;

/// <summary>
/// What one <c>claude -p --output-format json</c> invocation came back with. Every JSON-derived
/// field is nullable: the parser is tolerant like <c>Limits.LimitData_Parser</c>, because a field
/// missing from a newer CLI is a fact to record, not a reason to lose the turn.
/// </summary>
public interface ITurnResult
{
    int ExitCode { get; }
    bool TimedOut { get; }

    /// <summary>
    /// WHICH KILL THIS WAS, when <see cref="TimedOut"/> is set — and the reason it is a field rather
    /// than something inferred from the elapsed time. The heartbeat kills a process that has said
    /// nothing for the silence limit (2 minutes by default) and the deadline kills a turn that
    /// outlived the turn timeout (30 minutes): both come back timed out, with exit -1 and no result
    /// document, so from here they are indistinguishable — yet one produced no bytes at all and the
    /// other was working. Only the deadline kill earns a closing turn
    /// (<see cref="ClosingTurn.ClosingTurn_Rule"/>), so the silence kill is MARKED where it is known,
    /// by the stream executor, and never guessed at later.
    /// </summary>
    bool KilledOnSilence { get; }

    /// <summary>The CLI's own verdict (<c>is_error</c>); false when the JSON was absent.</summary>
    bool IsError { get; }
    string? Subtype { get; }

    /// <summary>The session's final message — what becomes its channel entry.</summary>
    string? ResultText { get; }
    string? SessionId { get; }
    double? TotalCostUsd { get; }
    long? DurationMs { get; }
    long? DurationApiMs { get; }
    int? NumTurns { get; }
    int? ApiErrorStatus { get; }
    string RawStdout { get; }
    string RawStderr { get; }
    TimeSpan Elapsed { get; }
}
