namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHETHER ONE OWNER MESSAGE READS AS FINISHED — the rule that decides whether they wait at all.
///
/// <para>
/// Measured on the VPS on 2026-09-09: an owner's Telegram message took 11–12 s median to reach the
/// supervisor's channel, and six of those seconds were the aggregation window
/// (<c>OWNER_AGGREGATION_SECONDS</c>). Every message served that quiet period so that the occasional
/// burst would arrive as ONE turn. The owner's ruling that day was to shrink the window to three
/// seconds and to skip it altogether for a message that is plainly over: one ending in <c>.</c>,
/// <c>?</c> or <c>!</c>, or one that is a slash command.
/// </para>
/// <para>
/// A CLASS OF ITS OWN, not an <c>if</c> inside the buffer, because this is the half of the change an
/// owner can argue with. "Finished" is a judgement about how they type — the window's own doc comment
/// has been re-argued three times (eight seconds, then six with the ⏸ button, now three) — and it
/// must be readable and changeable without touching a lock-guarded class around it.
/// </para>
/// <para>
/// IT DOES NOT DECIDE "SINGLE". This answers a question about ONE text;
/// <see cref="OwnerDeliveryBuffer.IOwnerDeliveryBuffer"/> is what knows whether that text is the only
/// one buffered for its target. Two finished sentences arriving in a burst still serve the window,
/// because the second is evidence that the first was not the whole thought.
/// </para>
/// <para>
/// AND IT DOES NOT OVERRIDE ⏸. A hold is checked BEFORE this rule is ever asked (see
/// <c>OwnerDeliveryBufferModel.Is_Ready</c>): a finished sentence escaping a hold would be the silent
/// lapse of 2026-08-20 arriving by a new route.
/// </para>
/// </summary>
public static class OwnerMessageComplete_Decider
{
    /// <summary>
    /// True when this text may be delivered with no quiet period at all.
    ///
    /// <para>
    /// Trailing whitespace is typing, not meaning, so it is trimmed before the last character is read
    /// — Telegram preserves the newline a phone keyboard leaves behind. An empty text is NOT complete,
    /// and the reason is not tidiness: it has no last character, so answering true here would be
    /// answering from an index that does not exist.
    /// </para>
    /// </summary>
    public static bool Is_Complete(string text)
    {
        var trimmed = text.AsSpan().Trim();

        if (trimmed.Length == 0)
            return false;

        // A SLASH COMMAND IS FINISHED BY CONSTRUCTION — a word the app parses, never the opening of a
        // thought. STARTS with one, deliberately: "2/3 of the tests pass" and "check /status when you
        // can" are unfinished sentences that happen to contain a slash.
        if (trimmed[0] == '/')
            return true;

        return trimmed[^1] is '.' or '?' or '!';
    }
}
