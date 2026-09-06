namespace AIOrchestratorCoreLib.Running;

/// <summary>One addressed part of a session's final message. <c>SourceKey</c> is null for text the session addressed to nobody.</summary>
public readonly record struct ReplyBlock(string? SourceKey, string Text);

/// <summary>
/// A MULTI-SOURCE SESSION'S FINAL MESSAGE, SPLIT BY THE CHANNEL EACH PART IS FOR. A supervisor woken by
/// the owner and by two spokes answers all three in one turn, and the bridge has to know which paragraph
/// belongs where — a verdict filed in the owner channel does not clear
/// <see cref="Status.MemberStates.AwaitingSupervisorReview"/> on the member it was about.
///
/// <para>
/// THE FORMAT IS ONE LINE, in the same shape as the <c>QUESTION:</c> / <c>OPTION:</c> / <c>DEADLINE:</c>
/// markers the entries already use — <c>TO: imp-1</c> alone on its line opens a block that runs to the
/// next such line or to the end. Text before the first one is addressed to nobody. It is deliberately
/// not JSON, not a fence and not a tool call: a model writing prose gets this right without being told
/// twice, and every failure of it is legible in the file afterwards.
/// </para>
/// <para>
/// TOLERANT IN ONE DIRECTION ONLY. A marker whose argument is not a single word is not a marker — it is
/// a sentence that happens to begin with "to:" — because the cost of the two mistakes is not symmetric:
/// treating prose as an address moves a paragraph to a channel nobody meant, while treating an address
/// as prose leaves it in the owner channel where a human is already looking. And an address naming a
/// channel this session does not have is NOT dropped: the caller writes it to the owner channel with a
/// note saying where it was meant to go. Nothing a session says is lost for want of a format
/// (<see cref="PrintTurnEntry_Splitter"/> makes the same promise about subjects).
/// </para>
/// <para>
/// A SINGLE-SOURCE SESSION NEVER COMES THROUGH HERE. A member has one channel, is never told about this
/// format, and would have its report cut in half by a line it wrote as prose. The caller skips the split
/// when the session has one source, which is every role but the orchestration supervisor.
/// </para>
/// </summary>
public static class TurnReply_Splitter
{
    public const string TO_MARKER = "TO:";

    public static IReadOnlyList<ReplyBlock> Split(string? resultText)
    {
        var text = (resultText ?? string.Empty).Replace("\r\n", "\n");

        List<ReplyBlock> blocks = [];
        string? currentKey = null;
        List<string> currentLines = [];

        foreach (var line in text.Split('\n'))
        {
            if (Read_Address_OrNull(line) is not string address)
            {
                currentLines.Add(line);
                continue;
            }

            Flush(blocks, currentKey, currentLines);

            currentKey = address;
            currentLines = [];
        }

        Flush(blocks, currentKey, currentLines);

        return blocks;
    }

    /// <summary>The address a <c>TO:</c> line names, or null when the line is not one.</summary>
    public static string? Read_Address_OrNull(string line)
    {
        var trimmed = line.Trim();

        if (!trimmed.StartsWith(TO_MARKER, StringComparison.OrdinalIgnoreCase))
            return null;

        var address = trimmed[TO_MARKER.Length..].Trim();

        // A single word or it is prose: "TO: the owner, when you get a moment" is a sentence, and
        // reading it as an address would move the paragraph after it out of the channel a human is
        // watching. Markdown emphasis around the word is stripped for the same reason the entry parser
        // strips it from an author (`ChannelEntry_Parser.Parse_Author`) — the shape is speculative there
        // and here, and near-harmless in both.
        if (address.Length == 0 || address.Any(char.IsWhiteSpace))
            return null;

        var undecorated = address.Trim('*', '_', '`', ':', ',', '.', ';', '(', ')', '[', ']', '"', '\'');

        // A line of pure punctuation after the marker names nobody, and returning it as an address would
        // open a block keyed on the empty string that no source could ever match.
        return undecorated.Length == 0 ? null : undecorated;
    }

    static void Flush(List<ReplyBlock> blocks, string? key, IReadOnlyList<string> lines)
    {
        var body = string.Join('\n', lines).Trim();

        if (body.Length == 0)
            return;

        blocks.Add(new ReplyBlock(key, body));
    }
}
