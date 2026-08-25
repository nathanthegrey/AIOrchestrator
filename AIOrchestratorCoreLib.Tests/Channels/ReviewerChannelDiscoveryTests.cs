using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// A reviewer's channel was never DISCOVERED, so it was never tailed and never mirrored — the
/// prefix test here read `imp-` alone. The absence was invisible from the inside: MirrorText_Formatter
/// had full reviewer support, its own green glyph and a passing test for it, all reached by nothing.
///
/// The owner found it from the outside, 2026-08-25: *"I should receive Sup Online, or Solo Online,
/// and I also want Rev1 Online."* They had been getting imp-1's boot greeting for months while no
/// reviewer had ever said a word to them.
/// </summary>
public class ReviewerChannelDiscoveryTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public ReviewerChannelDiscoveryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-revdiscovery-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    void Seed_Orchestration(string orchId, params string[] memberIds)
    {
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder(orchId));
        File.WriteAllText(_paths.Get_SessionFile(orchId), "{}");
        File.WriteAllText(_paths.Get_OwnerChannelFile(orchId), "# owner channel\n");

        foreach (var memberId in memberIds)
        {
            Directory.CreateDirectory(_paths.Get_ImplementerFolder(orchId, memberId));
            File.WriteAllText(_paths.Get_ImplementerChannelFile(orchId, memberId), $"# {memberId} channel\n");
        }
    }

    [Fact]
    public void AReviewersChannel_IsDiscovered_AlongsideTheImplementers()
    {
        Seed_Orchestration("orch-1", "imp-1", "rev-1");

        var found = ChannelDiscovery.Find_ChannelFiles(_paths);

        Assert.Contains(found, channel => channel.OrchId == "orch-1" && channel.SpokeName == "rev-1");
        Assert.Contains(found, channel => channel.OrchId == "orch-1" && channel.SpokeName == "imp-1");
    }

    /// <summary>
    /// Both prefixes come from MemberKind_Ids rather than being restated here — a second copy of
    /// "imp-" is exactly how the reviewer went missing, so the test pins the shape it must keep.
    /// </summary>
    [Theory]
    [InlineData("rev-1")]
    [InlineData("rev-2")]
    [InlineData("REV-3")]
    public void EveryReviewerIndex_IsFound_AndTheMatchIsCaseInsensitive(string memberId)
    {
        Seed_Orchestration("orch-2", memberId);

        var found = ChannelDiscovery.Find_ChannelFiles(_paths);

        Assert.Contains(found, channel => string.Equals(channel.SpokeName, memberId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A spoke channel is NOT an owner channel — that flag is what routes it past OwnerPush_Policy
    /// and keeps the mirror to the presence one-liner alone. Getting this wrong would put every
    /// reviewer report on the owner's phone, which is the waterfall the whole filter exists to stop.
    /// </summary>
    [Fact]
    public void AReviewersChannel_IsNotAnOwnerChannel()
    {
        Seed_Orchestration("orch-3", "rev-1");

        var reviewer = Assert.Single(ChannelDiscovery.Find_ChannelFiles(_paths), channel => channel.SpokeName == "rev-1");

        Assert.False(reviewer.IsOwnerChannel);
    }

    /// <summary>
    /// A solo has no spoke of its own — it writes to owner-channel.md — so its folder must not be
    /// picked up as one even though it sits beside the others.
    /// </summary>
    [Fact]
    public void ASolosFolder_IsStillNotASpoke()
    {
        Seed_Orchestration("orch-4", "solo-1");

        Assert.DoesNotContain(ChannelDiscovery.Find_ChannelFiles(_paths), channel => channel.SpokeName == "solo-1");
    }
}
