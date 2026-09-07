using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// ASCII mockups are how the owner picks between layout options from their phone. What this class
/// still owns is the second half of that: the translator never touches a drawing. The RENDERING of
/// the block moved to TelegramHtml_Renderer on 2026-09-07 — its tests moved with it, so there is
/// one place a &lt;pre&gt; is asserted rather than two that can disagree.
/// </summary>
public class MonospaceBlocksFormatterTests
{
    const string MOCKUP_MESSAGE = """
        🔴 Sup: two layouts for the settings window — which one?

        ```
        +----------+-------------+
        | rail     | content     |
        |  general |  [x] dark   |
        |  models  |  [ ] italian|
        +----------+-------------+
        ```
        OPTION: rail on the left
        """;

    [Fact]
    public void Extract_LiftsTheDrawingOut_SoProseCanBeTranslatedWithoutIt()
    {
        var (withoutBlocks, blocks) = MonospaceBlocks_Formatter.Extract_Blocks(MOCKUP_MESSAGE);

        Assert.Single(blocks);
        Assert.Contains("rail", blocks[0]);
        Assert.DoesNotContain("+----------+", withoutBlocks);
        Assert.Contains("which one?", withoutBlocks);
    }

    [Fact]
    public void ExtractThenRestore_ReturnsTheDrawingCharacterForCharacter()
    {
        var (withoutBlocks, blocks) = MonospaceBlocks_Formatter.Extract_Blocks(MOCKUP_MESSAGE);

        // Stands in for what the translator does to the prose around the block.
        var translated = withoutBlocks.Replace("which one?", "quale dei due?");
        var restored = MonospaceBlocks_Formatter.Restore_Blocks(translated, blocks);

        Assert.Contains("quale dei due?", restored);
        Assert.Contains("|  general |  [x] dark   |", restored);
    }
}
