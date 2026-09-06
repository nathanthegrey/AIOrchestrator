using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The addressing format, and the two directions its mistakes can go. Reading prose as an address moves a
/// paragraph to a channel nobody meant; reading an address as prose leaves it in the owner channel where
/// a human is already looking. The parser is tolerant only towards the second.
/// </summary>
public class TurnReplySplitterTests
{
    [Fact]
    public void TextBeforeTheFirstMarker_IsAddressedToNobody()
    {
        var blocks = TurnReply_Splitter.Split("a summary\n\nTO: imp-1\nVERDICT\n\naccepted");

        Assert.Equal(2, blocks.Count);
        Assert.Null(blocks[0].SourceKey);
        Assert.Equal("a summary", blocks[0].Text);
        Assert.Equal("imp-1", blocks[1].SourceKey);
        Assert.Equal("VERDICT\n\naccepted", blocks[1].Text);
    }

    [Fact]
    public void AMessageWithNoMarkerAtAll_IsOneUnaddressedBlock()
    {
        var block = Assert.Single(TurnReply_Splitter.Split("Status\n\nall fine"));

        Assert.Null(block.SourceKey);
        Assert.Equal("Status\n\nall fine", block.Text);
    }

    [Fact]
    public void EachMarkerOpensABlockThatRunsToTheNext()
    {
        var blocks = TurnReply_Splitter.Split("TO: owner\nStatus\n\nfine\n\nTO: imp-1\nBRIEF\n\ngo\n\nTO: rev-1\nREVIEW\n\nlook");

        Assert.Equal(["owner", "imp-1", "rev-1"], blocks.Select(block => block.SourceKey));
        Assert.Equal("BRIEF\n\ngo", blocks[1].Text);
    }

    /// <summary>
    /// The reason a member is never put through this splitter at all, stated at the level below: a line
    /// like this is ordinary prose in a report about routing, and a rule that read it as an address would
    /// silently cut the report in half.
    /// </summary>
    [Theory]
    [InlineData("TO: the owner, when you get a moment")]
    [InlineData("to: whoever picks this up")]
    [InlineData("TO:")]
    [InlineData("TO: ***")]
    [InlineData("nothing to: see here")]
    public void ALineThatIsNotJustAMarkerAndAWord_IsProse(string line)
    {
        Assert.Null(TurnReply_Splitter.Read_Address_OrNull(line));

        var block = Assert.Single(TurnReply_Splitter.Split($"{line}\nand the rest"));
        Assert.Null(block.SourceKey);
    }

    [Theory]
    [InlineData("TO: imp-1", "imp-1")]
    [InlineData("to: imp-1", "imp-1")]
    [InlineData("  TO:   rev-2  ", "rev-2")]
    [InlineData("TO: **owner**", "owner")]
    public void AMarkerAndASingleWord_IsAnAddress(string line, string expected)
    {
        Assert.Equal(expected, TurnReply_Splitter.Read_Address_OrNull(line));
    }

    [Fact]
    public void AnEmptyBlock_IsNotAnEntry()
    {
        var blocks = TurnReply_Splitter.Split("TO: imp-1\n\nTO: owner\nStatus\n\nfine");

        Assert.Equal("owner", Assert.Single(blocks).SourceKey);
    }
}
