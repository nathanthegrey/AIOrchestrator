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

    /// <summary>Cancels every in-flight turn (process trees killed) and waits for them to settle.</summary>
    Task Stop_Async();
}
