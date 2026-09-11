namespace AIOrchestratorCoreLib.Running.TurnLiveness;

internal sealed class TurnSilenceBrakeModel(
    TimeSpan silenceLimit,
    TimeSpan pollInterval,
    Func<DateTime?> readTranscriptLastWriteUtc,
    Func<int, int?> countDescendants) : ITurnSilenceBrake
{
    readonly Func<DateTime?> _readTranscriptLastWriteUtc = readTranscriptLastWriteUtc;
    readonly Func<int, int?> _countDescendants = countDescendants;

    public TimeSpan SilenceLimit { get; } = silenceLimit;
    public TimeSpan PollInterval { get; } = pollInterval;

    public string? Decide_Kill_OrNull(DateTime nowUtc, DateTime turnStartedUtc, DateTime? lastOutputUtc, int processId)
    {
        // THE TURN'S OWN START IS THE FLOOR. A resumed transcript carries every earlier turn's writes,
        // and a stamp older than this turn says nothing about it — the stream brake's first version
        // measured from the PREVIOUS turn's last byte and killed 17 idle supervisors five seconds
        // into their next prompt (2026-09-09, fixed by b5f76072). Same trap, closed here by
        // construction.
        var lastSign = turnStartedUtc;

        if (lastOutputUtc > lastSign)
            lastSign = lastOutputUtc.Value;

        // Read only once the cheaper signal has run out: the transcript is a folder walk, and
        // output that arrived a second ago already answers the question.
        if (nowUtc - lastSign < SilenceLimit)
            return null;

        var transcript = _readTranscriptLastWriteUtc();

        if (transcript > lastSign)
            lastSign = transcript.Value;

        if (nowUtc - lastSign < SilenceLimit)
            return null;

        // A COMMAND STILL RUNNING IS WORK. A build or a suite writes nothing to the transcript until
        // it returns; measured 2026-09-11, an idle turn has no process below it and a working one has
        // its shell and the test runner. Null — the OS would not say — is read as alive.
        var descendants = _countDescendants(processId);

        if (descendants == null || descendants > 0)
            return null;

        return Describe_Kill(nowUtc - lastSign, SilenceLimit);
    }

    internal static string Describe_Kill(TimeSpan silentFor, TimeSpan limit)
    {
        return $"the turn showed no sign of life for {silentFor.TotalMinutes:F1} min — no output, no write to its transcript or a sub-agent's, no command running below it — and was killed (the limit is {limit.TotalMinutes:F1} min, set by '{TurnSilenceBrake_Factory.CONFIG_KEY}' in config.json)";
    }
}
