using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE ONE RULE THAT DECIDES WHETHER THE OWNER WAITS AT ALL.
///
/// <para>
/// Measured on the VPS, 2026-09-09: an owner's Telegram message took 11–12 s median to reach the
/// supervisor's channel, and six of those seconds were the aggregation window — a quiet period
/// served by every message, so that the rare burst arrives as one turn. The owner's ruling that day
/// was to shrink the window to three seconds AND to skip it entirely for a message that is plainly
/// finished: one that ends in <c>.</c>, <c>?</c> or <c>!</c>, or that is a slash command.
/// </para>
/// <para>
/// The rule is a decider of its own rather than an <c>if</c> inside the buffer because it is the
/// half of the change an owner can argue with. "Finished" is a judgement about their typing habits,
/// not a property of the buffer, and it has to be readable — and changeable — without reading a
/// concurrency-sensitive class around it.
/// </para>
/// <para>
/// SINGLE is NOT this class's question. It decides whether ONE text looks finished;
/// <c>OwnerDeliveryBufferModel</c> is what knows whether that text is the only one buffered. Two
/// finished sentences that arrived in a burst still serve the window, because the second one is
/// evidence the first was not the whole thought.
/// </para>
/// </summary>
public class OwnerMessageCompleteDeciderTests
{
    [Theory]
    [InlineData("restart the crew.")]
    [InlineData("is the merge done?")]
    [InlineData("stop!")]
    [InlineData("what happened?!")]
    // Trailing whitespace and newlines are typing, not meaning — Telegram keeps them.
    [InlineData("restart the crew.\n")]
    [InlineData("  restart the crew.  ")]
    // A multi-line message is still one message; its LAST character is what says it is finished.
    [InlineData("first line\nand the point is this.")]
    public void AFinishedSentence_IsComplete(string text)
    {
        Assert.True(OwnerMessageComplete_Decider.Is_Complete(text), $"'{text}' should not have made the owner wait");
    }

    /// <summary>
    /// A SLASH COMMAND IS COMPLETE BY CONSTRUCTION. It is a word the app parses, never the opening
    /// of a thought, and it is the one owner input where the aggregation window buys literally
    /// nothing: nobody follows <c>/status</c> with a second half.
    /// </summary>
    [Theory]
    [InlineData("/status")]
    [InlineData("/cost")]
    [InlineData("/start the crm crew")]
    [InlineData("  /tokens")]
    public void ASlashCommand_IsComplete(string text)
    {
        Assert.True(OwnerMessageComplete_Decider.Is_Complete(text), $"'{text}' is a command and should not have waited");
    }

    /// <summary>
    /// THE CASE THE WINDOW EXISTS FOR. A line with no terminator is how a dictated thought arrives
    /// mid-sentence, and it is exactly where the owner's next three seconds are worth serving.
    /// </summary>
    [Theory]
    [InlineData("also")]
    [InlineData("and then we should")]
    [InlineData("look at the plan,")]
    [InlineData("the merge is done;")]
    [InlineData("wait…")]
    public void AnUnfinishedLine_IsNotComplete(string text)
    {
        Assert.False(OwnerMessageComplete_Decider.Is_Complete(text), $"'{text}' reads unfinished and must still serve the window");
    }

    /// <summary>
    /// EMPTY IS NOT COMPLETE, and the reason is not tidiness: an empty text has no last character to
    /// read, so a decider that answered "true" here would answer it from an index that does not
    /// exist. A caption-less photo and a stripped-down forward both arrive as one.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void NothingAtAll_IsNotComplete(string text)
    {
        Assert.False(OwnerMessageComplete_Decider.Is_Complete(text));
    }

    /// <summary>
    /// A SLASH THAT IS NOT A COMMAND. The rule is "starts with a slash", so a fraction or a path in
    /// the middle of a sentence must not be mistaken for one — and a message that merely CONTAINS a
    /// command word is still an unfinished sentence.
    /// </summary>
    [Theory]
    [InlineData("2/3 of the tests pass")]
    [InlineData("check /status when you can")]
    public void ASlashInTheMiddle_IsNotACommand(string text)
    {
        Assert.False(OwnerMessageComplete_Decider.Is_Complete(text));
    }
}
