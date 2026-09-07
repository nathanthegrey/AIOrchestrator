using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The pure decision behind <c>ATTACH: &lt;path&gt;</c>: which files an agent may send the owner.
/// </summary>
public class EntryAttachmentPolicyTests
{
    static readonly string Repo = Path.Combine(Path.GetTempPath(), "aiorch-attach-repo");
    static readonly string Channel = Path.Combine(Path.GetTempPath(), "aiorch-attach-channel");
    static readonly IReadOnlyList<string> Roots = [Repo, Channel];

    [Fact]
    public void AFileUnderTheRepo_IsSent()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "mockups", "plan-card.html"), Roots, exists: true, lengthBytes: 12_000);
        Assert.Equal(AttachmentVerdicts.Send, verdict);
    }

    [Fact]
    public void AFileUnderTheChannelFolder_IsSent()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Channel, "media", "report.md"), Roots, exists: true, lengthBytes: 500);
        Assert.Equal(AttachmentVerdicts.Send, verdict);
    }

    [Fact]
    public void ASiblingFolderWhoseNameMerelyStartsWithTheRoot_IsOutside()
    {
        // `…/aiorch-attach-repo-evil/x.html` starts with the root's characters and is not under it.
        var verdict = EntryAttachment_Policy.Decide(Repo + "-evil" + Path.DirectorySeparatorChar + "x.html", Roots, exists: true, lengthBytes: 10);
        Assert.Equal(AttachmentVerdicts.OutsideAllowedRoots, verdict);
    }

    [Fact]
    public void ATraversalOutOfTheRoot_IsOutside()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "..", "id_rsa"), Roots, exists: true, lengthBytes: 10);
        Assert.Equal(AttachmentVerdicts.OutsideAllowedRoots, verdict);
    }

    [Fact]
    public void AMissingFile_IsReportedBeforeAnythingElse()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "gone.html"), Roots, exists: false, lengthBytes: 0);
        Assert.Equal(AttachmentVerdicts.MissingFile, verdict);
    }

    [Fact]
    public void AFileOverTelegramsDocumentCap_IsTooLarge()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "dump.bin"), Roots, exists: true, lengthBytes: EntryAttachment_Policy.MAX_BYTES + 1);
        Assert.Equal(AttachmentVerdicts.TooLarge, verdict);
    }

    [Fact]
    public void NoAllowedRoots_MeansNothingIsSendable()
    {
        var verdict = EntryAttachment_Policy.Decide(Path.Combine(Repo, "x.html"), [], exists: true, lengthBytes: 10);
        Assert.Equal(AttachmentVerdicts.OutsideAllowedRoots, verdict);
    }

    [Fact]
    public void EveryRefusal_NamesTheRuleAndThePath()
    {
        foreach (var verdict in new[] { AttachmentVerdicts.MissingFile, AttachmentVerdicts.OutsideAllowedRoots, AttachmentVerdicts.TooLarge })
        {
            var text = EntryAttachment_Policy.Describe(verdict, "/x/y.html", Roots);
            Assert.Contains("/x/y.html", text, StringComparison.Ordinal);
            Assert.Contains("ATTACH", text, StringComparison.Ordinal);
        }

        Assert.Contains(Repo, EntryAttachment_Policy.Describe(AttachmentVerdicts.OutsideAllowedRoots, "/x/y.html", Roots), StringComparison.Ordinal);
    }
}
