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

    public static (string Subject, string Body) Split(string? resultText)
    {
        var text = (resultText ?? string.Empty).Replace("\r\n", "\n").Trim();

        if (text.Length == 0)
            return (EMPTY_SUBJECT, string.Empty);

        var lines = text.Split('\n');
        var firstLine = Clean_SubjectLine(lines[0]);

        if (lines.Length >= 3 && lines[1].Trim().Length == 0 && firstLine.Length > 0 && firstLine.Length <= MAX_SUBJECT_LENGTH)
            return (firstLine, string.Join('\n', lines.Skip(2)).Trim());

        var subject = firstLine.Length == 0 ? EMPTY_SUBJECT : firstLine;

        if (subject.Length > MAX_SUBJECT_LENGTH)
            subject = subject[..(MAX_SUBJECT_LENGTH - 1)].TrimEnd() + "…";

        return (subject, text);
    }

    static string Clean_SubjectLine(string line)
    {
        // A heading mark on the first line would turn the subject into a second header — the
        // parser matches '## [' at line start — so it goes, together with bold marks.
        return line.Trim().TrimStart('#').Trim().Trim('*').Trim();
    }
}
