namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// The public constants of print dispatching — the words a supervisor (and a test) can look for
/// in a channel, and the limits a maintainer needs to know. The dispatcher model is internal;
/// these are its contract.
/// </summary>
public static class PrintTurn_Words
{
    /// <summary>Failed attempts of one turn before the session stalls until new traffic arrives.</summary>
    public const int MAX_ATTEMPTS = 3;

    /// <summary>The environment variable a print-run session reads to know it is one (the kit's conditional paragraph keys on it).</summary>
    public const string RUNNER_ENV_VAR = "AIORCH_RUNNER";

    /// <summary>Subject prefix of the record written after every attempt: outcome, cost, duration, api_error_status.</summary>
    public const string TURN_ENDED_SUBJECT = "turn_ended";

    /// <summary>Subject prefix of the alert written when a turn has failed MAX_ATTEMPTS times.</summary>
    public const string TURN_STALLED_SUBJECT = "turn stalled";
}
