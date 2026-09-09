using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// The entry a fresh member is working FROM. A session that resumes its transcript remembers its
/// brief; a fresh one does not, and the 4a design handed it "the last three entries" instead —
/// which do not contain the brief once a review round or two has passed. Measured 2026-09-08 on
/// the VPS: 3 of 25 member channels were compacted (45 entries ≈ 29 turns kept live) and the
/// archive was opened by a session twice in the whole window, so the brief of a long-lived member
/// was unreachable in practice. The finder runs on the WHOLE history (archive + live) and picks
/// deterministically, so the pack never depends on a model's guess about what it was asked.
///
/// <para>
/// Two rules, in order. A supervisor entry whose subject opens with a task marker is a brief — the
/// supervisors write `BRIEF —`, `REVIEW …`, `FINAL REVIEW —`, `FINDINGS —` by protocol, and the
/// LATEST such entry is the work in hand. When no subject carries a marker (an early orchestration,
/// a supervisor that words things its own way), the longest supervisor entry among the last
/// <see cref="FALLBACK_WINDOW"/> is taken: a brief is the one entry a supervisor cannot write
/// short. Null when the channel holds no supervisor entry at all.
/// </para>
/// </summary>
public static class Brief_Finder
{
    public const int FALLBACK_WINDOW = 20;

    public static readonly IReadOnlyList<string> TASK_MARKERS =
        ["BRIEF", "REVIEW", "FINAL REVIEW", "RE-REVIEW", "FINDINGS", "TASK", "GO AHEAD"];

    public static IChannelEntry? Find_OrNull(IReadOnlyList<IChannelEntry> history)
    {
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var entry = history[i];

            if (entry.Author == ChannelAuthors.Supervisor && Has_TaskMarker(entry.Subject))
                return entry;
        }

        IChannelEntry? longest = null;
        var start = Math.Max(0, history.Count - FALLBACK_WINDOW);

        for (var i = start; i < history.Count; i++)
        {
            var entry = history[i];

            if (entry.Author != ChannelAuthors.Supervisor)
                continue;

            // ">=" so that, among equals, the LATER entry wins — recency is the tie-break because
            // the newer of two equally long supervisor entries is the more likely to be current.
            if (longest == null || entry.RawText.Length >= longest.RawText.Length)
                longest = entry;
        }

        return longest;
    }

    public static bool Has_TaskMarker(string subject)
    {
        var trimmed = subject.TrimStart();

        foreach (var marker in TASK_MARKERS)
        {
            if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
