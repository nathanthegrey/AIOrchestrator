using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>
/// Has the solo written down what the crew replacing it will need? The precondition of a promotion,
/// and what makes "nothing moves" safe.
///
/// Promotion ends the solo session and spawns a supervisor onto the same `owner-channel.md`. No
/// history is copied because none has to be — the supervisor reads the very file the solo was
/// writing. But the solo's IN-CONTEXT state dies with its terminal, and that is the half no file
/// carries: whatever it worked out and never wrote down is gone. The channel is the durable part,
/// so the handover entry is what makes the durable part complete.
///
/// A REQUIREMENT OF THE REQUEST, not a courtesy, and a settled design decision. The owner tap that
/// follows costs a supervisor and an implementer indefinitely; a crew briefed from a channel that
/// stops mid-thought is worse than the solo that was already there.
/// </summary>
public static class HandoverEntry_Detector
{
    /// <summary>
    /// The marker the solo puts in its handover entry. Reads like the six markers already in this
    /// system because it IS one — same vocabulary, same matcher, same position rule.
    /// </summary>
    public const string HANDOVER_MARKER = "HANDOVER";

    /// <summary>
    /// THE SOLO'S OWN ENTRY, and read through the one matcher this repo has rather than a second one
    /// written for this marker. That buys the rules already fought for: the marker counts in the
    /// SUBJECT anywhere or at the START of a body line, so mid-sentence discussion of a handover is
    /// not a handover; it must be a whole token, so `HANDOVERS` is not it; and bold or bulleted
    /// markdown still counts, because members write markdown.
    ///
    /// The AUTHOR gate is the same one the window markers needed and for the same reason. The owner's
    /// own messages arrive in this file as `FROM owner` entries, so without it "can you hand this
    /// over to a crew?" would satisfy the requirement that exists to make the SOLO write things down.
    /// </summary>
    public static bool Has_HandoverEntry(IReadOnlyList<IChannelEntry> entries)
    {
        return Has_HandoverEntry(entries, ChannelAuthors.Solo);
    }

    /// <summary>
    /// THE SAME REQUIREMENT, IN THE OTHER DIRECTION. A demotion ends the SUPERVISOR and spawns a solo
    /// onto the same channel, so the entry that has to exist is the supervisor's — and everything the
    /// summary above says about why applies unchanged, with the roles swapped.
    ///
    /// The author is a parameter rather than "any session" precisely because of the gate the summary
    /// describes: the owner's own `FROM owner` messages live in this file, and so does the other
    /// role's. Accepting any author would let a solo's old handover, written before a promotion,
    /// satisfy the demotion that comes after it — a stale entry about a different session's state.
    /// </summary>
    public static bool Has_HandoverEntry(IReadOnlyList<IChannelEntry> entries, ChannelAuthors author)
    {
        foreach (var entry in entries)
        {
            if (entry.Author != author)
                continue;

            if (MemberState_Resolver.Contains_Marker(entry, HANDOVER_MARKER))
                return true;
        }

        return false;
    }
}
