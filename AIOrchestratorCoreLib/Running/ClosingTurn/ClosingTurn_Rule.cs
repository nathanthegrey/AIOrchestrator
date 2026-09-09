using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running.ClosingTurn;

/// <summary>
/// WHICH KILL EARNS A CLOSING TURN — one question, answered in one place, because the two kills
/// look identical from the dispatcher's side and mean opposite things.
///
/// <para>
/// A DEADLINE KILL is a turn that ran out of its 30 minutes: measured on the VPS, those turns were
/// working (32–92 tool calls) and had something to report. A SILENCE KILL is the heartbeat catching
/// a process that has said nothing for the silence limit — it produced no bytes at all, so there is
/// nothing to close down and today's retry is the right answer. Both come back with
/// <see cref="ITurnResult.TimedOut"/> set and exit -1, which is why the silence kill has to be
/// MARKED at the place that knows (<see cref="TurnResult_Factory.CreateFrom_SilenceKill"/>, called
/// by the stream executor) rather than guessed at from a duration here.
/// </para>
/// </summary>
public static class ClosingTurn_Rule
{
    public static bool Is_DeadlineKill(ITurnResult result)
    {
        return result.TimedOut && !result.KilledOnSilence;
    }

    /// <summary>
    /// How long the closing turn gets: <see cref="ClosingTurn_Words.TIMEOUT"/>, or the session's own
    /// turn timeout when that is shorter. A closing turn is never given more time than the turn it
    /// is closing — and the clamp is also what lets the failure branch be tested in seconds rather
    /// than in five minutes.
    /// </summary>
    public static TimeSpan Resolve_Timeout(TimeSpan turnTimeout)
    {
        return turnTimeout < ClosingTurn_Words.TIMEOUT ? turnTimeout : ClosingTurn_Words.TIMEOUT;
    }
}
