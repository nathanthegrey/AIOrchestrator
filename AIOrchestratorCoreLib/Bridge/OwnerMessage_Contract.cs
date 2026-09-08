namespace AIOrchestratorCoreLib.Bridge;

/// <summary>One way an entry breaks the owner-message contract.</summary>
public enum OwnerMessageFaults
{
    /// <summary>More than one `QUESTION:` line. The owner cannot answer two things with one reply.</summary>
    TwoQuestions,

    /// <summary>Prose below the question. A question is the last thing in an entry or it is a moving target.</summary>
    ProseAfterTheQuestion,

    /// <summary>`OPTION:` lines with no `QUESTION:` above them — buttons under nothing.</summary>
    OptionsWithoutAQuestion,

    /// <summary>Line one says "I read you" instead of saying the thing.</summary>
    OpensWithAReceipt,

    /// <summary>Code, paths or stack traces in a message to someone who does not read code.</summary>
    CarriesCode,

    /// <summary>Over the length the owner will actually read.</summary>
    TooLong,
}

/// <summary>
/// THE RULES THE PROTOCOL ASKS FOR, CHECKED INSTEAD OF HOPED FOR.
///
/// <para>
/// The supervisor's protocol is 1,164 lines carrying 229 imperative rules, and a mechanical audit on
/// 2026-09-07 found 14 pairs that cannot both be obeyed. The owner's complaints all trace to the same
/// shape: a rule near the top ("lead with the decision", "one open question at a time") is contradicted
/// by another 300 to 780 lines below it, and the lower one wins because it is what the model read last.
/// </para>
/// <para>
/// <b>A SENTENCE IN A PROMPT IS A WISH; A CHECK IS A RULE.</b> Nothing here argues with the model. It
/// reads what was actually written and names what is wrong with it, so the handful of rules that can
/// be settled mechanically stop depending on which of 229 instructions the model happened to weigh.
/// </para>
/// <para>
/// <b>IT REPORTS, IT DOES NOT SWALLOW.</b> A validator that silently dropped an owner-facing entry
/// would turn a formatting fault into a lost answer — the single worst outcome in this system, and
/// the one the whole question/answer audit was about. Faults are logged and coached back to the
/// session; the owner still gets the message.
/// </para>
/// <para>
/// <b>ONLY WHAT A MACHINE CAN SETTLE.</b> "Answer completely" and "say it in plain words" are real
/// rules and they are not here, because a check that guesses would train the session to write for the
/// checker. Counting question markers, spotting a file path and reading the first line are not guesses.
/// </para>
/// </summary>
public static class OwnerMessage_Contract
{
    public const int MAXIMUM_LINES = 6;
    public const int MAXIMUM_CHARACTERS = 600;

    const string QUESTION_MARKER = "QUESTION:";
    const string OPTION_MARKER = "OPTION:";

    /// <summary>Markers that legitimately follow a question — they are part of it, not prose.</summary>
    static readonly string[] QUESTION_COMPANIONS = [OPTION_MARKER, "DEADLINE:", "DEFAULT:", "IMAGE:", "ATTACH:"];

    /// <summary>
    /// How a reply that is only an acknowledgement opens. Deliberately anchored to the START of the
    /// line: "noted" opening an entry is a receipt, the same word inside a sentence is not.
    /// </summary>
    static readonly string[] RECEIPT_OPENERS =
    [
        "noted", "got it", "ok,", "okay,", "understood", "will do", "on it", "roger",
        "reading", "picking this up", "picking it up", "starting", "working on",
        "i'll ", "i will ", "i'm going to ", "im going to ", "received", "thanks", "sure,",
    ];

    /// <summary>
    /// Checks one owner-facing entry body. Empty result means it conforms.
    /// </summary>
    public static IReadOnlyList<OwnerMessageFaults> Check(string body)
    {
        List<OwnerMessageFaults> faults = [];

        if (string.IsNullOrWhiteSpace(body))
            return faults;

        var lines = body.Replace("\r\n", "\n").Split('\n');

        var questionIndexes = new List<int>();
        var optionIndexes = new List<int>();

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].TrimStart();

