using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Decides what actually reaches the owner's PHONE. Everything the supervisor writes still lands in
/// owner-channel.md and is readable in the app — this only governs the push.
///
/// The owner, after a transcript of running commentary: "I answer the sup a question, and then the
/// sup doesn't disturb me anymore unless it has another question. A brief every 30 minutes about
/// how the work is going is fine, but not the waterfall of messages I get now."
///
/// So the phone gets exactly three things:
///   - a QUESTION (it needs them to decide, and it stops the conversation until they do),
///   - the ANSWER to something they asked (they are waiting for it),
///   - a BLOCKED flag (work has stopped and only they can restart it).
/// Progress narration is not one of them. It is not lost; it is simply not a notification.
/// </summary>
public static class OwnerPush_Policy
{
    /// <summary>
    /// Written by the supervisor when it needs a decision — rendered as tappable buttons. The word
    /// itself lives with the rest of the channel vocabulary (<see cref="MemberState_Resolver.QUESTION_MARKER"/>);
    /// this name stays because this file's readers are about the owner's phone, not about member state.
    /// </summary>
    public const string QUESTION_MARKER = MemberState_Resolver.QUESTION_MARKER;
    public const string OPTION_MARKER = "OPTION:";

    /// <summary>Work has stopped and only the owner can restart it.</summary>
    public const string BLOCKED_MARKER = "BLOCKED ON OWNER";

    /// <summary>A picture for the owner, uploaded as a photo. See <see cref="Carries_FileForTheOwner"/>.</summary>
    public const string IMAGE_MARKER = "IMAGE:";

    /// <summary>A file for the owner, uploaded as a document. See <see cref="Carries_FileForTheOwner"/>.</summary>
    public const string ATTACH_MARKER = "ATTACH:";

    /// <summary>
    /// The one-line greeting a session writes as it boots — "supervisor online — …", "solo online
    /// — …". A FOURTH thing the phone gets, and the newest, because the owner cannot use this system
    /// without it (2026-08-25): *"At the start of a new session I don't receive a message from the
    /// sup/solo telling me it's online and ready, so I don't know when I can start writing. Absurdly
    /// I receive a message from the impl saying it's online … I should receive Sup Online, or Solo
    /// Online, and I also want Rev1 Online. In short, I want to know that the sessions are ready."*
    ///
    /// It IS progress narration by shape — no question, no marker — so the filter below killed it,
    /// and the role commands made that worse by mandating an EMPTY body, which removes the last
    /// chance of a stray '?' rescuing it. A member's identical greeting reached the phone only
    /// because a spoke channel is not an owner channel and never meets this policy at all.
    ///
    /// It cannot become a waterfall: a session writes it exactly once, at boot.
    /// </summary>
    public const string ONLINE_MARKER = "online";

