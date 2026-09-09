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
    /// THERE IS NO HALF-DONE WORK BEHIND THIS KILL, when <see cref="TimedOut"/> is set — and the
    /// reason it is a field rather than something inferred from the elapsed time. Every kill comes
    /// back the same way (timed out, exit -1, no result document), yet they mean opposite things,
    /// and only a kill that interrupted WORK earns a closing turn
    /// (<see cref="ClosingTurn.ClosingTurn_Rule"/>). So it is MARKED where it is known — by the
    /// stream executor, which is the only place that still knows — and never guessed at later.
    ///
    /// <para>
    /// TWO PRODUCERS, both in the stream executor, both meaning "the model was never asked the
    /// question this turn is about". The first is the SILENCE kill: the heartbeat caught a process
    /// that had said nothing for the silence limit, so it was not working. The second is the BOOT:
    /// a session whose role command alone outlived the turn timeout never received its brief at all
    /// (adversarial review, 2026-09-09 — it returned as an ordinary deadline kill, so it was given a
    /// closing turn that resumed a transcript containing nothing but the boot, and the brief the
    /// session had never seen was marked delivered).
    /// </para>
    /// </summary>
    bool NothingToClose { get; }

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
