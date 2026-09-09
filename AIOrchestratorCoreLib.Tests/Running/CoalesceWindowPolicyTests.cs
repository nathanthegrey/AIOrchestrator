using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// WHO PAYS THE COALESCE WINDOW AND WHO DOES NOT (owner decision, 2026-09-09).
///
/// <para>
/// The dispatcher waits <c>DEFAULT_COALESCE_WINDOW</c> — three seconds — for the pending set to stop
/// changing before it starts a turn, so entries still landing ride the same turn instead of buying a
/// second one. For member → supervisor traffic that is worth every second: an extra supervisor wake
/// costs ~1 M input tokens, measured, and a crew of implementers filing reports within a second of
/// each other is the normal case.
/// </para>
/// <para>
/// For the OWNER it is three seconds of a person waiting on their phone, on top of the aggregation
/// window and a mirror tick — 11–12 s median end to end, measured on the VPS the same day. So the
/// window is WAIVED when the pending set contains something the owner wrote on an owner channel, and
/// kept for everything else. Entries that happen to land in the same pass still ride along: the
/// waiver skips the WAIT, it does not narrow the turn.
/// </para>
/// <para>
/// THE TWO HALVES ARE PINNED SEPARATELY BELOW — author, and channel — because a case that could be
/// waived for either of two reasons would pin neither (CLAUDE.md decision 20).
/// </para>
/// </summary>
public class CoalesceWindowPolicyTests
{
    static readonly ITurnSource OWNER = TurnSource_Factory.Create_Owner("/o/owner-channel.md");
    static readonly ITurnSource IMP = TurnSource_Factory.Create_Spoke("imp-1", "/o/imp-1/channel.md");

    static IChannelEntry Entry(string author, string subject)
    {
        return ChannelEntry_Parser.Parse_All($"## [1] FROM {author} — 2026-09-09 10:05 — {subject}\n\nbody\n")[0];
    }

    /// <summary>The owner's phone line: the one case that skips the wait.</summary>
    [Fact]
    public void AnOwnerEntryOnTheOwnerChannel_WaivesTheWindow()
    {
        Assert.True(CoalesceWindow_Policy.Is_Waived([new PendingEntry(OWNER, Entry("owner", "restart the crew"))]));
    }

    /// <summary>
    /// MEMBER TRAFFIC STILL PAYS IT. This is the case the window was written for and the one that
    /// carries the measured cost — ~1 M input tokens per extra supervisor wake — so it is the control
    /// that stops the waiver from quietly becoming "no coalescing at all".
    /// </summary>
    [Fact]
    public void MemberTraffic_StillServesTheWindow()
    {
        Assert.False(CoalesceWindow_Policy.Is_Waived(
        [
            new PendingEntry(IMP, Entry("implementer", "task done")),
            new PendingEntry(IMP, Entry("implementer", "and the next one")),
        ]));
    }

    /// <summary>
    /// THE OWNER'S MESSAGE DOES NOT WAIT BEHIND A BUSY CREW. One owner entry in the set is enough,
    /// and the members' entries ride the very same turn — the waiver skips the WAIT, it does not
    /// narrow what the turn reads.
    /// </summary>
    [Fact]
    public void OneOwnerEntryAmongMemberReports_WaivesTheWindowForAllOfThem()
    {
        Assert.True(CoalesceWindow_Policy.Is_Waived(
        [
            new PendingEntry(IMP, Entry("implementer", "task done")),
            new PendingEntry(OWNER, Entry("owner", "stop everything")),
            new PendingEntry(IMP, Entry("implementer", "and the next one")),
        ]));
    }

    /// <summary>
    /// THE AUTHOR HALF. Same channel, different author: a solo writes its own entries into
    /// <c>owner-channel.md</c> (<see cref="TurnSources_Resolver.Resolve_Own"/> maps it there), so an
    /// owner CHANNEL is not by itself evidence that the owner is waiting.
    /// </summary>
    [Fact]
    public void AMemberEntryOnTheOwnerChannel_DoesNotWaiveIt()
    {
        Assert.False(CoalesceWindow_Policy.Is_Waived([new PendingEntry(OWNER, Entry("implementer", "task done"))]));
    }

    /// <summary>
    /// THE CHANNEL HALF. Same author, different channel: the owner can type straight into a member's
    /// spoke, and that is not the phone line the 11–12 s was measured on — nothing was mirrored, no
    /// aggregation window was served, and the member it addresses is not the owner's supervisor. It
    /// keeps the window, which also keeps the waiver confined to the roles that HAVE an owner channel:
    /// supervisor, solo and general.
    /// </summary>
    [Fact]
    public void AnOwnerEntryTypedIntoASpoke_DoesNotWaiveIt()
    {
        Assert.False(CoalesceWindow_Policy.Is_Waived([new PendingEntry(IMP, Entry("owner", "do this one first"))]));
    }

    /// <summary>Nothing pending is not a waiver — the boot turn has its own reason to run.</summary>
    [Fact]
    public void AnEmptyPendingSet_DoesNotWaiveIt()
    {
        Assert.False(CoalesceWindow_Policy.Is_Waived([]));
    }
}
