using AIOrchestratorCoreLib.Translation;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Translation;

/// <summary>
/// AN ENGLISH MESSAGE ON AN ITALIAN PHONE LOOKED EXACTLY LIKE A TRANSLATED ONE.
///
/// <para>
/// Every failure path in the translator returns the original text and writes a log warning. On
/// 2026-09-07 one whole message reached the owner in English mid-conversation, indistinguishable
/// from a deliberate switch of language. The marker is the smallest thing that tells them, and these
/// pin the line between prose worth marking and the heartbeat traffic that must stay clean.
/// </para>
/// </summary>
public class UntranslatedTextMarkerTests
{
    /// <summary>Verbatim from the topic — the message that arrived in English and started this.</summary>
    const string THE_ENGLISH_MESSAGE =
        "Both current sessions are mid-task, so I've asked for one more to take it rather than let it queue.";

    [Fact]
    public void AnUntranslatedSentence_IsMarked()
    {
        Assert.True(UntranslatedText_Marker.Should_Mark(THE_ENGLISH_MESSAGE));
        Assert.StartsWith(UntranslatedText_Marker.MARKER, UntranslatedText_Marker.Mark(THE_ENGLISH_MESSAGE), StringComparison.Ordinal);
    }

    [Fact]
    public void TheOriginalTextSurvivesTheMark()
    {
        Assert.Contains(THE_ENGLISH_MESSAGE, UntranslatedText_Marker.Mark(THE_ENGLISH_MESSAGE), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE TRAFFIC THAT MUST STAY CLEAN. These come back unchanged because there was nothing to
    /// translate; marking them would stamp a warning glyph on most of what the owner sees.
    /// </summary>
    [Theory]
    [InlineData("✓✓")]
    [InlineData("PULSE · 2/7 · 28% · unchanged 20 min")]
    [InlineData("🟢")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("imp-1: closed")]
    [InlineData("FIN-D-272")]
    public void StatusTraffic_IsNeverMarked(string text)
    {
        Assert.False(UntranslatedText_Marker.Should_Mark(text));
    }

    /// <summary>Long enough in characters but not in words — a path, an id, a figure row.</summary>
    [Theory]
    [InlineData("/home/orch/.claude/supervision/fincanva-1/orchestrator.log.jsonl")]
    [InlineData("2026-09-07T11:21:01.2292685Z 2026-09-07T11:21:19.0000000Z")]
    public void LongButWordless_IsNotProse(string text)
    {
        Assert.False(UntranslatedText_Marker.Should_Mark(text));
    }

    [Fact]
    public void ShortProse_IsBelowTheThreshold()
    {
        Assert.False(UntranslatedText_Marker.Should_Mark("Done, merged."));
    }

    /// <summary>
    /// IDEMPOTENT. A message that passes through twice must not collect two flags, and must not be
    /// re-marked on a retry.
    /// </summary>
    [Fact]
    public void MarkingTwice_MarksOnce()
    {
        var once = UntranslatedText_Marker.Mark(THE_ENGLISH_MESSAGE);

        Assert.Equal(once, UntranslatedText_Marker.Mark(once));
        Assert.False(UntranslatedText_Marker.Should_Mark(once));
    }

    [Fact]
    public void TheThresholds_StayLooseEnoughForARealSentence()
    {
        // A guard on the guard: raise these and the message that started this stops being marked.
        Assert.True(THE_ENGLISH_MESSAGE.Length >= UntranslatedText_Marker.PROSE_MINIMUM_CHARACTERS);
        Assert.True(UntranslatedText_Marker.PROSE_MINIMUM_WORDS <= 5);
    }
}
