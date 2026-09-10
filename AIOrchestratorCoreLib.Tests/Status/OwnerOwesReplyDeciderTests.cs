using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// WHICH QUESTION THE OWNER STILL OWES AN ANSWER TO — and, far more often, none.
///
/// <para>
/// THE FIRST RULE (2026-08-15) was "alert only if I owe you a reply", after the ⚠️ fired on plain
/// silence: *"quiet for 28 min and no session is working — text it to wake it up"* on two topics,
/// answered *"Makes no sense."* The channel was quiet because THEY had stopped texting.
/// </para>
/// <para>
/// THE SECOND RULE (2026-09-09) is this file's subject, and it came from the same alert getting it
/// wrong the other way. "The session spoke last" is not a debt: on 2026-09-09 the owner was told
/// *"has been waiting on your reply for 25 min"* at 17:30 — thirty minutes after the supervisor
/// wrote **"Nothing more needed from you"** — then again at 18:38, while the supervisor was paused
/// for a usage limit, and again at 20:25; three more on a second topic. Six false alerts in an
/// evening, each indistinguishable from a real one, which is what makes them expensive.
/// </para>
/// <para>
/// So the decider now answers WHICH question, by entry index: only a declared question counts, and
/// the index is what lets the caller alert once per question instead of once per stretch of silence.
/// </para>
/// </summary>
public class OwnerOwesReplyDeciderTests
{
    const string QUESTION_BODY = "QUESTION: merge wf-perf now?\nOPTION: Merge it\nOPTION: Hold";
    const string BLOCKED_BODY = "BLOCKED ON OWNER — I need the staging token before anything else moves.";
    const string PLAIN_BODY = "Two fixes landed and the suite is green. Nothing more needed from you.";

    static IChannelEntry Entry(int index, ChannelAuthors author, string body = PLAIN_BODY)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-09-09 17:00", "subject", body, $"## [{index}] FROM {author}\n{body}");
    }

    [Fact]
    public void ADeclaredQuestionFromTheSupervisor_IsWhatTheOwnerOwes()
    {
        Assert.Equal(2, OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull(
            [Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Supervisor, QUESTION_BODY)]));
    }

    [Fact]
    public void ABlockedOnOwnerEntry_CountsAsAQuestion()
    {
        Assert.Equal(2, OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull(
            [Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Solo, BLOCKED_BODY)]));
    }

    /// <summary>
    /// THE DEFECT THIS FILE EXISTS FOR: the supervisor's last word was a report, and the old rule
    /// read it as a debt. The owner's example is in the class summary — an alert thirty minutes
    /// after "Nothing more needed from you".
    /// </summary>
    [Fact]
    public void APlainReportFromTheSupervisor_IsNotADebt()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull(
            [Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Supervisor)]));
    }

    [Fact]
    public void TheOwnerSpokeLast_TheyOweNothing()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull(
            [Entry(1, ChannelAuthors.Supervisor, QUESTION_BODY), Entry(2, ChannelAuthors.Owner)]));
    }

    /// <summary>
    /// A question the supervisor has since moved past is not revived. They asked, then reported
    /// something else — the owner is answering the conversation, not an archive.
    /// </summary>
    [Fact]
    public void AQuestionBehindALaterPlainEntry_IsNotOwedAnyMore()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull(
        [
            Entry(1, ChannelAuthors.Owner),
            Entry(2, ChannelAuthors.Supervisor, QUESTION_BODY),
            Entry(3, ChannelAuthors.Supervisor),
        ]));
    }

    /// <summary>
    /// APP ENTRIES ARE SKIPPED, unchanged from the first rule: the app writes precisely when
    /// somebody has been waiting, so counting one as the last word let its own status push silence
    /// the alert — a feature quietly disabling itself.
    /// </summary>
    [Fact]
    public void AnAppEntryAfterTheQuestion_DoesNotCancelTheDebt()
    {
        Assert.Equal(2, OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull(
        [
            Entry(1, ChannelAuthors.Owner),
            Entry(2, ChannelAuthors.Supervisor, QUESTION_BODY),
            Entry(3, ChannelAuthors.App),
        ]));
    }

    [Fact]
    public void AnAppEntryAfterTheOwner_DoesNotCreateADebt()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull(
            [Entry(1, ChannelAuthors.Supervisor, QUESTION_BODY), Entry(2, ChannelAuthors.Owner), Entry(3, ChannelAuthors.App)]));
    }

    [Fact]
    public void NobodyHasSpokenYet_NothingIsOwed()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull([]));
    }

    /// <summary>
    /// PROSE ENDING IN '?' IS DELIBERATELY NOT A QUESTION HERE, though the push filter treats it as
    /// one: pushing an ask-shaped line costs one message the owner can ignore, alerting on it costs
    /// a ⚠️ every 25 minutes about a rhetorical question. Their own ruling on the topic glyph
    /// (2026-08-25) drew the same line: *"it's not that it should be interpreted indirectly based on
    /// the presence of a ? here and there that could mean anything."*
    /// </summary>
    [Fact]
    public void ARhetoricalQuestionInProse_IsNotADeclaredQuestion()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestionIndex_OrNull(
            [Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Supervisor, "Why did that fail? Because the token was stale. Fixed.")]));
    }

    [Fact]
    public void Is_RealQuestion_ReadsTheTwoDeclaredShapesAndNothingElse()
    {
        Assert.True(OwnerOwesReply_Decider.Is_RealQuestion(QUESTION_BODY));
        Assert.True(OwnerOwesReply_Decider.Is_RealQuestion(BLOCKED_BODY));
        Assert.True(OwnerOwesReply_Decider.Is_RealQuestion("blocked on owner — lowercase still counts"));
        Assert.False(OwnerOwesReply_Decider.Is_RealQuestion(PLAIN_BODY));
        Assert.False(OwnerOwesReply_Decider.Is_RealQuestion(""));
    }
}
