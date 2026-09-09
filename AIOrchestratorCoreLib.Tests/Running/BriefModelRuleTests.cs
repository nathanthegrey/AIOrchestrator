using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The model a brief asks for. Four rules are pinned here because each of them, broken, costs
/// something different: the ACCEPT is the feature; the REFUSAL is the owner's standing rule about
/// fable, which the app enforces rather than documents (decision 21); the FENCE is the mistake
/// <c>TurnReply_Splitter</c> already made once, where a supervisor quoting the protocol back moved a
/// paragraph to a channel nobody meant; and the AUTHOR is the conflict of interest — the session
/// that benefits from the expensive model is not the session that pays for it.
/// </summary>
public class BriefModelRuleTests
{
    [Theory]
    [InlineData("MODEL: sonnet", "sonnet")]
    [InlineData("MODEL: opus", "opus")]
    [InlineData("MODEL: haiku", "haiku")]
    [InlineData("model: sonnet", "sonnet")]
    [InlineData("  MODEL:   Sonnet  ", "sonnet")]
    [InlineData("MODEL: **sonnet**", "sonnet")]
    public void ALineNamingAnAcceptedModel_IsTheModel(string line, string expected)
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(1, ChannelAuthors.Supervisor, "BRIEF — a one-line fix", $"go and do it\n\n{line}\n"));

        Assert.Equal(expected, resolved.Model);
        Assert.Null(resolved.RefusalReason);
    }

    /// <summary>The header is a line like any other: a supervisor can size the task in the subject.</summary>
    [Fact]
    public void TheMarkerIsReadFromTheSubjectToo()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(4, ChannelAuthors.Supervisor, "MODEL: sonnet", "a one-line fix in the loader"));

        Assert.Equal("sonnet", resolved.Model);
    }

    /// <summary>Nearly every entry ever written: no marker, nothing to say, no reason to log.</summary>
    [Fact]
    public void AnEntryWithNoMarker_SaysNothingAndComplainsAboutNothing()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(2, ChannelAuthors.Supervisor, "BRIEF — port the ledger", "the model of the ledger is the parser's business"));

        Assert.Null(resolved.Model);
        Assert.Null(resolved.RefusalReason);
    }

    /// <summary>
    /// The owner's standing rule, enforced where it can be. The reason must NAME fable: "model
    /// refused" is the silence decision 21 forbids, and the spelling comes from the constant the
    /// request reader already refuses on, so the two cannot drift apart.
    /// </summary>
    [Fact]
    public void ABriefAskingForFable_IsRefusedWithAReasonThatNamesIt()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(7, ChannelAuthors.Supervisor, "BRIEF — the hard one", "MODEL: fable"));

        Assert.Null(resolved.Model);
        Assert.NotNull(resolved.RefusalReason);
        Assert.Contains(OrchestrationRequests_Reader.FORBIDDEN_MEMBER_MODEL, resolved.RefusalReason);
        Assert.Contains("[7]", resolved.RefusalReason);
    }

    /// <summary>Capitalisation must not be a way past the rule.</summary>
    [Fact]
    public void FableIsRefusedWhateverItsCapitalisation()
    {
        Assert.Null(BriefModel_Rule.Resolve_FromEntry(Entry(8, ChannelAuthors.Supervisor, "BRIEF", "MODEL: Fable")).Model);
    }

    /// <summary>
    /// A marker line that names something unusable is REFUSED, never passed over: the turn still runs
    /// on the registered model, but the supervisor's unusable sentence reaches the log naming which
    /// predicate failed. Nothing here is guessed into a model — a typo must not be corrected for the
    /// owner behind their back.
    /// </summary>
    [Theory]
    [InlineData("MODEL: gpt-4")]
    [InlineData("MODEL: claude")]
    [InlineData("MODEL:")]
    [InlineData("MODEL: ***")]
    [InlineData("MODEL: whichever you think is cheapest")]
    public void AMarkerLineThatNamesNoModelThisAppRuns_IsRefusedWithAReason(string line)
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(9, ChannelAuthors.Supervisor, "BRIEF", line));

        Assert.Null(resolved.Model);
        Assert.NotNull(resolved.RefusalReason);
        Assert.Contains("[9]", resolved.RefusalReason);
    }

    /// <summary>The word must OPEN the line — "the model: whatever" is a sentence, not an instruction.</summary>
    [Theory]
    [InlineData("we picked the model: sonnet, for the record")]
    [InlineData("nothing about a model here")]
    public void AMarkerInTheMiddleOfASentence_IsProse(string line)
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(3, ChannelAuthors.Supervisor, "BRIEF", line));

        Assert.Null(resolved.Model);
        Assert.Null(resolved.RefusalReason);
    }

    /// <summary>
    /// The lesson TurnReply_Splitter learned for its own marker: a supervisor quoting the protocol,
    /// a member's report, or this rule's own documentation into a fence must not thereby move anyone
    /// onto another model.
    /// </summary>
    [Fact]
    public void AMarkerInsideACodeFence_IsQuotedText()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(5, ChannelAuthors.Supervisor, "BRIEF — the protocol", "the format is:\n\n```\nMODEL: sonnet\n```\n\nnow do the task"));

        Assert.Null(resolved.Model);
        Assert.Null(resolved.RefusalReason);
    }

    /// <summary>And a real marker after the fence closes is still a real marker.</summary>
    [Fact]
    public void AMarkerAfterTheFenceCloses_StillCounts()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(6, ChannelAuthors.Supervisor, "BRIEF", "```\nMODEL: opus\n```\n\nMODEL: sonnet"));

        Assert.Equal("sonnet", resolved.Model);
    }

    /// <summary>
    /// A supervisor that changes its mind in writing order has changed its mind: preferring the
    /// first line would silently obey a sentence its author had already corrected.
    /// </summary>
    [Fact]
    public void SeveralMarkers_TheLastOneWins()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(10, ChannelAuthors.Supervisor, "BRIEF", "MODEL: sonnet\n\non reflection this is the hard one\n\nMODEL: opus"));

        Assert.Equal("opus", resolved.Model);
    }

    /// <summary>A body marker supersedes the subject, because that is the order they were written in.</summary>
    [Fact]
    public void ABodyMarker_SupersedesTheSubject()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(11, ChannelAuthors.Supervisor, "BRIEF — MODEL: sonnet", "MODEL: opus"));

        Assert.Equal("opus", resolved.Model);
    }

    /// <summary>
    /// LAST WINS EVEN WHEN IT IS THE WORSE LINE. "The supervisor's latest word was unusable" and
    /// "the supervisor asked for sonnet" are different facts, and falling back to the accepted line
    /// above would report the second when only the first is true.
    /// </summary>
    [Fact]
    public void AnAcceptedLineFollowedByARefusedOne_IsARefusal()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(12, ChannelAuthors.Supervisor, "BRIEF", "MODEL: sonnet\n\nMODEL: fable"));

        Assert.Null(resolved.Model);
        Assert.NotNull(resolved.RefusalReason);
    }

    /// <summary>And the correction in the other direction is honoured with no complaint left over.</summary>
    [Fact]
    public void ARefusedLineFollowedByAnAcceptedOne_IsAccepted()
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(13, ChannelAuthors.Supervisor, "BRIEF", "MODEL: fable\n\non reflection:\n\nMODEL: opus"));

        Assert.Equal("opus", resolved.Model);
        Assert.Null(resolved.RefusalReason);
    }

    /// <summary>
    /// ONLY A SUPERVISOR MAY SET IT. A member asking for the expensive model on its own judgement is
    /// the conflict of interest the request reader already refuses; the owner is excluded too,
    /// because they have set-model and one decision must not have two mechanisms.
    /// </summary>
    [Theory]
    [InlineData(ChannelAuthors.Implementer)]
    [InlineData(ChannelAuthors.Reviewer)]
    [InlineData(ChannelAuthors.Solo)]
    [InlineData(ChannelAuthors.Owner)]
    [InlineData(ChannelAuthors.App)]
    public void AnEntryFromAnyoneButTheSupervisor_CannotSetTheModel(ChannelAuthors author)
    {
        var resolved = BriefModel_Rule.Resolve_FromEntry(Entry(14, author, "REPORT", "MODEL: opus"));

        Assert.Null(resolved.Model);
        Assert.Null(resolved.RefusalReason);
    }

    /// <summary>
    /// The dispatcher's whole question. Entries arrive in delivery order, so the last one to carry a
    /// marker is the supervisor's latest word; an entry carrying none says nothing either way.
    /// </summary>
    [Fact]
    public void AcrossABatch_TheLastEntryWithAMarkerWins()
    {
        IChannelEntry[] pending =
        [
            Entry(1, ChannelAuthors.Supervisor, "BRIEF", "MODEL: opus"),
            Entry(2, ChannelAuthors.Supervisor, "AMENDMENT", "MODEL: sonnet"),
            Entry(3, ChannelAuthors.Supervisor, "ONE MORE THING", "and mind the tests"),
        ];

        Assert.Equal("sonnet", BriefModel_Rule.Resolve_ForTurn(pending, "opus").Model);
    }

    /// <summary>Nobody asked: the turn runs on what the session was registered with.</summary>
    [Fact]
    public void WithNoMarkerAnywhere_TheTurnKeepsTheRegisteredModel()
    {
        IChannelEntry[] pending = [Entry(1, ChannelAuthors.Supervisor, "BRIEF", "go and do it")];

        var resolved = BriefModel_Rule.Resolve_ForTurn(pending, "opus");

        Assert.Equal("opus", resolved.Model);
        Assert.Null(resolved.RefusalReason);
    }

    /// <summary>
    /// A REFUSAL COSTS THE ASK, NEVER THE TURN. The session runs on its registered model and the
    /// reason goes to the log — the alternative, skipping the turn, would let a supervisor's typo
    /// stall an implementer.
    /// </summary>
    [Fact]
    public void ARefusedAsk_FallsBackToTheRegisteredModel_AndStillCarriesTheReason()
    {
        IChannelEntry[] pending = [Entry(1, ChannelAuthors.Supervisor, "BRIEF", "MODEL: fable")];

        var resolved = BriefModel_Rule.Resolve_ForTurn(pending, "sonnet");

        Assert.Equal("sonnet", resolved.Model);
        Assert.NotNull(resolved.RefusalReason);
    }

    /// <summary>An empty batch is the idle tick, and it must not disturb the session's model.</summary>
    [Fact]
    public void AnEmptyBatch_KeepsTheRegisteredModel()
    {
        Assert.Equal("haiku", BriefModel_Rule.Resolve_ForTurn([], "haiku").Model);
        Assert.Null(BriefModel_Rule.Resolve_ForTurn([], null).Model);
    }

    /// <summary>
    /// THE ACCEPTED WORDS ARE THE ONES THE APP ALREADY WRITES, not a list this rule invented: every
    /// shipped role default must be a word a brief could also ask for, or the two vocabularies have
    /// already drifted and a supervisor reading config.json would be refused for copying it.
    /// </summary>
    [Fact]
    public void EveryShippedRoleDefault_IsAWordABriefMayAskFor()
    {
        string[] shipped =
        [
            OrchestratorConfig_Factory.DEFAULT_SUPERVISOR_MODEL,
            OrchestratorConfig_Factory.DEFAULT_IMPLEMENTER_MODEL,
            OrchestratorConfig_Factory.DEFAULT_REVIEWER_MODEL,
            OrchestratorConfig_Factory.DEFAULT_SOLO_MODEL,
            OrchestratorConfig_Factory.DEFAULT_GENERAL_SUPERVISOR_MODEL,
            OrchestratorConfig_Factory.DEFAULT_COMMUNICATOR_MODEL,
        ];

        foreach (var word in shipped)
            Assert.Contains(word, BriefModel_Rule.ACCEPTED_MODELS);
    }

    /// <summary>A refusal a person has to act on names the alternatives.</summary>
    [Fact]
    public void TheAcceptedWords_AreSpelledOutForTheReader()
    {
        Assert.Equal("opus, sonnet or haiku", BriefModel_Rule.Describe_Accepted());
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string body)
    {
        var word = ChannelAuthor_Words.Get_Word(author);
        var raw = $"## [{index}] FROM {word} — 2026-09-09 10:00 — {subject}\n\n{body}";

        return ChannelEntry_Factory.Create(index, author, "2026-09-09 10:00", subject, body, raw);
    }
}
