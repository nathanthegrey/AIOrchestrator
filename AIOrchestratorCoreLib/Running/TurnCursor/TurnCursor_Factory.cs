using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.TurnCursor;

public static class TurnCursor_Factory
{
    public static ITurnCursor Create(string sourceKey, string channelFilePath, int highWaterIndex, IReadOnlySet<string> delivered)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException($"A turn cursor needs a source key (channel '{channelFilePath}')");
        if (string.IsNullOrWhiteSpace(channelFilePath))
            throw new ArgumentException($"A turn cursor needs a channel file (source '{sourceKey}')");
        if (highWaterIndex < 0)
            throw new ArgumentException($"High water index must be >= 0, got {highWaterIndex} (source '{sourceKey}')");

        return new TurnCursorModel(sourceKey, channelFilePath, highWaterIndex, delivered);
    }

    /// <summary>
    /// FIRST SIGHT OF A SOURCE: everything already in it is HISTORY, not traffic. The identities of the
    /// inbound entries live at this moment are recorded as delivered, so the session is not handed a
    /// channel's whole backlog as if it had just been said to it.
    ///
    /// <para>
    /// It is the same call <see cref="Bridge.BridgeState_Store"/> makes for the mirror and
    /// <see cref="ChannelBaseline_Pass"/> makes for the shape sweeps, and it has the same cost, which is
    /// why the caller announces it rather than swallowing it: absorbed history is a HOLE, not a
    /// duplicate, and a hole nobody is told about is the failure this repo keeps paying for. A spoke that
    /// appears while the session is running is empty when it appears, so in the ordinary life of an
    /// orchestration this absorbs nothing at all.
    /// </para>
    /// </summary>
    public static ITurnCursor Create_Baseline(ITurnSource source, SessionRoles role, IReadOnlyList<IChannelEntry> liveEntries)
    {
        HashSet<string> delivered = [];
        var highWater = 0;

        foreach (var entry in liveEntries)
        {
            if (!PrintTurn_Trigger.Is_Inbound(role, entry.Author))
                continue;

            delivered.Add(ChannelEntry_Digest.Compute(entry));
            highWater = Math.Max(highWater, entry.Index);
        }

        return Create(source.Key, source.ChannelFilePath, highWater, delivered);
    }

    /// <summary>An empty cursor: nothing has been delivered and nothing is absorbed — every inbound entry is pending.</summary>
    public static ITurnCursor Create_Empty(ITurnSource source)
    {
        return Create(source.Key, source.ChannelFilePath, 0, new HashSet<string>());
    }

    /// <summary>
    /// The cursor after a turn: the delivered identities plus the ones just handed over, PRUNED to what
    /// is still in the live file.
    ///
    /// <para>
    /// The prune is what bounds the set, and it is safe in exactly one direction: an identity that has
    /// left the live file left it through <see cref="Channel_Compactor"/>, which only ever moves from the
    /// front and never puts anything back. Nothing else removes an entry from a channel — they are
    /// append-only by protocol.
    /// </para>
    /// </summary>
    public static ITurnCursor CreateFrom_Delivered(ITurnCursor cursor, SessionRoles role, IReadOnlyList<IChannelEntry> liveEntries, IReadOnlyList<IChannelEntry> justDelivered)
    {
        HashSet<string> stillLive = [];
        var highWater = cursor.HighWaterIndex;

        foreach (var entry in liveEntries)
        {
            if (PrintTurn_Trigger.Is_Inbound(role, entry.Author))
                stillLive.Add(ChannelEntry_Digest.Compute(entry));
        }

        HashSet<string> delivered = [];

        foreach (var identity in cursor.Delivered)
        {
            if (stillLive.Contains(identity))
                delivered.Add(identity);
        }

        foreach (var entry in justDelivered)
        {
            delivered.Add(ChannelEntry_Digest.Compute(entry));
            highWater = Math.Max(highWater, entry.Index);
        }

        return Create(cursor.SourceKey, cursor.ChannelFilePath, highWater, delivered);
    }
}