            if (trimmed.StartsWith(QUESTION_MARKER, StringComparison.Ordinal))
                questionIndexes.Add(index);
            else if (trimmed.StartsWith(OPTION_MARKER, StringComparison.Ordinal))
                optionIndexes.Add(index);
        }

        if (questionIndexes.Count > 1)
            faults.Add(OwnerMessageFaults.TwoQuestions);

        if (optionIndexes.Count > 0 && questionIndexes.Count == 0)
            faults.Add(OwnerMessageFaults.OptionsWithoutAQuestion);

        if (questionIndexes.Count > 0 && Has_ProseAfter(lines, questionIndexes[^1]))
            faults.Add(OwnerMessageFaults.ProseAfterTheQuestion);

        if (Opens_WithAReceipt(lines))
            faults.Add(OwnerMessageFaults.OpensWithAReceipt);

        if (Carries_Code(body))
            faults.Add(OwnerMessageFaults.CarriesCode);

        if (Is_TooLong(lines, body))
            faults.Add(OwnerMessageFaults.TooLong);

        return faults;
    }

    /// <summary>What to write back to the session. One line per fault, in its own words.</summary>
    public static string Describe(OwnerMessageFaults fault)
    {
        return fault switch
        {
            OwnerMessageFaults.TwoQuestions =>
                "Two QUESTION: lines in one entry. The owner answers with one message, so the second question "
                + "gets no answer and the app cannot tell which one they meant. Ask one; keep the other for after.",
            OwnerMessageFaults.ProseAfterTheQuestion =>
                "There is prose BELOW your QUESTION: line. The question must be the last thing in the entry — "
                + "anything after it arrives under a lock screen the owner has already stopped reading.",
            OwnerMessageFaults.OptionsWithoutAQuestion =>
                "OPTION: lines with no QUESTION: above them. Buttons under nothing: the app cannot register the "
                + "decision, so a tap answers something that was never asked.",
            OwnerMessageFaults.OpensWithAReceipt =>
                "Your first line is a receipt, not the answer. The owner reads line one and often stops — put the "
                + "decision, the result or the answer there, and what you are about to do on line two.",
            OwnerMessageFaults.CarriesCode =>
                "This entry carries a file path, an identifier or a stack trace. The owner does not read code: "
                + "name the thing by what it does for the product, and keep the path for the channel.",
            OwnerMessageFaults.TooLong =>
                $"Over {MAXIMUM_LINES} lines or {MAXIMUM_CHARACTERS} characters. Cut the evidence, never the answer — "
                + "if they want the reasoning they will ask for it.",
            _ => "This entry breaks the owner-message contract.",
        };
    }

    static bool Has_ProseAfter(string[] lines, int lastQuestionIndex)
    {
        for (var index = lastQuestionIndex + 1; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();

            if (trimmed.Length == 0)
                continue;

            var isCompanion = false;

            foreach (var companion in QUESTION_COMPANIONS)
            {
                if (trimmed.StartsWith(companion, StringComparison.Ordinal))
                {
                    isCompanion = true;
                    break;
                }
            }

            if (!isCompanion)
                return true;
        }

        return false;
    }

    static bool Opens_WithAReceipt(string[] lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
                continue;

            // A MARKER LINE IS NOT PROSE and cannot be a receipt, so it is not the line under test.
            if (trimmed.StartsWith("IMAGE:", StringComparison.Ordinal) || trimmed.StartsWith("ATTACH:", StringComparison.Ordinal))
                continue;

            var lowered = trimmed.ToLowerInvariant();

            foreach (var opener in RECEIPT_OPENERS)
            {
                if (lowered.StartsWith(opener, StringComparison.Ordinal))
                    return true;
            }

            // Only the FIRST prose line is judged: a receipt on line two is where the contract puts it.
            return false;
        }

        return false;
    }

    static bool Carries_Code(string body)
    {
        foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();

            // IMAGE: and ATTACH: carry a path BY DESIGN — it is how a screenshot or a file reaches the phone.
            if (line.StartsWith("IMAGE:", StringComparison.Ordinal) || line.StartsWith("ATTACH:", StringComparison.Ordinal))
                continue;

            if (Has_DeepPath(line) || Has_StackFrame(line) || Has_CodeIdentifier(line))
                return true;
        }

        return false;
    }

    /// <summary>Three or more path separators in one token — a real path, not a date or a fraction.</summary>
    static bool Has_DeepPath(string line)
    {
        foreach (var token in line.Split(' ', '\t'))
        {
            var separators = 0;

            foreach (var character in token)
            {
                if (character is '/' or '\\')
                    separators++;
            }

            if (separators >= 3)
                return true;
        }

        return false;
    }

    static bool Has_StackFrame(string line)
    {
        return line.StartsWith("at ", StringComparison.Ordinal) && line.Contains(':');
    }

    /// <summary>
    /// CamelCase joined by an underscore — the naming convention this codebase uses everywhere
    /// (`PlanLedger_Parser`, `Nudge_Decider`). Narrow on purpose: a member id like `imp-1` is how the
    /// owner refers to the crew themselves and must never be flagged.
    /// </summary>
    static bool Has_CodeIdentifier(string line)
    {
        foreach (var token in line.Split(' ', '\t', ',', '.', '(', ')'))
        {
            var underscore = token.IndexOf('_');

            if (underscore <= 0 || underscore == token.Length - 1)
                continue;

            if (char.IsUpper(token[0]) && char.IsUpper(token[underscore + 1]))
                return true;
        }

        return false;
    }

    static bool Is_TooLong(string[] lines, string body)
    {
        if (body.Length > MAXIMUM_CHARACTERS)
            return true;

        var counted = 0;

        foreach (var line in lines)
        {
            if (line.Trim().Length > 0)
                counted++;
        }

        return counted > MAXIMUM_LINES;
    }
}
