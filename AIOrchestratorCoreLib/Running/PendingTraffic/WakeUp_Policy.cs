using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.Running.PendingTraffic;

/// <summary>
/// WHETHER THIS PENDING SET IS WORTH A TURN AT ALL — the question <see cref="CoalesceWindow_Policy"/>
/// does not ask. That one decides how long a set waits for the rest of itself; this one decides
/// whether the set starts a turn now or is held for the digest.
///
/// <para>
/// MEASURED ON THE VPS, 6–9 Sep 2026: an orchestration supervisor was woken ~400 times in the window,
/// 247 of them by MEMBER traffic, at 2.5 model calls and a mean 398 k of context per call — one
/// wake-up is on the order of 1 M input tokens, and 15 % of those turns produced less than 600 output
/// tokens (an acknowledgement). The supervisor is the most expensive role in the system and the last
/// one still resuming a transcript, so cutting the NUMBER of its wake-ups is the one large saving
/// available without touching its memory. Spec §C4 of
/// <c>docs/superpowers/specs/2026-09-08-token-efficiency-design.md</c>.
/// </para>
/// <para>
/// THE RULE IN THREE LINES. The owner wakes it now. A member's ordinary report waits for the digest —
/// at most <c>IRunnerConfigs.MemberDigestWindow</c>, so several members' reports ride ONE turn instead
/// of buying one each. The app's own bookkeeping never wakes it and cannot reach this at all
/// (<see cref="PrintTurn_Trigger.Is_Inbound"/> excludes <see cref="ChannelAuthors.App"/>, so a
/// <c>turn_ended</c>, a <c>STATUS</c>, a ledger advisory or an orphan notice is never pending for
/// anybody) — measured: zero turns in the whole window were triggered by app entries alone.
/// </para>
/// <para>
/// NOTHING IS LOST BY BEING HELD, and that is what makes the digest cheap rather than risky. A turn
/// takes EVERY pending entry of every channel, so a held member report rides the very next turn
/// whatever starts it — the owner's next message included. The owner therefore never waits for a
/// digest, and a member's answer to the owner cannot be delayed by one: the entry that would carry it
/// is on the same turn as the owner traffic that asked for it.
/// </para>
/// <para>
/// A MEMBER THAT IS BLOCKED IS NOT HELD. <see cref="ESCALATION_MARKERS"/> is the vocabulary a member
/// has for "I cannot go on without you", and an entry carrying one starts a turn at once. The markers
/// were read out of the kit rather than invented, and two of the three the brief proposed do not
/// exist in this direction — see that field.
/// </para>
/// <para>
/// A POLICY AND NOT AN <c>if</c> IN THE DISPATCHER, for the reason <see cref="CoalesceWindow_Policy"/>
/// gives: the decision has one line per reason, the reasons are arguable from measurements, and they
/// have to be testable without a clock, a state file or a session.
/// </para>
/// </summary>
public static class WakeUp_Policy
{
    /// <summary>
    /// WHAT A MEMBER WRITES WHEN IT CANNOT GO ON, and therefore what is never digested. Verified in
    /// the kit on 2026-09-09 rather than assumed:
    ///
    /// <para>
    /// <c>BLOCKED ON OWNER</c> is real and is a member's marker — <c>kit/skills/implementer/SKILL.md</c>
    /// ("Blocked on the owner? … phrase it as <c>BLOCKED ON OWNER</c> plus the question and options")
    /// and <see cref="MemberState_Resolver.BLOCKED_ON_OWNER_MARKER"/>, which is where the word lives.
    /// A member writing it has stopped and a PERSON is at the end of the chain, so five minutes of
    /// digest would be five minutes of two sessions waiting.
    /// </para>
    /// <para>
    /// <c>QUESTION</c> is the SUPERVISOR's vocabulary for asking the OWNER
    /// (<c>kit/skills/supervisor/SKILL.md</c>, and <c>Bridge.Decisions.OwnerQuestion_Contract</c> owns
    /// the word); the member skills never teach it, because a member's only interlocutor is its
    /// supervisor. It is honoured anyway, and spelled here rather than referenced because
    /// <c>Running</c> does not depend on <c>Bridge</c> and must not start to for one word: if a member
    /// does write it, the entry is a question and waking is both the cheap and the safe direction.
    /// </para>
    /// <para>
    /// TWO MARKERS THE BRIEF NAMED ARE NOT HERE, and their absence is a finding rather than an
    /// omission. The <c>turn stalled</c> subject is written by the APP
    /// (<see cref="PrintTurn_Words.TURN_STALLED_SUBJECT"/>, author <see cref="ChannelAuthors.App"/>),
    /// so it is not inbound for any role and can never reach this method. And <c>WAITING ON</c> is not
    /// protocol vocabulary at all: the real marker in that area is
    /// <see cref="MemberState_Resolver.STANDING_BY_MARKER"/>, which declares the OPPOSITE — nothing
    /// owed, nothing running — and is the best digest candidate there is.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ESCALATION_MARKERS =
    [
        MemberState_Resolver.BLOCKED_ON_OWNER_MARKER,
        "QUESTION",
    ];

