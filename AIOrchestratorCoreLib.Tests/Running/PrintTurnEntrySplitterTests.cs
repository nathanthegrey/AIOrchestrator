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

    [Fact]
    public void Empty_IsNamedEmpty()
    {
        Assert.Equal(PrintTurnEntry_Splitter.EMPTY_SUBJECT, PrintTurnEntry_Splitter.Split(null).Subject);
    }
}
