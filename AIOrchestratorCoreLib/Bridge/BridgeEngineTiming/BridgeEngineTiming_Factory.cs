using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;

public static class BridgeEngineTiming_Factory
{
    /// <summary>How long the mirror loop sleeps between ticks.</summary>
    /// <summary>
    /// The CEILING on how long an append can go unnoticed — not the rate the loop runs at. It stays
    /// 2000 because it is the safety net: <see cref="ChannelChangeWaker.IChannelChangeWaker"/> ends the
    /// wait early when the filesystem says a channel was written, and filesystem notification is
    /// best-effort everywhere (inotify runs out of watches on a Linux box with many folders, a network
    /// filesystem reports nothing, a container can have no backend at all). Where the watcher never
    /// fires, the loop behaves EXACTLY as it did before it existed.
    /// </summary>
    const int MIRROR_TICK_MILLISECONDS = 2000;

    /// <summary>
    /// How long a message waits before it is delivered, so a burst of texts arrives as ONE turn.
    ///
    /// FOUR SECONDS WAS TOO SHORT TO BE HELD. WAIT can only stop a message that is still in the
    /// buffer, and four seconds is less than it takes to realise you have more to say and type a
    /// word — measured on the owner's machine, a WAIT five seconds behind its message arrived after
    /// the take and stopped nothing.
    ///
    /// SIX, because the ⏸ button changed what the window has to be long enough FOR. It went to eight
    /// while holding meant typing; with a tap sitting under the receipt the owner set it back down
    /// themselves (2026-08-15) — "with the button we can reduce the window". The number is a balance
    /// between how long a hold takes to express and how long every message waits, and the button
    /// moved the first half of that.
    /// </summary>
    /// <summary>
    /// The owner often texts several messages in a row — quiet time before delivery as ONE entry, so a
    /// burst arrives on the session as one turn instead of one turn each.
    ///
    /// <para>
    /// FOUR SECONDS WAS TOO SHORT TO BE HELD: WAIT can only stop a message still in the buffer, and
    /// four seconds is less than it takes to realise you have more to say and type a word — measured on
    /// the owner's machine, a WAIT five seconds behind its message arrived after the take and stopped
    /// nothing. It went to eight. SIX, because the ⏸ button changed what the window has to be long
    /// enough FOR: with a tap sitting under the receipt the owner set it back down themselves
    /// (2026-08-15) — "with the button we can reduce the window".
    /// </para>
    /// <para>
    /// THREE, and the balance changed again on 2026-09-09 because the window is no longer served by
    /// everybody. Measured on the VPS that day: 11–12 s median from the owner's text to the entry
    /// landing in the supervisor's channel, six of them here — and the owner asked for the wait to
    /// shrink. A message that is plainly over now skips this window ENTIRELY
    /// (<see cref="OwnerMessageComplete_Decider"/>, asked below the ⏸ check so a hold still stops
    /// everything), which leaves the window covering only what it was ever for: a burst of typing that
    /// has not finished yet.
    /// </para>
    /// </summary>
    const int OWNER_AGGREGATION_SECONDS = 3;

    /// <summary>
    /// Pause before re-sending a channel whose mirror send failed. The tailer re-emits an
    /// unconfirmed append on EVERY poll — that is what makes the retry possible — so without this
    /// the retry would be a 2-second hammer against an endpoint that is already failing, which is
    /// precisely the shape that earns a bot a server-side throttle.
    /// </summary>
    const int MIRROR_RETRY_BACKOFF_SECONDS = 30;

    /// <summary>THE SHIPPED NUMBERS. Every production caller gets these, and nothing else may.</summary>
    public static IBridgeEngineTiming Create_Production()
    {
        return new BridgeEngineTimingModel(
            MIRROR_TICK_MILLISECONDS,
            OWNER_AGGREGATION_SECONDS,
            MIRROR_RETRY_BACKOFF_SECONDS,
            (int)ChannelWrite_Lock.DEFAULT_TICK_ALLOWANCE.TotalMilliseconds);
    }

    /// <summary>
    /// THE TEST SEAM. A test that drives the real engine loop pays these periods in wall-clock
    /// sleep; handing in shorter ones buys the run back without changing a single thing the test
    /// asserts. The guards below are the production floors the rest of the engine relies on: a
    /// non-positive tick is a hot loop, and <c>OwnerDeliveryBuffer_Factory</c> refuses an
    /// aggregation window under a second.
    /// </summary>
    public static IBridgeEngineTiming Create_Custom(
        int mirrorTickMilliseconds,
        int ownerAggregationSeconds,
        int mirrorRetryBackoffSeconds,
        int tickLockAllowanceMilliseconds)
    {
        if (mirrorTickMilliseconds < 1)
            throw new ArgumentException($"mirrorTickMilliseconds must be >= 1, got {mirrorTickMilliseconds}");

        if (ownerAggregationSeconds < 1)
            throw new ArgumentException($"ownerAggregationSeconds must be >= 1, got {ownerAggregationSeconds}");

        if (mirrorRetryBackoffSeconds < 1)
            throw new ArgumentException($"mirrorRetryBackoffSeconds must be >= 1, got {mirrorRetryBackoffSeconds}");

        // ONE, NOT ZERO. An allowance of zero is still an allowance in force, so the guard is not
        // about the mechanism — it is that a tick which may not wait at all cannot distinguish a
        // contended channel from a free one on a loaded machine, and every lock test in the suite
        // would start passing for the wrong reason.
        if (tickLockAllowanceMilliseconds < 1)
            throw new ArgumentException($"tickLockAllowanceMilliseconds must be >= 1, got {tickLockAllowanceMilliseconds}");

        return new BridgeEngineTimingModel(
            mirrorTickMilliseconds,
            ownerAggregationSeconds,
            mirrorRetryBackoffSeconds,
            tickLockAllowanceMilliseconds);
    }
}
