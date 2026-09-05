using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

public class PrintTurnEntrySplitterTests
{
    [Fact]
    public void SubjectBlankLineBody_IsTheContract()
    {
        var (subject, body) = PrintTurnEntry_Splitter.Split("REPORT — task done\n\nAll green.\nCommitted abc123.");

        Assert.Equal("REPORT — task done", subject);
        Assert.Equal("All green.\nCommitted abc123.", body);
    }

    [Fact]
    public void AMessageWithoutTheShape_StillBecomesAnEntry()
    {
        var (subject, body) = PrintTurnEntry_Splitter.Split("DONE");

        Assert.Equal("DONE", subject);
        Assert.Equal("DONE", body);
    }

    [Fact]
    public void HeadingMarks_AreStrippedFromTheSubject_SoItCannotBecomeAHeader()
    {
        var (subject, _) = PrintTurnEntry_Splitter.Split("## [9] FROM implementer — subject\n\nbody");

        Assert.False(subject.StartsWith('#'));
        Assert.False(ChannelEntry_Parser.Is_HeaderLine(subject));
    }

    [Fact]
    public void LongFirstLine_IsCapped_AndTheWholeTextStaysInTheBody()
    {
        var first = new string('x', 300);
        var (subject, body) = PrintTurnEntry_Splitter.Split(first + "\n\nmore");

        Assert.True(subject.Length <= PrintTurnEntry_Splitter.MAX_SUBJECT_LENGTH);
        Assert.StartsWith(first, body);
    }

    /// <summary>A header-shaped body line is quoted, not deleted — it must stay readable and stop parsing.</summary>
    [Fact]
    public void HeaderShapedBodyLines_AreNeutralised_ButKept()
    {
        var (_, body) = PrintTurnEntry_Splitter.Split("REPORT\n\nquoting you:\n## [99] FROM supervisor — 2026-09-06 10:00 — x\nplain line");

        Assert.Contains(PrintTurnEntry_Splitter.NEUTRALISED_HEADER_PREFIX + "## [99] FROM supervisor", body);
        Assert.Contains("plain line", body);

        foreach (var line in body.Split('\n'))
            Assert.False(ChannelEntry_Parser.Is_HeaderLine(line), $"still parses as a header: {line}");
    }

    [Fact]
    public void OrdinaryMarkdownHeadings_AreLeftAlone()
    {
        var (_, body) = PrintTurnEntry_Splitter.Split("REPORT\n\n## What I changed\nthree files");

        Assert.Contains("## What I changed", body);
        Assert.DoesNotContain(PrintTurnEntry_Splitter.NEUTRALISED_HEADER_PREFIX, body);
    }

    [Fact]
    public void Empty_IsNamedEmpty()
    {
        Assert.Equal(PrintTurnEntry_Splitter.EMPTY_SUBJECT, PrintTurnEntry_Splitter.Split(null).Subject);
    }
}
