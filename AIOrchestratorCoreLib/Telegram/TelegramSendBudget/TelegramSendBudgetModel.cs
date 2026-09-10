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

    internal TelegramSendBudgetModel(double sendTokens, DateTime sendRefilledUtc)
    {
        _sendTokens = sendTokens;
        _sendRefilledUtc = sendRefilledUtc;
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
