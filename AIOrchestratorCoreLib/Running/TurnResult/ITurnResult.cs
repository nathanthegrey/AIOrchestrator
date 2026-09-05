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
