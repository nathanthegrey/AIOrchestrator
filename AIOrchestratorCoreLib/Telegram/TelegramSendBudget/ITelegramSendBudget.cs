namespace AIOrchestratorCoreLib.Telegram.TelegramSendBudget;

/// <summary>
/// THE TWO OUTBOUND ALLOWANCES, held outside the HTTP client so they can be persisted and tested.
///
/// <para>
/// One bucket for calls that CREATE A MESSAGE (Telegram's published twenty-a-minute ceiling) and a
/// second, larger one for edits, deletes and <c>answerCallbackQuery</c> — see
/// <see cref="TokenBucket_Gate"/> for both derivations, and for why the second one had to exist at
/// all (brief F5: it was not "on a different bucket", it was on none).
/// </para>
/// <para>
/// A COMPONENT RATHER THAN TWO FIELDS ON THE CLIENT, for one reason: the SEND bucket survives a
/// restart. It is written into <c>.bridge-state.json</c> beside the offsets the bridge already
/// rewrites on every tick, and restored on start — so a process that died after spending eight
/// tokens resumes with two, and a crash loop cannot mint a fresh burst each time it comes up. That
/// needs the state to be readable from outside the client, which a private field is not.
/// </para>
/// </summary>
public interface ITelegramSendBudget
{
    /// <summary>Blocks until a MESSAGE-CREATING call may go out.</summary>
    Task Wait_ForSend_Async(CancellationToken cancellationToken);

    /// <summary>Blocks until an EDIT, DELETE or callback answer may go out.</summary>
    Task Wait_ForControl_Async(CancellationToken cancellationToken);

    /// <summary>
    /// Blocks until THIS MESSAGE may be edited again — a per-message gate, not a bucket.
    ///
    /// <para>
    /// Telegram throttles edits of one message far harder than calls to the group, so the control
    /// bucket cannot express this however it is sized: two topics editing once a minute each is two
    /// calls a minute, and no group-level allowance would hold that back. Measured on 2026-09-10 —
    /// see <see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/> for the journal.
    /// </para>
    /// <para>
    /// NOT PERSISTED, like the control bucket and for the same reason: after a restart there is no
    /// message this process has recently edited, and the first edit of each is the one that matters.
    /// </para>
    /// </summary>
    Task Wait_ForMessageEdit_Async(long messageId, CancellationToken cancellationToken);

    /// <summary>
    /// The send bucket as it stands, for persistence. The CONTROL bucket is deliberately not here:
    /// it starts empty at every start by design, so there is nothing about it worth carrying over.
    /// </summary>
    (double Tokens, DateTime RefilledUtc) Read_SendState();
}
