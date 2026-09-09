using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.StatePack;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// The brief is the one entry a fresh member cannot do without, and the one the 4a fallback ("the
/// last three entries") lost first. These pin the two rules: a task marker wins over everything
/// later, and without markers the longest recent supervisor entry stands in.
/// </summary>
public class BriefFinderTests
{
    [Fact]
    public void Find_TakesTheLatestSupervisorEntryWithATaskMarker_EvenWhenLaterEntriesExist()
    {
        var history = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "BRIEF — port the ledger", "long body of the first brief ....."),
            Entry(2, ChannelAuthors.Implementer, "imp-1 online", "ready"),
            Entry(3, ChannelAuthors.Supervisor, "REVIEW FIN-D-1 @ abc1234", "scope + depth"),
            Entry(4, ChannelAuthors.Implementer, "Committed abc1234", "report"),
            Entry(5, ChannelAuthors.Supervisor, "Accepted — good work", "a short acknowledgement"),
            Entry(6, ChannelAuthors.App, "[agent] turn_ended imp-1 turn 4 — success", "request_id: ..."),
        };

        var brief = Brief_Finder.Find_OrNull(history);

        Assert.NotNull(brief);
        Assert.Equal(3, brief!.Index);
    }

    [Fact]
    public void Find_WithoutMarkers_TakesTheLongestRecentSupervisorEntry_LaterWinsATie()
    {
        var history = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "Here is what I need", new string('x', 400)),
            Entry(2, ChannelAuthors.Implementer, "imp-1 online", "ready"),
            Entry(3, ChannelAuthors.Supervisor, "ok", "short"),
            Entry(4, ChannelAuthors.Owner, "hello", new string('o', 2000)),
        };

        var brief = Brief_Finder.Find_OrNull(history);

        Assert.NotNull(brief);
        Assert.Equal(1, brief!.Index);
    }

    [Fact]
    public void Find_FallbackLooksOnlyAtTheRecentWindow()
    {
        var entries = new List<IChannelEntry> { Entry(1, ChannelAuthors.Supervisor, "very old and very long", new string('x', 5000)) };

        for (var i = 2; i <= 2 + Brief_Finder.FALLBACK_WINDOW; i++)
            entries.Add(Entry(i, i % 2 == 0 ? ChannelAuthors.Implementer : ChannelAuthors.Supervisor, $"entry {i}", "short"));

        var brief = Brief_Finder.Find_OrNull(entries);

        Assert.NotNull(brief);
        Assert.NotEqual(1, brief!.Index);
        Assert.Equal(ChannelAuthors.Supervisor, brief.Author);
    }

    [Fact]
    public void Find_WithNoSupervisorEntry_IsNull()
    {
        var history = new[]
        {
            Entry(1, ChannelAuthors.Owner, "BRIEF — the owner uses the word too", "not a supervisor"),
            Entry(2, ChannelAuthors.Implementer, "imp-1 online", "ready"),
        };

        Assert.Null(Brief_Finder.Find_OrNull(history));
    }

    [Theory]
    [InlineData("BRIEF — integrate the moved dev", true)]
    [InlineData("  final review — FIN-D-282a", true)]
    [InlineData("FINDINGS — ten from rev-8", true)]
    [InlineData("GO AHEAD — resume", true)]
    [InlineData("Accepted at c4b8edce2", false)]
    [InlineData("STANDING BY — waiting", false)]
    [InlineData("A briefing on the review policy", false)]
    public void HasTaskMarker_MatchesTheSubjectOpening_CaseInsensitive(string subject, bool expected)
    {
        Assert.Equal(expected, Brief_Finder.Has_TaskMarker(subject));
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string body)
    {
        var word = ChannelAuthor_Words.Get_Word(author);
        var raw = $"## [{index}] FROM {word} — 2026-09-09 10:00 — {subject}\n\n{body}";
        return ChannelEntry_Factory.Create(index, author, "2026-09-09 10:00", subject, body, raw);
    }
}
