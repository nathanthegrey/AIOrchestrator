namespace AIOrchestratorCoreLib.Running.PrintTurnDispatcher;

/// <summary>
/// Turns inbound channel entries into print turns, one session at a time per session. Driven by
/// the bridge's mirror tick; owns the in-flight turns and kills them on <see cref="Stop_Async"/>.
/// </summary>
public interface IPrintTurnDispatcher
{
    /// <summary>One pass over every registered print session: reads its channel, starts a turn where one is due. Never blocks on a turn.</summary>
    void Tick(DateTime nowLocal);

    /// <summary>Turns started and not yet settled — for the tests and for a future status card.</summary>
    int InFlightCount { get; }

    /// <summary>
    /// Whether THIS session has a turn running right now.
    ///
    /// <para>
    /// THE ONE TRUE ANSWER TO "IS IT WORKING", and until now nobody could ask it. The bridge inferred
    /// liveness from <c>.usage.json</c>, a file the Claude Code status line writes — which a headless
    /// <c>claude -p</c> session never renders, so on a bridge-driven host the answer was permanently
    /// "no idea", read everywhere as "idle". That fiction reached the owner as "idle — waiting" over a
    /// working member, suppressed the typing bubble, fired stall alerts against busy orchestrations,
    /// and on one path condemned members that were mid-turn.
    /// </para>
    /// <para>
    /// This dispatcher STARTED the turn, so it does not have to infer anything. Same key the in-flight
    /// map already uses.
    /// </para>
    /// </summary>
    bool Is_TurnInFlight(string orchId, string memberId);

    /// <summary>Cancels every in-flight turn (process trees killed) and waits for them to settle.</summary>
    Task Stop_Async();
}
