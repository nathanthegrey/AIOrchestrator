using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// WHAT WAKES A SUPERVISOR, WHAT WAITS, AND WHAT NEVER WAKES IT (spec §C4).
///
/// <para>
/// Measured on the VPS 6–9 Sep 2026: ~400 supervisor wake-ups in the window, 247 of them member
/// traffic, 2.5 model calls and a mean 398 k context per call — one wake-up is on the order of 1 M
/// input tokens, and 15 % of the turns were acknowledgement-sized. So a member's ordinary report is
/// held for the digest window and several of them ride ONE turn.
/// </para>
/// <para>
/// THESE ARE THE PURE CASES — no clock, no files, no session. The dispatcher's own behaviour is pinned
/// separately in <see cref="MemberTrafficRidesOneDigestedTurnTests"/>, for the reason the coalesce
/// policy's tests give: delete the call in <c>Consider_Session</c> and every test in this file stays
/// green.
/// </para>
/// </summary>
public class WakeUpPolicyTests
{
    static readonly ITurnSource OWNER = TurnSource_Factory.Create_Owner("/o/owner-channel.md");
    static readonly ITurnSource IMP = TurnSource_Factory.Create_Spoke("imp-1", "/o/imp-1/channel.md");
    static readonly ITurnSource REV = TurnSource_Factory.Create_Spoke("rev-2", "/o/rev-2/channel.md");

    static readonly DateTime NOW = new(2026, 9, 9, 10, 5, 0);
    static readonly TimeSpan DIGEST = TimeSpan.FromMinutes(5);

    static IChannelEntry Entry(string author, string subject, string body = "body")
    {
        return ChannelEntry_Parser.Parse_All($"## [1] FROM {author} — 2026-09-09 10:05 — {subject}\n\n{body}\n")[0];
    }

    static string? Resolve(IReadOnlyList<PendingEntry> pending, DateTime? heldSince, DateTime now)
    {
        return WakeUp_Policy.Resolve_WakeReason_OrNull(pending, heldSince, now, DIGEST);
    }

    /// <summary>
    /// THE OWNER'S LATENCY IS UNTOUCHED — the whole point of the change, and the thing it must not
    /// cost. Their message wakes the supervisor on the tick it is seen, digest or no digest.
    /// </summary>
    [Fact]
    public void AnOwnerMessage_WakesTheSupervisorAtOnce()
    {
        var reason = Resolve([new PendingEntry(OWNER, Entry("owner", "restart the crew"))], NOW, NOW);

        Assert.NotNull(reason);
        Assert.Contains("owner", reason);
    }

    /// <summary>
    /// AND THE OWNER TYPING STRAIGHT INTO A SPOKE IS STILL THE OWNER. It is a path the app supports
    /// (a supervisor's sources include every open spoke, and the owner can write into one from the
    /// terminal), and the digest must not decide by channel what it can decide by author.
    /// </summary>
    [Fact]
    public void AnOwnerMessageInASpoke_WakesTheSupervisorAtOnce()
    {
        Assert.NotNull(Resolve([new PendingEntry(IMP, Entry("owner", "do this one first"))], NOW, NOW));
    }

