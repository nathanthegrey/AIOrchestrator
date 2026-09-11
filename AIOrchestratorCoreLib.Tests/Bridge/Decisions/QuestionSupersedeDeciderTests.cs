using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Bridge.EngineState;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Decisions;

/// <summary>
/// The three rules that stop a question being asked twice — each one a link in the fincanva-6 chain
/// of 2026-09-11 (see <see cref="QuestionSupersede_Decider"/>).
/// </summary>
public class QuestionSupersedeDeciderTests
{
    static readonly DateTime T0 = new(2026, 9, 11, 8, 35, 0, DateTimeKind.Utc);

    static OpenQuestionRecord Question(long messageId, DateTime askedUtc, string? prompt = null)
    {
        return new OpenQuestionRecord { MessageId = messageId, OrchId = "fincanva-6", Text = $"{prompt ?? "q"}\n\n1) a\n2) b", Prompt = prompt, AskedUtc = askedUtc };
    }

    [Theory]
    [InlineData("devo fare solo smoke?")]
    [InlineData("Potuto lavoro finito, devo fare solo smoke?")]
    public void AnOwnerMessageThatAsks_IsNotAReply(string text)
    {
        Assert.False(QuestionSupersede_Decider.Counts_AsAReply(text));
    }

    [Theory]
    [InlineData("start the build and hold the merge")]
    [InlineData("B as I already said")]
    public void AnOwnerMessageThatDoesNotAsk_IsAReply(string text)
    {
        Assert.True(QuestionSupersede_Decider.Counts_AsAReply(text));
    }

    [Fact]
    public void WithNoReply_NothingIsSuperseded()
    {
        Assert.Empty(QuestionSupersede_Decider.Select_ToSupersede([Question(1, T0)], ownerRepliedUtc: null, awaitingReadBack: []));
    }

    [Fact]
    public void AQuestionAskedBeforeTheReply_IsSuperseded_AndOneAskedAfterIt_IsNot()
    {
        var before = Question(1, T0);
        var after = Question(2, T0.AddMinutes(5));

        var superseded = QuestionSupersede_Decider.Select_ToSupersede([before, after], T0.AddMinutes(2), awaitingReadBack: []);

        Assert.Equal([1L], superseded.Select(question => question.MessageId));
    }

    [Fact]
    public void AQuestionTappedAndAwaitingItsCode_IsNeverSuperseded()
    {
        var tapped = Question(1, T0);
        var untapped = Question(2, T0.AddMinutes(1));

        var superseded = QuestionSupersede_Decider.Select_ToSupersede([tapped, untapped], T0.AddMinutes(2), awaitingReadBack: [1L]);

        Assert.Equal([2L], superseded.Select(question => question.MessageId));
    }

    [Fact]
    public void ARepeat_IsTheSameQuestionLine_WhateverItsCaseAndSpacing()
    {
        var open = Question(7, T0, "Unisco FIN-D-302a a dev, così lo trovi su staging per lo smoke?");

        var repeat = QuestionSupersede_Decider.Find_Repeat_OrNull([open], "unisco FIN-D-302a  a dev,\ncosì lo trovi su staging per lo smoke?");

        Assert.Same(open, repeat);
    }

    [Fact]
    public void ADifferentQuestion_IsNotARepeat()
    {
        var open = Question(7, T0, "Unisco FIN-D-302a a dev?");

        Assert.Null(QuestionSupersede_Decider.Find_Repeat_OrNull([open], "Unisco FIN-D-302b a dev?"));
    }

    [Fact]
    public void AQuestionSavedWithoutItsLine_IsNeverARepeat()
    {
        // Written by a build before the line was remembered: nothing to compare is not a match.
        var open = Question(7, T0, prompt: null);

        Assert.Null(QuestionSupersede_Decider.Find_Repeat_OrNull([open], "q"));
    }
}
