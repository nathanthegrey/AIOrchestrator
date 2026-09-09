namespace AIOrchestratorCoreLib.Bridge.ChannelChangeWaker;

internal sealed class ChannelChangeWakerModel : IChannelChangeWaker
{
    readonly FileSystemWatcher? _watcher;
    readonly Action<string> _logLine;

    readonly object _gate = new();

    /// <summary>
    /// The latch a write completes. REPLACED after every wake rather than reset, because a
    /// <see cref="TaskCompletionSource"/> cannot be un-completed and a waiter reading the old one
    /// while a new one is installed would miss the very write it is waiting for.
    /// </summary>
    TaskCompletionSource _signal = New_Signal();

    int _settlingPulsesOwed;
    bool _reportedAnError;

    internal ChannelChangeWakerModel(FileSystemWatcher? watcher, Action<string> logLine)
    {
        _logLine = logLine;
        _watcher = watcher;

        if (_watcher == null)
            return;

        _watcher.Changed += On_ChannelWritten;
        _watcher.Created += On_ChannelWritten;
        _watcher.Renamed += On_ChannelWritten;

        // Deleted too, and not for the deletion's sake: compaction MOVES a channel's older half into a
        // sibling .archive.md, and the loop reading the survivors one tick sooner is the same win.
        _watcher.Deleted += On_ChannelWritten;

        _watcher.Error += On_WatcherError;

        try
        {
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            // WHERE THE REAL FAILURES LAND. Constructing the watcher only checks the folder; arming it
            // is what asks the OS for a notification handle, so an exhausted inotify budget or a
            // filesystem that cannot watch throws HERE, not in the factory. Same contract: say it once,
            // keep the tick, never take the mirror loop down for an optimisation.
            _logLine(
                $"Channel-change watcher could not be armed ({ex.GetType().Name}: {ex.Message}) — the mirror "
                    + "loop keeps its tick, which is exactly the behaviour it had before the watcher existed. "
                    + "Appends are late, never lost.");

            _watcher.Dispose();
            _watcher = null;
        }
    }

    public async Task Wait_ForChangeOrTick_Async(int tickMilliseconds, CancellationToken cancellationToken)
    {
        if (_watcher == null)
        {
            await Task.Delay(tickMilliseconds, cancellationToken);
            return;
        }

        if (Take_SettlingPulse())
        {
            // Never LONGER than the tick it stands in for: a caller ticking faster than the settling
            // gap must not be slowed down by the thing that exists to speed it up.
            await Task.Delay(Math.Min(ChannelChangeWaker_Factory.SETTLING_PULSE_MILLISECONDS, tickMilliseconds), cancellationToken);
            return;
        }

        Task wake;

        lock (_gate)
            wake = _signal.Task;

        using var tickCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var tick = Task.Delay(tickMilliseconds, tickCancellation.Token);

        if (await Task.WhenAny(tick, wake) == tick)
        {
            // Awaited rather than returned from: if what ended the tick was the token, this is the
            // OperationCanceledException the loop above already catches and ends on.
            await tick;
            return;
        }

        // The timer is no longer wanted, and it is OBSERVED as well as cancelled: an abandoned
        // Task.Delay faults on its way out and an unread fault is an UnobservedTaskException raised
        // from the finalizer thread, minutes later, blamed on whatever is running then.
        tickCancellation.Cancel();

        try
        {
            await tick;
        }
        catch (OperationCanceledException)
        {
            // The cancellation this method just asked for.
        }

        // THE DEBOUNCE, and it is why the latch is replaced AFTER it rather than before: every event
        // raised during this pause lands on the latch that is already set, so the whole burst — the
        // several notifications one append raises, and the several appends one crew makes — is one
        // wake. Without it the loop would run a tick per event, on its own writes as well as theirs.
        await Task.Delay(ChannelChangeWaker_Factory.DEBOUNCE_MILLISECONDS, cancellationToken);

        lock (_gate)
        {
            _signal = New_Signal();
            _settlingPulsesOwed = ChannelChangeWaker_Factory.SETTLING_PULSES;
        }
    }

    public void Dispose()
    {
        if (_watcher == null)
            return;

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        catch (Exception ex)
        {
            // A watcher that will not shut down cleanly is not worth the loop it is being disposed
            // alongside — this runs while the bridge is already stopping.
            _logLine($"Channel-change watcher did not dispose cleanly ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    void On_ChannelWritten(object sender, FileSystemEventArgs args)
    {
        lock (_gate)
            _signal.TrySetResult();
    }

    /// <summary>
    /// The watcher's own failure path — an overflowed event buffer, or a watch the OS dropped. It may
    /// not throw: this runs on a thread pool thread the bridge does not own, and an exception here
    /// would take the process down rather than the loop.
    /// <para>
    /// ONCE PER WAKER. The condition that raises it persists, and a line every burst would bury the log
    /// it is written into. What follows a lost event is not a lost append — it is a late one, found by
    /// the tick.
    /// </para>
    /// </summary>
    void On_WatcherError(object sender, ErrorEventArgs args)
    {
        try
        {
            // A dropped event means the loop no longer knows the file changed, so the wake is raised
            // anyway: one extra tick costs nothing and covers whatever the watcher missed.
            lock (_gate)
                _signal.TrySetResult();

            if (_reportedAnError)
                return;

            _reportedAnError = true;

            var exception = args.GetException();

            _logLine(
                $"Channel-change watcher reported an error ({exception.GetType().Name}: {exception.Message}) — "
                    + "appends it missed are found by the 2 s mirror tick, which is the behaviour the loop had "
                    + "before the watcher existed. Said once: the condition persists and the line would repeat.");
        }
        catch
        {
            // Nothing above may escape onto a pool thread. There is no second reporting route to try:
            // the tick is the safety net and it is already in place.
        }
    }

    static TaskCompletionSource New_Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    bool Take_SettlingPulse()
    {
        lock (_gate)
        {
            if (_settlingPulsesOwed <= 0)
                return false;

            _settlingPulsesOwed--;
            return true;
        }
    }
}
