using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// Makes ASCII mockups survive the trip to a phone. Agents draw layout options, tables and trees in
/// fenced ``` blocks; Telegram's default rendering uses a proportional font, which turns any such
/// drawing into noise. Sent as HTML with a &lt;pre&gt; block it arrives monospaced and aligned.
///
/// The same fences also mark the text that must NOT be translated by the Italian layer — a mockup
/// or a code snippet is not prose, and translating it would corrupt the very thing being shown.
///
/// EXTRACTION ONLY, since 2026-09-07. This class used to render the &lt;pre&gt; block as well, and
/// that made it the SECOND Markdown-to-Telegram-HTML formatter once the mirror had to render bold,
/// bullets and links too. Two copies of a formatter is how they drift (CLAUDE.md decision 12), so
/// the rendering — fences included — now lives once, in
/// <see cref="Telegram.TelegramHtml_Renderer"/>. What is left here is the translator's concern:
/// lifting the blocks out and putting them back.
/// </summary>
public static partial class MonospaceBlocks_Formatter
{
    [GeneratedRegex(@"```[^\n]*\n(.*?)```", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex FencedBlock_Regex();

    const string PLACEHOLDER_PREFIX = "⁣BLOCK";

    /// <summary>
    /// Swaps fenced blocks for inert placeholders so surrounding prose can be translated while the
    /// blocks travel untouched. The placeholder uses an invisible separator, so a translator has
    /// nothing to "helpfully" reword.
    /// </summary>
    public static (string TextWithPlaceholders, IReadOnlyList<string> Blocks) Extract_Blocks(string text)
    {
        List<string> blocks = [];

        var replaced = FencedBlock_Regex().Replace(text, match =>
        {
            blocks.Add(match.Groups[1].Value);
            return $"{PLACEHOLDER_PREFIX}{blocks.Count - 1}⁣";
        });

        return (replaced, blocks);
    }

    public static string Restore_Blocks(string textWithPlaceholders, IReadOnlyList<string> blocks)
    {
        var restored = textWithPlaceholders;

        for (var i = 0; i < blocks.Count; i++)
            restored = restored.Replace($"{PLACEHOLDER_PREFIX}{i}⁣", $"```\n{blocks[i]}```");

        return restored;
    }
}
