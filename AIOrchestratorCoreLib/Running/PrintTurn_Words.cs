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
    /// Subject prefix of the entry written when a turn was refused for a usage limit that named its
    /// reset. NOT <see cref="TURN_STALLED_SUBJECT"/>, deliberately: a stall means "not retried until
    /// new traffic arrives", and this turn has an appointment. The subject carries the time it keeps.
    /// </summary>
    public const string TURN_LIMITED_SUBJECT = "turn waiting on a usage limit";

    /// <summary>
    /// Subject prefix of the entry written ONCE when new traffic lands on a session that is waiting
    /// out a usage limit.
    ///
    /// <para>
    /// F5, probe 2026-09-09: <c>Consider_Session</c> returns on the appointment before it looks at
    /// what is pending, so the owner got their ✓ ack from the bridge and then hours of silence with
    /// nothing anywhere saying why. This entry is that "why", and it goes to the owner's audience for
    /// the roles that have one — it is actionable, which is the test decision 15 sets: <c>/resume</c>
    /// runs the session now.
    /// </para>
    /// <para>
    /// It deliberately does NOT contain <see cref="TURN_LIMITED_SUBJECT"/> as a substring: the two
    /// entries answer different questions, and a reader (or a test) counting one must not find the
    /// other.
    /// </para>
    /// </summary>
    public const string NEW_TRAFFIC_LIMITED_SUBJECT = "new traffic is waiting on a usage limit";

    /// <summary>
    /// How long after the stated reset the deferred turn is retried. Small on purpose — it exists
    /// only so the account is certainly past the boundary (the sentence names an hour, not a second,
    /// and the two clocks are not the same clock) and never as a throttle.
    ///
    /// <para>
    /// IT IS NOT WHAT BOUNDS THE WAIT — that is <see cref="LimitReset_Parser.MAX_DEFERRAL"/>, added
    /// after this margin turned out to be the thing that made a just-passed boundary re-park a session
    /// for a further day (the retry fires 30 s after the named hour, so a boundary the account has not
    /// really crossed refuses again and the same sentence is parsed one half-minute too late).
    /// </para>
    /// </summary>
    public static readonly TimeSpan LIMIT_RESET_MARGIN = TimeSpan.FromSeconds(30);

    /// <summary>Subject prefix of the note written when part of a reply named a channel the session is not woken by.</summary>
    public const string MISADDRESSED_SUBJECT = "reply not addressable";
}
