namespace AIOrchestratorCoreLib.Bridge.ChannelChangeWaker;

public static class ChannelChangeWaker_Factory
{
    /// <summary>
    /// How long a wake waits for the rest of its burst before the loop runs. One append raises several
    /// events (a size change and a write stamp are two notifications for one <c>AppendAllText</c>), and
    /// a crew filing four reports at once is ONE arrival, not four ticks.
    /// </summary>
    public const int DEBOUNCE_MILLISECONDS = 150;

    /// <summary>
    /// The gap between the short polls that follow a wake — see <see cref="SETTLING_PULSES"/>.
    /// </summary>
    public const int SETTLING_PULSE_MILLISECONDS = 200;

    /// <summary>
    /// WHY A WAKE IS NOT ONE POLL. <c>ChannelTailerModel.QUIET_POLLS_TO_FLUSH</c> is 2: the tailer
    /// releases a channel's LAST entry only after two further polls have seen the file stop growing,
    /// because an entry still being written must never be mirrored half-formed and its tail silently
    /// dropped. Those two polls used to be two more ticks — so an append reached Telegram in ~6 s, of
    /// which the watcher alone would have saved only the first two.
    ///
    /// <para>
    /// So a wake is one poll plus the settling polls that release what it found, and they run at
    /// <see cref="SETTLING_PULSE_MILLISECONDS"/> instead of the tick. This number is the tailer's, and
    /// if that constant ever moves this one is stale — the end-to-end guard is
    /// <c>AChannelAppendWakesTheBridgeTests</c>, which measures the whole path and fails on the
    /// difference rather than leaving it to be noticed.
    /// </para>
    /// </summary>
    public const int SETTLING_PULSES = 2;

    /// <summary>
    /// What one arrival may cost the loop, however many events it raised: the wake and its settling
    /// polls, and nothing per event. A burst that cost one tick each would be the mirror loop spinning
    /// on its own writes.
    /// </summary>
    public const int MAXIMUM_WAKES_PER_BURST = 1 + SETTLING_PULSES;

    /// <summary>
    /// <paramref name="logLine"/> is how a watcher that cannot be started, or that loses events, says
    /// so — one line, into the orchestration log, never Telegram (the owner can do nothing about an
    /// inotify limit) and never stderr, which nothing here reads. A watcher this machine refuses is NOT
    /// an error the bridge propagates: the loop keeps its tick and works exactly as it did before.
    /// </summary>
    public static IChannelChangeWaker Create(string supervisionRoot, Action<string> logLine)
    {
        try
        {
            var watcher = new FileSystemWatcher(supervisionRoot, "*.md")
            {
                IncludeSubdirectories = true,

                // Size AND LastWrite: an append is a size change on every filesystem here, and a write
                // stamp on most — asking for both is what makes the wake independent of which one this
                // platform reports. FileName covers the channel that did not exist yet, which is the
                // first entry of every new orchestration and implementer.
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            return new ChannelChangeWakerModel(watcher, logLine);
        }
        catch (Exception ex)
        {
            logLine(
                $"Channel-change watcher could not be started over '{supervisionRoot}' "
                    + $"({ex.GetType().Name}: {ex.Message}) — the mirror loop keeps its tick, which is exactly "
                    + "the behaviour it had before the watcher existed. Appends are late, never lost.");

            return new ChannelChangeWakerModel(null, logLine);
        }
    }
}
