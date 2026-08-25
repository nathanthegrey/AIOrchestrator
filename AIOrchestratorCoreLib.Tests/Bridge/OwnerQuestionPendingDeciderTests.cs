using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// ❓ MEANT "THE SESSION SPOKE LAST", AND THE OWNER READ IT AS "A QUESTION IS WAITING FOR ME".
///
/// 2026-08-21: a topic carried ❓ while the session under it was correctly reporting no open
/// questions. Both were right — the glyph was driven by OwnerOwesReply_Decider, a whose-move-is-it
/// test built for the stall alert and reused for the topic name as if the two facts were the same
/// one. Their words: *"If there are no questions, why did they put the question mark in the topic
/// name? That emoji is reserved for when there's a non-blocking question."*
///
/// These tests pin the distinction that was missing: a report is not a question, and a question
/// stays outstanding until the OWNER answers it.
/// </summary>
public class OwnerQuestionPendingDeciderTests
{
    static IChannelEntry Entry(int index, ChannelAuthors author, string raw)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-08-21 10:00", "subject", raw, raw);
    }

    /// <summary>
    /// THE BUG, PINNED. A plain progress report by the session is exactly what used to light the
    /// glyph, and it is the case the owner complained about.
    /// </summary>
    [Fact]
    public void APlainReportBySoloIsNotAQuestion()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Owner, "go ahead"),
            Entry(2, ChannelAuthors.Solo, "fix landed, 214 tests green, branch ready"),
        ]));
    }

    [Fact]
    public void AnExplicitQuestionMarkerIsAQuestion()
    {
        Assert.True(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo, "QUESTION: merge now or hold?\nOPTION: merge\nOPTION: hold"),
        ]));
    }

    /// <summary>
    /// PROSE NO LONGER LIGHTS THE GLYPH — this case is inverted from what it used to assert.
    ///
    /// The owner's ruling, 2026-08-25: *"The question mark in the topic name should be assigned when
    /// there's an intention from the sup/solo to ask a question. It's not that it should be
    /// interpreted indirectly based on the presence of a ? here and there that could mean anything."*
    ///
    /// THE TRADE IS REAL AND WORTH IT. A session that asks in prose and never writes `QUESTION:` now
    /// gets no glyph — but the question still reaches the owner's PHONE, because
    /// OwnerPush_Policy.Asks_InProse is unchanged and the push is deliberately biased the other way.
    /// What is lost is a mark on the topic list; what is bought is that the mark never lies.
    /// </summary>
    [Fact]
    public void AProseQuestion_DoesNotLightTheGlyph_ItIsNotDeclared()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Supervisor, "should I drop the legacy path while I am in here?"),
        ]));
    }

    /// <summary>
    /// THE OWNER'S OWN CASE, and the one that made this unavoidable. `supervisor.md` tells a
    /// supervisor to open its reply by quoting them (`Owner: &lt;their text&gt;`), so the instant the owner
    /// asked anything, the supervisor's next entry carried a line ending in `?` and lit the glyph.
    /// Their words: *"Even when I'm the one asking the question"*.
    /// </summary>
    [Fact]
    public void TheSupervisorQuotingTheOwnersQuestion_DoesNotLightTheGlyph()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Supervisor, "Owner: can you also check the splash timing?\nOn it — reading the launch path now."),
        ]));
    }

    /// <summary>
    /// BLOCKED ON OWNER is a declaration too, and the strongest one — it says the work has STOPPED.
    /// It lights the glyph without needing a `QUESTION:` line beside it.
    /// </summary>
    [Fact]
    public void BlockedOnOwner_LightsTheGlyph_WithoutAQuestionMarker()
    {
        Assert.True(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Supervisor, "BLOCKED ON OWNER: I need the API token before I can go on."),
        ]));
    }

    /// <summary>
    /// THE OTHER HALF OF THE OWNER'S ASK: *"when they receive it, regardless of how, they modify the
    /// status as 'info received' one more time in some unequivocal way."*
    ///
    /// An answer does not always arrive as a `FROM owner` entry — a tapped button, a reply in the
    /// terminal, an answer given in another topic — so the session can clear the glyph by SAYING so,
    /// instead of the glyph outliving a question that is already settled.
    /// </summary>
    [Fact]
    public void AnAnsweredMarker_ClearsTheGlyph_EvenWithNoOwnerEntry()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo, "QUESTION: merge now or hold?\nOPTION: merge\nOPTION: hold"),
            Entry(2, ChannelAuthors.Solo, "ANSWERED — they tapped merge; landing it now."),
        ]));
    }

    /// <summary>
    /// And the marker is read the way every marker here is — a whole token in the SUBJECT or at the
    /// START of a body line. Discussing the word mid-sentence must not clear a live question, which
    /// is the same class of mistake the `?` inference was.
    /// </summary>
    [Fact]
    public void TheWordAnsweredMidSentence_DoesNotClearTheGlyph()
    {
        Assert.True(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo, "QUESTION: merge now or hold?\nOPTION: merge\nOPTION: hold"),
            Entry(2, ChannelAuthors.Solo, "still waiting — nothing has been answered yet on my side."),
        ]));
    }

    /// <summary>
    /// THE ONE THAT MADE ❓ PERMANENT. The session's answer to the owner used to re-light the glyph
    /// the instant it was written, so answering them marked the topic as owing them an answer.
    /// </summary>
    [Fact]
    public void AnsweringTheOwnerDoesNotRelightTheGlyph()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo, "QUESTION: merge now or hold?\nOPTION: merge\nOPTION: hold"),
            Entry(2, ChannelAuthors.Owner, "hold"),
            Entry(3, ChannelAuthors.Solo, "held. moving to the next line."),
        ]));
    }

    /// <summary>
    /// A question is not cancelled by the session carrying on talking. It stays outstanding over any
    /// number of later reports, because the owner still has not answered it — the scan walks back
    /// past them rather than stopping at the newest entry.
    /// </summary>
    [Fact]
    public void AQuestionSurvivesLaterReportsUntilTheOwnerAnswers()
    {
        Assert.True(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo, "QUESTION: merge now or hold?\nOPTION: merge\nOPTION: hold"),
            Entry(2, ChannelAuthors.Solo, "meanwhile: the other two lines are done"),
            Entry(3, ChannelAuthors.Solo, "and the suite is green"),
        ]));
    }

    /// <summary>
    /// APP ENTRIES NEITHER ASK NOR ANSWER. A STATUS push lands on its own schedule; if one could
    /// clear a pending question the feature would switch itself off, and the app's own status text
    /// is full of question marks besides.
    /// </summary>
    [Fact]
    public void AnAppEntryAfterAQuestionDoesNotClearIt()
    {
        Assert.True(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo, "QUESTION: merge now or hold?\nOPTION: merge\nOPTION: hold"),
            Entry(2, ChannelAuthors.App, "STATUS — 2/3 done. anything else?"),
        ]));
    }

    [Fact]
    public void AnAppEntryOnItsOwnAsksNothing()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.App, "GO AHEAD — resume. did the limit reset?"),
        ]));
    }

    /// <summary>
    /// A question mark inside a fenced block is a snippet, not an ask — this is OwnerPush_Policy's
    /// rule, and the glyph inherits it by using that same reader rather than a second opinion.
    /// </summary>
    [Fact]
    public void AQuestionMarkInsideAFencedBlockIsNotAQuestion()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo, "landed:\n```\nvar x = a ? b : c;\n```\nall green"),
        ]));
    }

    /// <summary>
    /// THE LEDGER MARKER, VERBATIM FROM strategy-lab-6. The owner, 2026-08-21: *"after I put it
    /// in pc mode a ? icon appeared in the name for no reason"*. The only session entry on that
    /// channel was a STANDING BY report, and the mark in it was `[?]` — the ledger's own
    /// blocked-on-owner marker, which every role command in this kit teaches sessions to write.
    ///
    /// This is why the glyph looked like it followed /pc: /pc is what made the session report, and
    /// any report mentioning the marker lit it. Nothing about presence was involved at all.
    /// </summary>
    [Fact]
    public void ALedgerMarkerInProseDoesNotLightTheGlyph()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo,
                "STANDING BY.\n\nNo task yet. Owner is at the terminal (entry [3]) — I asked "
                + "them there, in prose, what to work on. Ledger line sits at [?] until they answer."),
        ]));
    }

    /// <summary>
    /// THE SECOND REPORT, ALSO VERBATIM — this session's own entry, minutes later: *"you just
    /// put ? in the topic name"*. Here the mark is a NOUN, the name of the glyph being looked at. A
    /// reader that cannot tell a noun from an ask will keep finding new ways to be wrong, which is
    /// why the test is the SHAPE of the mark rather than a list of words to exclude.
    /// </summary>
    [Fact]
    public void TheMarkAsANounDoesNotLightTheGlyph()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo,
                "Investigating the ? glyph, the terminal rename and the hook in parallel now."),
        ]));
    }

    /// <summary>
    /// The same entry, now correctly SILENT: ledger prose plus a prose question is still nothing the
    /// session DECLARED. Under the old reader this lit the glyph on the trailing `?` alone. If it
    /// genuinely wants an answer it writes `QUESTION:`, which the case above pins.
    /// </summary>
    [Fact]
    public void ProseAboutTheLedgerMarker_StillDoesNotLightTheGlyph()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo,
                "Ledger line sits at [?] until they answer.\nShould I take it off your plate?"),
        ]));
    }

    /// <summary>An empty channel asks nothing — a brand-new orchestration shows no glyph.</summary>
    [Fact]
    public void AnEmptyChannelAsksNothing()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide([]));
    }

    /// <summary>The owner's own question is not the app asking THEM something.</summary>
    [Fact]
    public void TheOwnersOwnQuestionDoesNotLightTheGlyph()
    {
        Assert.False(OwnerQuestionPending_Decider.Decide(
        [
            Entry(1, ChannelAuthors.Solo, "starting on the crash"),
            Entry(2, ChannelAuthors.Owner, "how is it going?"),
        ]));
    }
}
