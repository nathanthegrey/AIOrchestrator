namespace AIOrchestratorCoreLib.Channels.DiscoveredChannel;

public static class DiscoveredChannel_Factory
{
    /// <summary>
    /// Every SPOKE channel, not only an implementer's — reviewers come through here too since
    /// ChannelDiscovery started finding them (2026-08-25). The name is narrower than the method;
    /// what it actually builds is "a member's own channel, which is not the owner channel", and
    /// nothing about it is implementer-specific.
    /// </summary>
    public static IDiscoveredChannel Create_ForImplementer(string orchId, string memberId, string filePath)
    {
        return new DiscoveredChannelModel(orchId, memberId, filePath, isOwnerChannel: false);
    }

    public static IDiscoveredChannel Create_ForOwner(string orchId, string filePath)
    {
        return new DiscoveredChannelModel(orchId, "owner", filePath, isOwnerChannel: true);
    }
}
