using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// The owner's complaint this exists for: a long supervisor message with buttons hanging off the
/// bottom and no visible question — "I have a multi choice, and no idea what is the question".
/// The buttons now ride on their own short message, and this builds what that message says.
/// </summary>
public class QuestionPromptBuilderTests
{
    [Fact]
    public void Build_ExplicitQuestionLine_Wins()
    {
        var prompt = QuestionPrompt_Builder.Build(
            ["Merge branch wf-perf into master now, or hold?"],
            "A long body. It even asks something else? Yes it does.");

        Assert.Equal("❓ Merge branch wf-perf into master now, or hold?", prompt);
    }

    [Fact]
    public void Build_SeveralQuestionLines_AreJoined()
    {
        var prompt = QuestionPrompt_Builder.Build(["Ship it now?", "Or wait for the review?"], "body");

        Assert.Equal("❓ Ship it now? Or wait for the review?", prompt);
    }

    [Fact]
    public void Build_NoQuestionLine_DerivesTheLastQuestionSentenceFromTheBody()
    {
        var body = """
        I finished the walk-forward run and the numbers look good.
        There were two regressions but both were in fixtures, not the engine.
        Do you want me to merge this into master?
        """;

        Assert.Equal("❓ Do you want me to merge this into master?", QuestionPrompt_Builder.Build([], body));
    }

    [Fact]
    public void Derive_TakesTheLAST_Question_NotTheFirst()
    {
        var body = "Should I start with the parser? Actually I did. Should I do the writer next?";

        Assert.Equal("Should I do the writer next?", QuestionPrompt_Builder.Derive_OrNull(body));
    }

    [Fact]
    public void Derive_StripsTheSpeakerPrefix()
    {
        Assert.Equal("Merge it?", QuestionPrompt_Builder.Derive_OrNull("🔴 Sup: Merge it?"));
    }

    /// <summary>
    /// Half a question is worse than the canned prompt — a paragraph-long "question" is exactly
    /// the wall of text the owner cannot answer from a lock screen.
    /// </summary>
    [Fact]
    public void Derive_RejectsAnOverlongSentence_RatherThanTruncatingIt()
    {
        var sprawling = $"{new string('x', QuestionPrompt_Builder.MAX_DERIVED_LENGTH + 20)}?";

        Assert.Null(QuestionPrompt_Builder.Derive_OrNull(sprawling));
        Assert.Equal($"❓ {QuestionPrompt_Builder.FALLBACK_PROMPT}", QuestionPrompt_Builder.Build([], sprawling));
    }

    [Fact]
    public void Derive_IgnoresQuestionMarksInsideFencedBlocks()
    {
        var body = """
        Here is the layout:

        ```
        | ready? | yes |
        ```

        Plain statement with no question.
        """;

        Assert.Null(QuestionPrompt_Builder.Derive_OrNull(body));
    }

    [Fact]
    public void Build_NothingToWorkWith_FallsBackToTheCannedPrompt()
    {
        Assert.Equal($"❓ {QuestionPrompt_Builder.FALLBACK_PROMPT}", QuestionPrompt_Builder.Build([], "No question here."));
        Assert.Equal($"❓ {QuestionPrompt_Builder.FALLBACK_PROMPT}", QuestionPrompt_Builder.Build([], ""));
    }

    [Fact]
    public void Build_BlankQuestionLines_AreIgnored()
    {
        Assert.Equal("❓ Real question?", QuestionPrompt_Builder.Build(["   ", "Real question?"], "body"));
    }

    /// <summary>
    /// After the tap the question must STAY visible with the answer under it: the Telegram toast is
    /// transient and the keyboard disappears, so this is the only lasting record of the choice.
    /// </summary>
    [Fact]
    public void Build_AnsweredText_KeepsTheQuestionAndRecordsTheChoice()
    {
        var answered = QuestionPrompt_Builder.Build_AnsweredText("❓ Merge now, or hold?", "Hold");

        Assert.Equal("❓ Merge now, or hold?\n\n✅ Hold", answered);
    }

    /// <summary>
    /// THE SAME RECORD FOR AN ANSWER THAT WAS TYPED. The owner answers by message whenever the reply
    /// needs more than a label, and that path used to stamp nothing at all — so scrolling back showed
    /// a question that still read as open. Owner, 2026-08-24: *"The unanswered question — or rather,
    /// the one I answered with a message instead of a tap — dated back further."*
    /// </summary>
    [Fact]
    public void Build_AnsweredByMessageText_RecordsTheirWords_NotAChoice()
    {
        var answered = QuestionPrompt_Builder.Build_AnsweredByMessageText("❓ Merge now, or hold?", "hold it, I want to read the diff first");

        Assert.Equal("❓ Merge now, or hold?\n\n✅ answered: hold it, I want to read the diff first", answered);
    }

    /// <summary>
    /// A typed answer can be a pasted conversation. The record must stay a record rather than become
    /// a second copy of the message, so only the first line survives and it is truncated.
    /// </summary>
    [Fact]
    public void Build_AnsweredByMessageText_TakesTheFirstLineAndTruncatesIt()
    {
        var pasted = $"{new string('x', QuestionPrompt_Builder.MAX_ANSWER_PREVIEW_LENGTH + 40)}\nand a second line";

        var answered = QuestionPrompt_Builder.Build_AnsweredByMessageText("❓ Which?", pasted);

        Assert.Equal(
            $"❓ Which?\n\n✅ answered: {new string('x', QuestionPrompt_Builder.MAX_ANSWER_PREVIEW_LENGTH)}…",
            answered);

        Assert.DoesNotContain("second line", answered, StringComparison.Ordinal);
    }

    /// <summary>
    /// A photo or a sticker leaves no usable preview. It still answered the question, so the record
    /// still has to say so — a tick with nothing after it would read as a rendering bug.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void Build_AnsweredByMessageText_WithNothingToPreview_StillSaysItWasAnswered(string ownerText)
    {
        var answered = QuestionPrompt_Builder.Build_AnsweredByMessageText("❓ Which?", ownerText);

        Assert.Equal($"❓ Which?\n\n✅ answered: {QuestionPrompt_Builder.ANSWERED_IN_WRITING}", answered);
    }
}
