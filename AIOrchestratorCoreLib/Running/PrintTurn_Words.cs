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

    /// <summary>
    /// Subject prefix of the alert written when a session has been killed at the turn deadline
    /// <c>ClosingTurn_Words.KILLS_BEFORE_ALERT</c> turns in a row. NOT
    /// <see cref="TURN_STALLED_SUBJECT"/>: nothing is stalled — every one of those turns ended, was
    /// reported on by its closing turn and had its entries retired, which is precisely why the
    /// attempt counter never moved and the stall alert could never fire. What it says is that the
    /// work is not fitting inside a turn, which is a thing to act on and not a failure to retry.
    /// </summary>
    public const string DEADLINE_KILLS_SUBJECT = "turns killed at the deadline";

    /// <summary>
    /// Subject prefix of the entry written when a turn was refused for a usage limit that named its
    /// reset. NOT <see cref="TURN_STALLED_SUBJECT"/>, deliberately: a stall means "not retried until
    /// new traffic arrives", and this turn has an appointment. The subject carries the time it keeps.
    /// </summary>
    public const string TURN_LIMITED_SUBJECT = "turn waiting on a usage limit";

    /// <summary>
    /// How long after the stated reset the deferred turn is retried. Small on purpose — it exists
    /// only so the account is certainly past the boundary (the sentence names an hour, not a second,
    /// and the two clocks are not the same clock) and never as a throttle.
    /// </summary>
    public static readonly TimeSpan LIMIT_RESET_MARGIN = TimeSpan.FromSeconds(30);

    /// <summary>Subject prefix of the note written when part of a reply named a channel the session is not woken by.</summary>
    public const string MISADDRESSED_SUBJECT = "reply not addressable";
}
