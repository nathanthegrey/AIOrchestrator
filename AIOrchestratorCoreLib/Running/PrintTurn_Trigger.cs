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
