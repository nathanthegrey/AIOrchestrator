using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Running.PendingTraffic;

/// <summary>
/// WHETHER THIS PENDING SET MAY START ITS TURN WITHOUT WAITING OUT THE COALESCE WINDOW.
///
/// <para>
/// The dispatcher normally waits <c>IRunnerConfigs.CoalesceWindow</c> — three seconds — for the
/// pending set to stop changing, so entries still landing ride the same turn instead of buying a
/// second one. For member → supervisor traffic that is worth every second: an extra supervisor wake
/// costs ~1 M input tokens, measured, and a crew filing reports within a second of each other is the
/// normal case, not the edge one.
/// </para>
/// <para>
/// FOR THE OWNER IT IS THREE SECONDS OF A PERSON WAITING (owner decision, 2026-09-09). Measured on
/// the VPS that day: 11–12 s median from their Telegram message to the supervisor's turn starting —
/// aggregation window, then a mirror tick, then this. The owner's ruling was to keep the coalescence
/// for member traffic and remove it for theirs, so an owner entry starts the turn on the NEXT
/// dispatcher pass. Whatever else lands in that pass still rides along: this skips the WAIT, it never
/// narrows the turn.
/// </para>
/// <para>
/// BOTH HALVES ARE REQUIRED — the OWNER wrote it, and they wrote it on an OWNER CHANNEL — and neither
/// is redundant. A solo writes its own entries into <c>owner-channel.md</c>
/// (<see cref="TurnSource.TurnSources_Resolver.Resolve_Own"/>), so the channel alone is not evidence
/// that a person is waiting; and the owner can type straight into a member's spoke, which is not the
/// path the 11–12 s was measured on — nothing was mirrored and no aggregation window was served.
/// Requiring the channel is also what confines the waiver to the roles that HAVE one (supervisor,
/// solo, general): a member's sources are spokes, so its traffic keeps the window with no role test
/// anywhere.
/// </para>
/// <para>
/// A POLICY RATHER THAN AN <c>if</c> because the dispatcher's decision has one line per reason and
/// this reason is the owner's, argued from a measurement — it belongs where it can be read and tested
/// without a state file, a clock or a session.
/// </para>
/// </summary>
public static class CoalesceWindow_Policy
{
    public static bool Is_Waived(IReadOnlyList<PendingEntry> pending)
    {
        foreach (var item in pending)
        {
            if (item.Source.IsOwnerChannel && item.Entry.Author == ChannelAuthors.Owner)
                return true;
        }

        return false;
    }
}