    /// <summary>
    /// Matched on the SUBJECT, not the raw text, so the word "online" in a sentence is not a
    /// greeting. The subject every role command mandates starts with the speaker and the marker:
    /// `supervisor online — <repo> — <folders>`, `solo online — …`, `rev-1 online`.
    /// </summary>
    public static bool Is_OnlineGreeting(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        var words = subject.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // The marker is the SECOND word at the latest — "supervisor online", "solo online",
        // "rev-1 online", "general supervisor online". Anything further in is prose.
        for (var i = 0; i < words.Length && i < 3; i++)
        {
            if (string.Equals(words[i].TrimEnd(',', '.', ':', ';'), ONLINE_MARKER, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// ownerIsWaitingForAReply: the owner sent something the supervisor has not answered yet, so
    /// THIS entry is that answer and must go through whatever else it contains.
    ///
    /// subject: the entry's subject line, used ONLY for the boot greeting above. Optional so the
    /// callers that genuinely have no subject to offer keep working; the mirror path always passes
    /// it.
    /// </summary>
    public static bool Should_Push(string rawEntryText, bool ownerIsWaitingForAReply, string? subject = null)
    {
        // Their own words, sent back to them with the session's label on, before anything the
        // owner's wait could be waiting FOR — so this comes ahead of the wait, deliberately.
        if (Is_OwnerRestatement(rawEntryText))
            return false;

        if (ownerIsWaitingForAReply)
            return true;

        if (Is_OnlineGreeting(subject))
            return true;

        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        return Carries_Question(rawEntryText)
            || Asks_InProse(rawEntryText)
            || Carries_FileForTheOwner(rawEntryText)
            || rawEntryText.Contains(BLOCKED_MARKER, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A FIFTH thing the phone gets: an entry carrying a file for the owner — a screenshot
    /// (<c>IMAGE:</c>) or a document (<c>ATTACH:</c>).
    ///
    /// <para>
    /// A FILE IS A DELIVERY, NOT NARRATION. The mockups, the CSV, the failing output: the owner has
    /// to LOOK at it, which is the whole reason it was produced, and an upload the phone never
    /// announces is an upload nobody opens. It also cannot become the waterfall this policy exists
    /// to prevent — a session writes a file for the owner rarely, and never on a loop.
    /// </para>
    /// <para>
    /// IT WAS ALREADY BROKEN FOR <c>IMAGE:</c>, silently, and that is why this is a fix rather than
    /// an addition: a screenshot sent as ordinary narration met the four rules above, matched none
    /// of them, and was suppressed with its photo. It only ever arrived when the owner happened to
    /// be waiting for a reply — which is how nobody noticed. <c>ATTACH:</c> (2026-09-07) would have
    /// inherited exactly that, so both markers are named here.
    /// </para>
    /// <para>
    /// MATCHED AT THE START OF A LINE, like the engine's own extractor, and not with
    /// <see cref="string.Contains(string, StringComparison)"/> the way <see cref="Carries_Question"/>
    /// is: only a column-0 marker line actually produces an upload, so a prose mention of "ATTACH:"
    /// must not push an entry that delivers nothing.
    /// </para>
    /// </summary>
    public static bool Carries_FileForTheOwner(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        foreach (var rawLine in rawEntryText.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            foreach (var marker in new[] { IMAGE_MARKER, ATTACH_MARKER })
            {
                if (line.StartsWith(marker, StringComparison.Ordinal) && line.Length > marker.Length && line[marker.Length..].Trim().Length > 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A question asked WITHOUT the markers still has to reach the owner. Relying on the markers
    /// alone made this filter capable of swallowing a real question in prose — the supervisor asks,
    /// the owner never sees it, and both wait forever. So the rule is ASK-SHAPED MEANS PUSH, and
    /// the mark is read outside fenced blocks so a mockup or a snippet is not mistaken for an ask.
    ///
    /// ASK-SHAPED MEANS ENDING A LINE, NOT MERELY CONTAINING THE MARK. `Contains('?')` was the
    /// original rule and it was too loose in a way that only showed once the ❓ topic glyph was
    /// built on this same reader (2026-08-21). The owner, of one topic: *"after I put it in pc mode
    /// a ? icon appeared in the name for no reason"*, and minutes later, of another: *"you just put
    /// ? in the topic name"*. Neither entry asked anything. One said "sits at [?] until they
    /// answer", the other "Investigating the ? glyph" — the mark as a LEDGER MARKER and as a NOUN.
    ///
    /// That shape is not rare: every role command in this kit teaches the `- [ ]`/`- [>]`/`- [?]`
    /// ledger vocabulary, so sessions write `[?]` in ordinary prose constantly. Under the old rule
    /// each such entry pushed the phone AND pinned ❓ on the topic until the owner typed something —
    /// the glyph scan stops only at an OWNER entry, so a single stray mark stuck indefinitely.
    ///
    /// A prose question ends its line with the mark. Anything else has the explicit markers, which
    /// every role command already tells sessions to use when they actually need a decision.
    ///
    /// THE BIAS IS STILL TOWARDS PUSHING, and tightening it does not risk the deadlock the loose
    /// rule was protecting against: an entry this filter suppresses is REMEMBERED, and the engine
    /// releases it once the session and every member have been idle for minutes
    /// (Break_SilentDeadlock_Async). That net is not theoretical — strategy-lab-6's own log carries
    /// it firing on 2026-08-21: *"Everything went idle with an unsent supervisor entry — releasing
    /// it in case it was a question"*. This filter is the fast path; that is the guarantee.
    /// </summary>
    /// <summary>
    /// The session quoting the owner back to the owner — an entry whose whole body is
    /// <c>Owner: "…"</c> and nothing else. It reached the phone as "🔴 Sup: Owner: 'Che ne pensi?…'"
    /// (2026-09-07): a message from the session that says nothing the owner did not just type, and
    /// worse, it spent their wait, so the real answer that followed was narration again. Such an
    /// entry is never pushed, and never counts as the reply they were waiting for.
    ///
    /// Only the bare quotation is caught. A reply that OPENS by quoting them and goes on to answer
    /// has more than one line of body, and is a reply.
    /// </summary>
    public static bool Is_OwnerRestatement(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        var bodyLines = rawEntryText.Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .SkipWhile(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToList();

        if (bodyLines.Count != 1)
            return false;

        return OwnerRestatement_Pattern.IsMatch(bodyLines[0].Trim());
    }

    static readonly System.Text.RegularExpressions.Regex OwnerRestatement_Pattern = new(
        "^(the\\s+)?owner(\\s+(said|says|asked|asks|wrote|writes))?\\s*:\\s*[\"\u201C\u201D'\u2018\u2019\u00AB\u00BB].*[\"\u201C\u201D'\u2018\u2019\u00AB\u00BB]\\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool Asks_InProse(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        var withoutBlocks = Strip_FencedBlocks(rawEntryText);

        foreach (var line in withoutBlocks.Split('\n'))
        {
            // TrimEnd also takes the '\r' of a CRLF channel, and any trailing spaces.
            if (line.TrimEnd().EndsWith('?'))
                return true;
        }

        return false;
    }

    static string Strip_FencedBlocks(string text)
    {
        var parts = text.Split("```");
        var kept = new System.Text.StringBuilder();

        // Even indices are outside fences, odd indices are inside them.
        for (var i = 0; i < parts.Length; i += 2)
            kept.Append(parts[i]);

        return kept.ToString();
    }

    /// <summary>
    /// Added to EVERY question automatically. A question on a phone is compressed to a couple of
    /// lines, so the owner regularly needs the reasoning behind it before they can choose — and
    /// without a button the only way to ask is to type, which defeats the point of tappable options.
    /// </summary>
    public const string MORE_DETAIL_LABEL = "❔ Explain the options";

    /// <summary>
    /// What the SUPERVISOR actually receives when that button is tapped. It is deliberately fuller
    /// than the label: the button is one tap, the instruction behind it has to be unambiguous, and
    /// it must end by re-asking so the decision is not left dangling.
    /// </summary>
    /// <summary>
    /// The way OUT of a question that is not answerable as asked, and the one button that does not
    /// consume the question.
    ///
    /// <para>
    /// "Explain the options" is a re-ask: it spends the buttons and the supervisor asks again. That
    /// is right when the wording was unclear, and wrong when the owner simply wants to discuss the
    /// decision — measured on one topic on 2026-09-07, where the owner answered a question with a
    /// question five times ("what are these methods? are they new?", "which part are you talking
    /// about?") and each time the exchange had to be rebuilt around a question that was no longer
    /// on the phone. Here the question stays put, with its buttons, until they tap one.
    /// </para>
    /// </summary>
    public const string TALK_LABEL = "💬 Let's talk";

    /// <summary>
    /// What the SUPERVISOR receives on that tap. It says explicitly not to re-ask, because the
    /// question it would re-ask is still open — a second copy of a live question is exactly the
    /// waterfall this policy exists to prevent.
    /// </summary>
    public const string TALK_REQUEST =
        "The owner wants to talk this decision through before choosing. Reply in prose, briefly, and "
        + "do NOT ask it again: the question is still on their phone with its buttons live, and it "
        + "closes when they tap one.";

    public const string MORE_DETAIL_REQUEST =
        "Explain this decision before I choose: what each option actually means in practice, what "
        + "differs between them, what it costs to get wrong, and which one you recommend and why. "
        + "Keep it short. Then ask the question again.";

    public static bool Carries_Question(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        return rawEntryText.Contains(QUESTION_MARKER, StringComparison.Ordinal)
            || rawEntryText.Contains(OPTION_MARKER, StringComparison.Ordinal);
    }
}
