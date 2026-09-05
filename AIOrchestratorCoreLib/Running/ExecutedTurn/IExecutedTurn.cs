namespace AIOrchestratorCoreLib.Running.ExecutedTurn;

/// <summary>
/// One print turn the bridge ran to completion — the record that makes a turn idempotent. Its
/// <see cref="RequestId"/> is <c>&lt;orch&gt;/&lt;member&gt;/&lt;turn&gt;</c>; a turn whose id is already
/// here is never run again, and a resume after a crash tells the session which ones these were.
/// </summary>
public interface IExecutedTurn
{
    int TurnNumber { get; }
    string RequestId { get; }

    /// <summary>The inbound entries this turn answered — the lowest and highest index it was handed.</summary>
    int FirstEntryIndex { get; }
    int LastEntryIndex { get; }
    DateTime EndedUtc { get; }

    /// <summary>'success' | 'error' | 'timeout' — as written to the turn_ended entry.</summary>
    string Outcome { get; }
    double? CostUsd { get; }
}
