using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Finds every channel file under the supervision root. An orchestration is any subfolder
/// holding a session.json; its channels are owner-channel.md plus every SPOKE channel —
/// imp-*/channel.md and rev-*/channel.md.
///
/// REVIEWERS WERE MISSING HERE AND THE ABSENCE WAS INVISIBLE. The prefix test was `imp-` alone, so
/// a rev-*/channel.md was never discovered, never tailed and never mirrored — while
/// MirrorText_Formatter carried full reviewer support (its own green glyph) and a passing test for
/// it, reached by nothing. The owner found it from the outside, 2026-08-25: *"I also want Rev1
/// Online"* — they were receiving imp-1's boot greeting and no reviewer had ever said a word.
///
/// A solo is deliberately NOT here: it writes to owner-channel.md, not to a spoke of its own
/// (MemberChannel_Locator), so its folder holds no channel file to find.
/// </summary>
public static class ChannelDiscovery
{
    /// <summary>
    /// The spoke folder prefixes, taken from the one place that defines them rather than restated —
    /// a second copy of "imp-" is how the reviewer went missing in the first place.
    /// </summary>
    static readonly string[] SPOKE_FOLDER_PREFIXES =
    [
        Sessions.MemberKind_Ids.IMPLEMENTER_PREFIX,
        Sessions.MemberKind_Ids.REVIEWER_PREFIX,
    ];

    /// <summary>Reserved orchestration id of the always-on general supervisor.</summary>
    public const string GENERAL_ORCH_ID = "general";

    public static IReadOnlyList<IDiscoveredChannel> Find_ChannelFiles(ISupervisionPaths paths)
    {
        List<IDiscoveredChannel> channels = [];

        if (!Directory.Exists(paths.Root))
            return channels;

        if (File.Exists(paths.GeneralChannelFile))
            channels.Add(DiscoveredChannel_Factory.Create_ForOwner(GENERAL_ORCH_ID, paths.GeneralChannelFile));

        foreach (var orchFolder in Directory.EnumerateDirectories(paths.Root))
        {
            var orchId = Path.GetFileName(orchFolder);

            if (orchId == GENERAL_ORCH_ID)
                continue;

            if (!File.Exists(paths.Get_SessionFile(orchId)))
                continue;

            var ownerChannel = paths.Get_OwnerChannelFile(orchId);
            if (File.Exists(ownerChannel))
                channels.Add(DiscoveredChannel_Factory.Create_ForOwner(orchId, ownerChannel));

            foreach (var memberFolder in Directory.EnumerateDirectories(orchFolder))
            {
                var memberId = Path.GetFileName(memberFolder);

                if (!SPOKE_FOLDER_PREFIXES.Any(prefix => memberId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var channelFile = paths.Get_ImplementerChannelFile(orchId, memberId);
                if (File.Exists(channelFile))
                    channels.Add(DiscoveredChannel_Factory.Create_ForImplementer(orchId, memberId, channelFile));
            }
        }

        return channels;
    }
}
