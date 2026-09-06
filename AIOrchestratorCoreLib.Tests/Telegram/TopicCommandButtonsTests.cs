using System.Text;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The owner wants the commands they reach for constantly to be permanent tappable furniture in
/// every topic — an inline keyboard on the status line AND a bar above the input box.
///
/// The same four commands rendered twice and parsed once is three places to drift, and every drift
/// is silent on the owner's side: a button that renders and does nothing, or a bar offering a verb
/// the lexer no longer knows. These tests pin the three to one array.
/// </summary>
public class TopicCommandButtonsTests
{
    /// <summary>Telegram's hard cap on callback_data — a payload over it is rejected at send time, on the phone.</summary>
    const int TELEGRAM_CALLBACK_DATA_BYTE_LIMIT = 64;

    /// <summary>A label wider than this stops being readable on a phone and starts wrapping.</summary>
    const int MAX_LABEL_LENGTH = 20;

    /// <summary>
    /// /refresh joined on the owner's call, 2026-08-25: *"It happens so often that the question mark
    /// gets stuck that this command should be one of the main command buttons always present."*
    /// </summary>
    [Fact]
    public void TheCommands_AreTheOnesTheOwnerAskedFor_InDisplayOrder()
    {
        Assert.Equal(new[] { "screen", "show", "merge", "test", "refresh" }, TopicCommandButtons.Commands);
    }

    [Fact]
    public void TheInlineButtons_FollowTheSameOrderAsTheCommands()
    {
        var built = TopicCommandButtons.Build_ForTopic(4242L);

        Assert.Equal(TopicCommandButtons.Commands.Count, built.Count);
        Assert.Equal(
            TopicCommandButtons.Commands,
            built.Select(button => TopicCommandButtons.Parse_OrNull(button.Data)!.Value.Command).ToArray());
    }

    // ---------------------------------------------------------------------------------------
    // Round trip
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The tap has to come back as the command it was drawn for, in the topic it was drawn in — a
    /// tap arrives with no text to infer either from, so if the payload does not carry them nothing
    /// downstream can reconstruct them.
    ///
    /// 0 is the General topic, which has no thread id: it must survive as 0, the convention the
    /// hold button and the receipt registry already share.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(4242L)]
    [InlineData(long.MaxValue)]
    public void EveryButton_RoundTripsWithItsTopic(long messageThreadId)
    {
        var built = TopicCommandButtons.Build_ForTopic(messageThreadId);

        for (var i = 0; i < built.Count; i++)
            Assert.Equal((TopicCommandButtons.Commands[i], messageThreadId), TopicCommandButtons.Parse_OrNull(built[i].Data));
    }

    /// <summary>
    /// A negative thread id cannot come from Telegram, whose ids are positive — but the type is
    /// long and this parser's null means "NOT OURS, fall through to the next handler". A payload
    /// that plainly is ours, rejected, falls through to handlers that will not recognise it either:
    /// the owner taps and nothing happens, silently. So the id is carried back verbatim and the
    /// component that holds the topic roster gets to say the topic does not exist, out loud.
    /// </summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(-4242L)]
    [InlineData(long.MinValue)]
    public void ANegativeTopic_IsCarriedBackVerbatimRatherThanSwallowed(long messageThreadId)
    {
        var built = TopicCommandButtons.Build_ForTopic(messageThreadId);

        Assert.Equal(("screen", messageThreadId), TopicCommandButtons.Parse_OrNull(built[0].Data));
    }

    // ---------------------------------------------------------------------------------------
    // Telegram's limits
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 64 BYTES, not characters, and the rejection happens at send time on the phone where nothing
    /// here can see it. long.MaxValue and long.MinValue are the widest ids the type can hold.
    /// </summary>
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(0L)]
    public void EveryPayload_FitsTelegramsSixtyFourByteLimit(long messageThreadId)
    {
        foreach (var (data, _) in TopicCommandButtons.Build_ForTopic(messageThreadId))
            Assert.True(
                Encoding.UTF8.GetByteCount(data) <= TELEGRAM_CALLBACK_DATA_BYTE_LIMIT,
                $"callback data too long ({Encoding.UTF8.GetByteCount(data)} bytes): '{data}'");
    }

    /// <summary>
    /// The label is read by a HUMAN on a phone: an emoji to find it with, then the slash command so
    /// the button's effect is unambiguous. A glyph alone leaves the owner guessing which of two
    /// similar pictures merges and which closes.
    /// </summary>
    [Fact]
    public void EveryLabel_IsShortAndNamesItsCommand()
    {
        foreach (var (data, label) in TopicCommandButtons.Build_ForTopic(7L))
        {
            var command = TopicCommandButtons.Parse_OrNull(data)!.Value.Command;

            Assert.EndsWith("/" + command, label, StringComparison.Ordinal);
            Assert.True(label.Length < MAX_LABEL_LENGTH, $"label too long for a phone: '{label}'");

            // Emoji first, then the command — the eye scans the pictures, not the text.
            Assert.NotEqual('/', label[0]);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Everything that is not ours
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE OTHER BUTTON FAMILIES ALREADY ON THIS BRIDGE. A tap this class cannot read must fall
    /// through to its own handler untouched — swallowing an "opt-{n}" as a malformed command would
    /// eat the owner's answer to a question, the one tap in this system that cannot be repeated.
    /// </summary>
    [Theory]
    [InlineData("hold:4242")]           // HoldButton_Data — pause delivery
    [InlineData("go:0")]                // HoldButton_Data — release
    [InlineData("close-yes-9f1c4a2b8d3e4f5a6b7c8d9e0f1a2b3c")]
    [InlineData("close-no-9f1c4a2b8d3e4f5a6b7c8d9e0f1a2b3c")]
    [InlineData("opt-0")]               // the single-use option registry
    [InlineData("opt-17")]
    public void TheOtherButtonFamilies_AreNotOurs(string callbackData)
    {
        Assert.Null(TopicCommandButtons.Parse_OrNull(callbackData));
    }

    /// <summary>
    /// And the other direction, because non-collision is a property of the PAIR: the hold parser
    /// must not claim our payloads either. Both prefixes have to stay unreadable to the other, or
    /// the parse ORDER in the tap handler silently decides what a payload means.
    /// </summary>
    [Fact]
    public void TheHoldParser_DoesNotClaimOurPayloads()
    {
        foreach (var (data, _) in TopicCommandButtons.Build_ForTopic(4242L))
            Assert.Null(HoldButton_Data.Parse_OrNull(data));
    }

    /// <summary>
    /// Malformed input returns null; it never throws. This runs on a wire payload, and an exception
    /// on the tap path takes down the handling of every OTHER tap in the same batch.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cmd:")]                    // no verb, no topic
    [InlineData("cmd:show")]                // a verb with no topic
    [InlineData("cmd:show:")]               // a verb with an empty topic
    [InlineData("cmd:show:notanumber")]
    [InlineData("cmd:show: 5")]             // whitespace is not something Build_ForTopic ever wrote
    [InlineData("cmd::5")]                  // a topic with no verb
    [InlineData("cmd:pause:5")]             // a verb this class does not offer
    [InlineData("cmd:SHOW:5")]              // ordinal, case-sensitive
    [InlineData("CMD:show:5")]
    [InlineData("cmd")]
    [InlineData("show:5")]                  // the prefix is what makes it ours
    public void AnythingMalformed_IsNull(string? callbackData)
    {
        Assert.Null(TopicCommandButtons.Parse_OrNull(callbackData));
    }
}