    /// <summary>A member's ordinary report is the 247: held until the window is out.</summary>
    [Fact]
    public void AMemberReport_IsHeldForTheDigestWindow()
    {
        Assert.Null(Resolve([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NOW, NOW.AddMinutes(4)));
    }

    /// <summary>And delivered when it is out — the bound on how late a report can be read.</summary>
    [Fact]
    public void AMemberReport_IsDeliveredWhenTheDigestWindowElapses()
    {
        var reason = Resolve([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NOW, NOW.AddMinutes(5));

        Assert.NotNull(reason);
        Assert.Contains("digest", reason);
    }

    /// <summary>
    /// THE SAVING, IN ONE ASSERTION. Two members reporting four minutes apart are ONE pending set
    /// when the window runs out, so they buy one turn between them instead of one each — and the
    /// second one's arrival did not push the deadline out, which is what the separate hold stamp
    /// (<c>SessionTracker.DigestHeldSince</c>) exists for.
    /// </summary>
    [Fact]
    public void TwoMembersReportingInsideOneWindow_RideOneTurn()
    {
        List<PendingEntry> pending =
        [
            new(IMP, Entry("implementer", "REPORT — parser done")),
            new(REV, Entry("reviewer", "VERDICT — accepted")),
        ];

        Assert.Null(Resolve(pending, NOW, NOW.AddMinutes(4)));
        Assert.NotNull(Resolve(pending, NOW, NOW.AddMinutes(5)));
    }

    /// <summary>
    /// A BLOCKED MEMBER IS NEVER HELD. <c>BLOCKED ON OWNER</c> means two sessions and a person are
    /// waiting on one another, so five minutes of digest would be five minutes of nobody working —
    /// the case the constraint on this change names explicitly.
    /// </summary>
    [Fact]
    public void AMemberDeclaringItIsBlocked_WakesTheSupervisorAtOnce()
    {
        var reason = Resolve(
            [new PendingEntry(IMP, Entry("implementer", $"{MemberState_Resolver.BLOCKED_ON_OWNER_MARKER} — which branch?"))],
            NOW,
            NOW);

        Assert.NotNull(reason);
        Assert.Contains(MemberState_Resolver.BLOCKED_ON_OWNER_MARKER, reason);
    }

    /// <summary>
    /// A QUESTION IS NOT HELD EITHER — and it is read from the BODY, at the start of a line, which is
    /// where the marker vocabulary says a declaration may live. The supervisor's own <c>QUESTION:</c>
    /// shape is what a member reaches for when it has one, and waking is both the cheap and the safe
    /// direction if it does.
    /// </summary>
    [Fact]
    public void AMemberAskingAQuestionInTheBody_WakesTheSupervisorAtOnce()
    {
        Assert.NotNull(Resolve(
            [new PendingEntry(IMP, Entry("implementer", "parser done", "QUESTION: do I merge or hold?"))],
            NOW,
            NOW));
    }

    /// <summary>
    /// AND A BRIEF THAT QUOTES A MARKER DOES NOT DECLARE ONE. The match is
    /// <see cref="MemberState_Resolver.Contains_Marker"/>'s, which excludes quotation for exactly this
    /// reason — members discuss this vocabulary constantly, and a mention that woke the supervisor
    /// would put the digest's cost back at one wake per report.
    /// </summary>
    [Fact]
    public void AMemberMerelyQuotingAMarker_IsStillHeld()
    {
        Assert.Null(Resolve(
            [new PendingEntry(IMP, Entry("implementer", "REPORT — parser done", "> BLOCKED ON OWNER is what I would have written yesterday"))],
            NOW,
            NOW.AddMinutes(1)));
    }

    /// <summary>
    /// ONE URGENT ENTRY RELEASES THE WHOLE SET, because a turn takes every pending entry there is.
    /// That is what makes holding cheap: nothing waits for its own second turn.
    /// </summary>
    [Fact]
    public void AnUrgentEntryBesideHeldOnes_ReleasesThemAll()
    {
        Assert.NotNull(Resolve(
            [
                new PendingEntry(IMP, Entry("implementer", "REPORT — parser done")),
                new PendingEntry(REV, Entry("reviewer", $"{MemberState_Resolver.BLOCKED_ON_OWNER_MARKER} — no test data")),
            ],
            NOW,
            NOW));
    }

    /// <summary>
    /// EVERY OTHER ROLE'S TIMING IS UNTOUCHED, AND WITHOUT A ROLE TEST ANYWHERE. A member's inbound
    /// authors are its supervisor and the owner (<see cref="PrintTurn_Trigger.Is_Inbound"/>), so a
    /// brief is not digestable traffic and an implementer is woken by it exactly as it was before this
    /// stage existed.
    /// </summary>
    [Fact]
    public void ASupervisorBriefToAMember_IsNeverHeld()
    {
        Assert.NotNull(Resolve([new PendingEntry(IMP, Entry("supervisor", "BRIEF — port the parser"))], NOW, NOW));
    }

    /// <summary>
    /// THE APP'S OWN BOOKKEEPING CANNOT REACH THIS METHOD AT ALL, which is the strongest form of "it
    /// never wakes the supervisor": <see cref="PrintTurn_Trigger.Is_Inbound"/> is false for
    /// <see cref="ChannelAuthors.App"/> for every role, so a <c>turn_ended</c>, a <c>STATUS</c>, a
    /// ledger advisory or an orphan notice is never pending for anybody and never enters a cursor
    /// either (<c>TurnCursor_Factory</c> records inbound entries only). Measured: zero turns in the
    /// 6–9 Sep window were triggered by app entries alone. Pinned here so a later change to
    /// <c>Is_Inbound</c> cannot quietly make app traffic a reason to wake the most expensive role in
    /// the system.
    /// </summary>
    [Fact]
    public void TheAppsOwnEntries_AreInboundForNobody()
    {
        foreach (var role in SessionRole_Names.ALL)
            Assert.False(PrintTurn_Trigger.Is_Inbound(role, ChannelAuthors.App), $"an app entry is inbound for {role}");
    }

    /// <summary>
    /// A ZERO WINDOW IS THE DIGEST TURNED OFF, not a zero-length wait that still holds for a tick.
    /// It is the owner's way back to one-entry-one-turn without a deployment.
    /// </summary>
    [Fact]
    public void AZeroDigestWindow_DeliversAtOnce()
    {
        Assert.NotNull(WakeUp_Policy.Resolve_WakeReason_OrNull(
            [new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NOW, NOW, TimeSpan.Zero));
    }

    /// <summary>
    /// THE BOOT TURN RUNS WITH NOTHING PENDING and must not be held: the greeting it produces is what
    /// creates the orchestration's Telegram topic, so holding it would hold the owner's own way in.
    /// </summary>
    [Fact]
    public void AnEmptyPendingSet_IsTheBootTurnAndRuns()
    {
        Assert.NotNull(Resolve([], null, NOW));
    }

    /// <summary>
    /// A HOLD NOBODY STAMPED IS NOT A REASON TO WAIT. The dispatcher always sets the stamp before it
    /// asks, so this is the unreachable-by-construction case — and it answers with today's behaviour
    /// rather than holding traffic on a clock that was never started.
    /// </summary>
    [Fact]
    public void MemberTrafficWithNoHoldStamp_IsDelivered()
    {
        Assert.NotNull(Resolve([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], null, NOW));
    }

    /// <summary>
    /// AND A STAMP IN THE FUTURE IS A CLOCK THAT MOVED, NEVER A LONGER WAIT (CLAUDE.md decision 12: a
    /// duration that cannot be believed must not become a confident wrong number). The honest
    /// direction here is to deliver.
    /// </summary>
    [Fact]
    public void AHoldStampInTheFuture_DeliversRatherThanWaiting()
    {
        Assert.NotNull(Resolve([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NOW.AddMinutes(30), NOW));
    }

    /// <summary>
    /// THE CLOCK STARTS ON THE FIRST HELD ENTRY: this is the question the dispatcher asks to decide
    /// when to stamp it, and it must be true of a member's report and false of everything else.
    /// </summary>
    [Fact]
    public void OnlyAMembersOrdinaryEntry_StartsTheHold()
    {
        Assert.True(WakeUp_Policy.Contains_DigestableTraffic([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))]));
        Assert.True(WakeUp_Policy.Contains_DigestableTraffic([new PendingEntry(REV, Entry("reviewer", "VERDICT — accepted"))]));

        Assert.False(WakeUp_Policy.Contains_DigestableTraffic([new PendingEntry(OWNER, Entry("owner", "restart the crew"))]));
        Assert.False(WakeUp_Policy.Contains_DigestableTraffic([new PendingEntry(IMP, Entry("supervisor", "BRIEF — port the parser"))]));
        Assert.False(WakeUp_Policy.Contains_DigestableTraffic([]));
        Assert.False(WakeUp_Policy.Contains_DigestableTraffic(
            [new PendingEntry(IMP, Entry("implementer", $"{MemberState_Resolver.BLOCKED_ON_OWNER_MARKER} — which branch?"))]));
    }
}
