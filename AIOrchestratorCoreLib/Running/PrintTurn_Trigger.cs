using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// WHICH entries start a print turn. An entry is inbound for a role when its author is someone
/// that role must answer — the supervisor (or the owner, typing straight into a spoke) for a
/// member; the owner for a solo and for the general supervisor. A session's OWN entries never do,
/// and neither do the app's: nudges, receipts and turn_ended records are read on the next turn,
/// and a turn started by the record of the previous turn would be a loop with one member in it.
/// Only entries ABOVE the last handled index count — that index is the queue's cursor.
///
/// <para>
/// THE CURSOR TRUSTS THE AGENT-WRITTEN <c>[n]</c>, AND THAT IS A KNOWN LIMIT, not an oversight.
/// CLAUDE.md decision 12 records that field as a guess unless the writer re-read the file, with a
/// live incident (two <c>[80]</c> and two <c>[81]</c> in one channel). A terminal supervisor that
/// mints a duplicate or lower index writes a brief a print-run member will never be handed — and
/// unlike a watcher, which wakes on any byte, there is no second path to notice it: the member
/// simply sits idle until the next well-numbered entry.
/// </para>
/// <para>
/// It is NOT fixed by counting entries or by their position in the file: <c>Channel_Compactor</c>
/// moves older entries into a sibling archive, so neither is monotonic (decision 13, which cost this
/// repo a nudge loop that could never clear). A cursor that survives both would have to be
/// content-addressed — a delivered-entry digest in the state file — which is a design change rather
/// than a tightening, and is deliberately left to the stage that can measure whether it is needed.
/// Until then the honest statement is: print-run delivery is exactly as reliable as the numbering
/// its writers keep, and for every print-run session's own entries that writer is the bridge.
/// </para>
/// </summary>
public static class PrintTurn_Trigger
{
    public static IReadOnlyList<IChannelEntry> Select_Pending(SessionRoles role, IReadOnlyList<IChannelEntry> entries, int lastHandledEntryIndex)
    {
        List<IChannelEntry> pending = [];

        foreach (var entry in entries)
        {
            if (entry.Index > lastHandledEntryIndex && Is_Inbound(role, entry.Author))
                pending.Add(entry);
        }

        return pending;
    }

    public static bool Is_Inbound(SessionRoles role, ChannelAuthors author)
    {
        return role switch
        {
            SessionRoles.Implementer or SessionRoles.Reviewer => author is ChannelAuthors.Supervisor or ChannelAuthors.Owner,
            SessionRoles.Solo or SessionRoles.General => author == ChannelAuthors.Owner,
            SessionRoles.Supervisor => author == ChannelAuthors.Owner || ChannelAuthor_Kinds.Is_Member(author),
            SessionRoles.Communicator => author == ChannelAuthors.Owner,
            _ => false,
        };
    }
}