    /// <summary>
    /// Whether anything in this set is a member entry the digest may hold. The dispatcher asks it to
    /// decide when to START its hold clock, which is why it is a separate question from
    /// <see cref="Resolve_WakeReason_OrNull"/>: the clock must be set on the tick the FIRST such entry
    /// appears and left alone afterwards, or a crew filing a report every minute would push its own
    /// deadline out for ever.
    /// </summary>
    public static bool Contains_DigestableTraffic(IReadOnlyList<PendingEntry> pending)
    {
        foreach (var item in pending)
        {
            if (Is_Digestable(item))
                return true;
        }

        return false;
    }

    /// <summary>
    /// WHY THIS SET IS WORTH A TURN NOW, or null to hold it for the digest. A reason rather than a
    /// bool because it is logged: a supervisor turn that did not happen is invisible, and the one
    /// line saying which rule held it is what makes the digest auditable instead of a shrug.
    /// </summary>
    /// <param name="digestHeldSince">
    /// When member traffic first became pending for this session — the dispatcher's own stamp, not a
    /// header's (CLAUDE.md decision 12: a channel header's date is agent-written and untrusted).
    /// NULL means nothing recorded a hold, which is a state this cannot reason from, so it WAKES:
    /// today's behaviour is the safe direction, and a set held on a stamp nobody wrote would be held
    /// for ever.
    /// </param>
    public static string? Resolve_WakeReason_OrNull(IReadOnlyList<PendingEntry> pending, DateTime? digestHeldSince, DateTime nowLocal, TimeSpan digestWindow)
    {
        // NOTHING PENDING REACHES HERE ONLY AS THE BOOT TURN — the dispatcher returns before this for
        // every other empty set (PrintTurnDispatcherModel.Needs_BootTurn). The greeting it produces is
        // what creates the orchestration's Telegram topic, so holding it would hold the owner's own
        // way in.
        if (pending.Count == 0)
            return "boot turn";

        foreach (var item in pending)
        {
            if (item.Entry.Author == ChannelAuthors.Owner)
                return $"the owner wrote in '{item.Source.Key}'";
        }

        foreach (var item in pending)
        {
            var marker = Find_EscalationMarker_OrNull(item);

            if (marker != null)
                return $"'{item.Source.Key}' wrote {marker}";
        }

        // ANYTHING THAT IS NOT A MEMBER'S ORDINARY ENTRY WAKES AS IT ALWAYS DID. This is what confines
        // the digest to the supervisor without a role test anywhere: a member's inbound authors are its
        // supervisor and the owner (PrintTurn_Trigger.Is_Inbound), so a brief or a verdict is never
        // digestable and every other role's timing is untouched.
        foreach (var item in pending)
        {
            if (!Is_Digestable(item))
                return $"'{item.Source.Key}' carries traffic the digest does not hold";
        }

        if (digestWindow <= TimeSpan.Zero)
            return "the member digest is off";

        if (digestHeldSince == null)
            return "no hold was recorded for this traffic";

        var held = nowLocal - digestHeldSince.Value;

        // A HOLD THAT READS AS NEGATIVE IS A CLOCK THAT MOVED, NEVER A REASON TO WAIT LONGER. Same
        // ruling as Describe_SinceStamp_OrNull's (CLAUDE.md decision 12): a duration that cannot be
        // believed must not turn into a confident wrong wait, and here the honest direction is to
        // deliver rather than to hold traffic until the clock catches up.
        if (held < TimeSpan.Zero)
            return "the hold stamp is in the future, so the digest cannot be timed";

        return held >= digestWindow
            ? $"the {Describe_Window(digestWindow)} member digest elapsed ({Describe_Window(held)} held)"
            : null;
    }

    /// <summary>
    /// An entry the digest may hold: written by a MEMBER — <see cref="ChannelAuthor_Kinds.Is_Member"/>,
    /// so reviewers and a solo count exactly as implementers do — and carrying none of
    /// <see cref="ESCALATION_MARKERS"/>.
    /// </summary>
    static bool Is_Digestable(PendingEntry item)
    {
        return ChannelAuthor_Kinds.Is_Member(item.Entry.Author) && Find_EscalationMarker_OrNull(item) == null;
    }

    /// <summary>
    /// The escalation marker this entry declares, or null.
    ///
    /// <para>
    /// THE MATCH IS <see cref="MemberState_Resolver.Contains_Marker"/>'S AND IS NOT REWRITTEN HERE.
    /// Every rule in it — the whole-token test, decoration stripped by category so "🚩 BLOCKED ON
    /// OWNER" counts, quotation excluded so a brief QUOTING the marker does not declare it, subject
    /// anywhere but body only at the start of a line — was paid for by a live failure, and that method
    /// says in as many words that a second matcher written for a new marker starts out missing all of
    /// them. This is its third consumer.
    /// </para>
    /// </summary>
    static string? Find_EscalationMarker_OrNull(PendingEntry item)
    {
        foreach (var marker in ESCALATION_MARKERS)
        {
            if (MemberState_Resolver.Contains_Marker(item.Entry, marker))
                return marker;
        }

        return null;
    }

    static string Describe_Window(TimeSpan window)
    {
        return window.TotalMinutes >= 1
            ? $"{window.TotalMinutes:0.#} min"
            : $"{window.TotalSeconds:0.#} s";
    }
}
