namespace AIOrchestratorCoreLib.Telegram.TelegramSendBudget;

internal sealed class TelegramSendBudgetModel : ITelegramSendBudget
{
    /// <summary>
    /// One lock for both buckets. Sends arrive from the mirror tick and the inbound batch at the
    /// same time, and a bucket read-modify-written from two threads hands the same token out twice —
    /// which is the burst it exists to prevent. Two locks would buy nothing: neither critical
    /// section does I/O.
    /// </summary>
    readonly Lock _lock = new();

    double _sendTokens;
    DateTime _sendRefilledUtc;

    // EMPTY AT EVERY START, unlike the send bucket, and it is not an oversight. Nothing carries it
    // over because nothing needs to: the app's edit traffic is a steady drip with occasional
    // bursts, so the only thing a full start would buy is the right to fire thirty edits in the
    // first second after a crash loop — the exact runaway the bucket was added to stop. The price
    // is that the first edit of a run waits two seconds.
    double _controlTokens;
    DateTime _controlRefilledUtc = DateTime.UtcNow;

    /// <summary>
    /// WHEN EACH MESSAGE WAS LAST EDITED — a per-message gate, because Telegram throttles edits of
    /// one message far harder than calls to the group (measured 2026-09-10; see
    /// <see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>).
    ///
    /// <para>
    /// BOUNDED BY PRUNING, not by a cap on count. The app edits a handful of long-lived messages —
    /// one PULSE per topic, one dashboard, the receipts — so the natural size is small; what would
    /// grow it without limit is a long run through many closed topics. An entry older than the gate
    /// can never hold anything back, so it is dropped when the map is next touched, which keeps this
    /// bounded by the number of messages edited in the last thirty seconds rather than ever.
    /// </para>
    /// </summary>
    readonly Dictionary<long, DateTime> _lastEditUtcByMessageId = [];

    internal TelegramSendBudgetModel(double sendTokens, DateTime sendRefilledUtc)
    {
        _sendTokens = sendTokens;
        _sendRefilledUtc = sendRefilledUtc;
    }

    public Task Wait_ForMessageEdit_Async(long messageId, CancellationToken cancellationToken)
    {
        return Wait_Async(
            () =>
            {
                var now = DateTime.UtcNow;
                var gap = TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE;

                Prune_StaleEdits(now, gap);

                if (!_lastEditUtcByMessageId.TryGetValue(messageId, out var lastEdit))
                {
                    // FIRST EDIT OF THIS MESSAGE GOES STRAIGHT OUT. The gate is about a REPEATED edit
                    // of the same message; making the first one wait would delay every status line by
                    // half a minute after every restart for nothing.
                    _lastEditUtcByMessageId[messageId] = now;

                    return TimeSpan.Zero;
                }

                var elapsed = now - lastEdit;

                if (elapsed >= gap)
                {
                    _lastEditUtcByMessageId[messageId] = now;

                    return TimeSpan.Zero;
                }

                // THE STAMP MOVES TO WHEN THIS EDIT WILL ACTUALLY GO OUT, not to now. Recording `now`
                // would let a second caller that arrives during the wait compute its own gap from a
                // moment already spent, and two waiters would both fire at the end of one gap.
                var wait = gap - elapsed;

                _lastEditUtcByMessageId[messageId] = lastEdit + gap;

                return wait;
            },
            cancellationToken);
    }

    /// <summary>
    /// Drops entries that can no longer hold anything back. Called under the lock, from the one
    /// method that touches the map.
    /// </summary>
    void Prune_StaleEdits(DateTime now, TimeSpan gap)
    {
        if (_lastEditUtcByMessageId.Count == 0)
            return;

        List<long>? expired = null;

        foreach (var (messageId, lastEdit) in _lastEditUtcByMessageId)
        {
            if (now - lastEdit >= gap)
                (expired ??= []).Add(messageId);
        }

        if (expired == null)
            return;

        foreach (var messageId in expired)
            _lastEditUtcByMessageId.Remove(messageId);
    }

    public Task Wait_ForSend_Async(CancellationToken cancellationToken)
    {
        return Wait_Async(
            () =>
            {
                var (tokens, refilledUtc, wait) = TokenBucket_Gate.Take(_sendTokens, _sendRefilledUtc, DateTime.UtcNow);
                _sendTokens = tokens;
                _sendRefilledUtc = refilledUtc;
                return wait;
            },
            cancellationToken);
    }

    public Task Wait_ForControl_Async(CancellationToken cancellationToken)
    {
        return Wait_Async(
            () =>
            {
                var (tokens, refilledUtc, wait) = TokenBucket_Gate.Take(
                    _controlTokens, _controlRefilledUtc, DateTime.UtcNow,
                    TokenBucket_Gate.CONTROL_CAPACITY, TokenBucket_Gate.DEFAULT_REFILL_SECONDS);

                _controlTokens = tokens;
                _controlRefilledUtc = refilledUtc;
                return wait;
            },
            cancellationToken);
    }

    public (double Tokens, DateTime RefilledUtc) Read_SendState()
    {
        lock (_lock)
            return (_sendTokens, _sendRefilledUtc);
    }

    /// <summary>
    /// Recomputed after each sleep rather than sleeping the whole predicted wait in one go: several
    /// senders race here, and the one that wakes first should take the token that actually became
    /// available, not the one that was promised to it a second ago.
    /// </summary>
    async Task Wait_Async(Func<TimeSpan> take, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;

            lock (_lock)
                wait = take();

            if (wait <= TimeSpan.Zero)
                return;

            await Task.Delay(wait, cancellationToken);
        }
    }
}
