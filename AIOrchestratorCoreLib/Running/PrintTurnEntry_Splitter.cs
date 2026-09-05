using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// A session's final message into a channel entry's (subject, body). The contract the role
/// command's print paragraph states: first line = subject, blank line, body. A message that does
/// not follow it still becomes an entry — its first line, trimmed of markdown heading marks and
/// capped, is the subject and the whole text is the body — so nothing a session says is dropped
/// for want of a format.
/// </summary>
public static class PrintTurnEntry_Splitter
{
    public const int MAX_SUBJECT_LENGTH = 120;
    public const string EMPTY_SUBJECT = "(no message)";

    /// <summary>
    /// What a header-shaped line in the body is turned into. A markdown quote marker: the line stays
    /// readable and stays in the entry, but it no longer BEGINS with the header pattern, which is
    /// the only thing the parser looks at.
    /// </summary>
    public const string NEUTRALISED_HEADER_PREFIX = "> ";

    public static (string Subject, string Body) Split(string? resultText)
    {
        var text = (resultText ?? string.Empty).Replace("\r\n", "\n").Trim();

        if (text.Length == 0)
            return (EMPTY_SUBJECT, string.Empty);

        var lines = text.Split('\n');
        var firstLine = Clean_SubjectLine(lines[0]);

        if (lines.Length >= 3 && lines[1].Trim().Length == 0 && firstLine.Length > 0 && firstLine.Length <= MAX_SUBJECT_LENGTH)
            return (firstLine, Neutralise_HeaderLines(string.Join('\n', lines.Skip(2)).Trim()));

        var subject = firstLine.Length == 0 ? EMPTY_SUBJECT : firstLine;

        if (subject.Length > MAX_SUBJECT_LENGTH)
            subject = subject[..(MAX_SUBJECT_LENGTH - 1)].TrimEnd() + "…";

        return (subject, Neutralise_HeaderLines(text));
    }

    /// <summary>
    /// A BODY LINE MAY NOT LOOK LIKE A HEADER, and this is the boundary where untrusted text becomes
    /// an entry, so it is where the rule belongs.
    ///
    /// <para>
    /// The prompt hands the session its pending entries VERBATIM, header lines included, and then
    /// tells it that its reply becomes an entry — quoting the context back is ordinary model
    /// behaviour, not an attack. An echoed <c>## [12] FROM supervisor — …</c> parses as a real
    /// entry: with an index above the cursor and an inbound author it triggers another turn for the
    /// same session, whose answer can echo it again; and because <c>ChannelEntry_Parser.Get_NextIndex</c>
    /// takes the MAXIMUM, a made-up index also jumps the numbering for every later writer.
    /// </para>
    /// <para>
    /// Neutralised rather than rejected: the turn's work is real and its words are the record. The
    /// line survives, quoted, and says plainly that it was quoted.
    /// </para>
    /// </summary>
    static string Neutralise_HeaderLines(string body)
    {
        if (body.Length == 0)
            return body;

        var lines = body.Split('\n');
        var neutralised = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!ChannelEntry_Parser.Is_HeaderLine(lines[i]))
                continue;

            lines[i] = NEUTRALISED_HEADER_PREFIX + lines[i];
            neutralised = true;
        }

        return neutralised ? string.Join('\n', lines) : body;
    }

    static string Clean_SubjectLine(string line)
    {
        // A heading mark on the first line would turn the subject into a second header — the
        // parser matches '## [' at line start — so it goes, together with bold marks.
        return line.Trim().TrimStart('#').Trim().Trim('*').Trim();
    }
}
