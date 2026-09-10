using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// WHICH QUESTION THE OWNER STILL OWES AN ANSWER TO — by entry index, or none.
///
/// <para>
/// WHAT THIS REPLACES, AND WHY. The rule was "the session spoke last, so the owner owes a reply",
/// and the ⚠️ stall alert was built on it. On 2026-09-09 that produced, on the owner's phone:
/// *"⚠️ … has been waiting on your reply for 25 min"* at 17:30 — thirty minutes after the
/// supervisor had written **"Nothing more needed from you"** — then again at 18:38, when the
/// supervisor was not waiting for anything but PAUSED for a usage limit, and again at 20:25. On a
/// second topic, three more. Every one of them was false, and each was indistinguishable from a real
/// one, which is what makes a false alert expensive: it teaches the owner to ignore the true ones.
/// </para>
/// <para>
/// THE OWNER'S RULING, 2026-09-09: the alert fires *"only when the supervisor's last owner-channel
/// entry is a real question (`QUESTION:` block or `BLOCKED ON OWNER`), once per question"*. "The
/// session spoke last" is not a debt — a supervisor reporting progress, saying goodnight, or saying
/// that nothing is needed has asked for nothing.
/// </para>
/// <para>
/// IT RETURNS THE INDEX, not a bool, and that is the "once per question" half of the ruling: the
/// caller remembers WHICH question it has already alerted about, so a second alert needs a second
/// question rather than merely more silence. A bool could only ever be re-armed by traffic, which is
/// how the old alert managed to fire three times about the same nothing.
/// </para>
/// <para>
/// APP ENTRIES ARE SKIPPED, unchanged from the rule this replaces: the app is not a participant in
/// this conversation, and it writes precisely when someone has been waiting. Counting one as the
/// last word let the app's own status push silence the alert — a feature quietly disabling itself.
/// </para>
/// </summary>
public static class OwnerOwesReply_Decider
{
    /// <summary>
    /// The index of the supervisor entry the owner owes an answer to, or null when they owe nothing.
    ///
    /// <para>
    /// Scanning stops at the first entry that settles the question: an OWNER entry means they have
    /// spoken since (no debt), and the first entry that SPEAKS TO THE OWNER is the one whose shape
    /// decides it. An earlier question behind a later plain report is not revived — the supervisor
    /// moved on, and the owner is answering the conversation, not an archive.
    /// </para>
    /// </summary>
    public static int? Find_UnansweredQuestionIndex_OrNull(IReadOnlyList<IChannelEntry> ownerChannelEntries)
    {
        for (var i = ownerChannelEntries.Count - 1; i >= 0; i--)
        {
            var entry = ownerChannelEntries[i];

            if (entry.Author == ChannelAuthors.Owner)
                return null;

            if (!ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author))
                continue;

            return Is_RealQuestion(entry.RawText) ? entry.Index : null;
        }

        return null;
    }

    /// <summary>
    /// A REAL QUESTION IS DECLARED, not inferred. `QUESTION:` and `BLOCKED ON OWNER` are the two
    /// shapes every role command teaches for "I cannot go on without you", and they are the two the
    /// owner named.
    ///
    /// <para>
    /// PROSE ENDING IN '?' IS DELIBERATELY NOT ENOUGH here, though
    /// <see cref="OwnerPush_Policy.Asks_InProse"/> treats it as enough for PUSHING. The two
    /// decisions are not the same: pushing an ask-shaped line costs one message the owner can
    /// ignore, while alerting on one costs a ⚠️ every 25 minutes about a rhetorical question. The
    /// owner's own ruling on the topic glyph (2026-08-25) is the same distinction: *"it's not that
    /// it should be interpreted indirectly based on the presence of a ? here and there that could
    /// mean anything."*
    /// </para>
    /// </summary>
    public static bool Is_RealQuestion(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        return rawEntryText.Contains(OwnerPush_Policy.QUESTION_MARKER, StringComparison.Ordinal)
            || rawEntryText.Contains(OwnerPush_Policy.BLOCKED_MARKER, StringComparison.OrdinalIgnoreCase);
    }
}
