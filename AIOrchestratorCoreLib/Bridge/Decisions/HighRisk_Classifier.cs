namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// Whether a decision on the owner's phone is one a single tap must not be allowed to take.
///
/// <para>
/// IT READS THE QUESTION, NOT THE OPTIONS. What makes "yes" dangerous is what it is an answer to,
/// and an option label is usually a word ("yes", "go ahead", "1") that carries no risk of its own.
/// The question text is where the agent writes "ready to push to main?" — so that is what is
/// matched, and every option of a high-risk question inherits the classification, including the
/// refusals. That costs a typed code to say NO, which is the safe direction to be wrong in: the
/// alternative is guessing which of the labels is the dangerous one.
/// </para>
/// <para>
/// SUBSTRINGS, CASE-INSENSITIVE, AND NOT A REGEX. The list is edited in config.json by a person
/// under pressure, and a regex there is a way to write a pattern that matches everything or throws
/// at parse time — neither of which announces itself. A plain substring cannot do either.
/// </para>
/// </summary>
public static class HighRisk_Classifier
{
    /// <summary>
    /// True when any configured pattern appears in the question. An empty pattern list means the
    /// owner has said nothing is high risk, and nothing is: this returns false rather than falling
    /// back to a default, because the fallback belongs to the config factory and applying it twice
    /// would make an explicit empty list impossible to express.
    /// </summary>
    public static bool Is_HighRisk(string? questionText, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(questionText))
            return false;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (questionText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Which pattern matched, for the log line that explains why the owner is being asked for a
    /// code. "It looked risky" is not actionable; "it matched 'deploy'" is, and it is what lets
    /// them fix a pattern that fires too often.
    /// </summary>
    public static string? Find_MatchedPattern_OrNull(string? questionText, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(questionText))
            return null;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (questionText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return pattern;
        }

        return null;
    }
}
