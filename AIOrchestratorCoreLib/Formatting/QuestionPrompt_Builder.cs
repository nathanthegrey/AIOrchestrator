using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// Builds the SHORT question that sits immediately above a set of decision buttons.
///
/// WHY THIS EXISTS: agents write long, thorough messages and then hang the options off the bottom
/// of them. On a phone that arrives as a wall of text with taps underneath and no visible question —
/// the owner has to reconstruct what is being asked before they can answer. So the buttons never
/// ride on the body any more: the body is sent as ordinary messages, and the buttons go on their
/// OWN short message carrying just the question.
///
/// The question comes from an explicit `QUESTION:` line when the agent wrote one (the role commands
/// require it). When it did not, one is derived from the body's last question sentence, and if even
/// that fails there is a canned prompt — the owner must never see naked buttons.
/// </summary>
public static class QuestionPrompt_Builder
{
    /// <summary>Beyond this a "question" is really a paragraph, and showing it defeats the purpose.</summary>
    public const int MAX_DERIVED_LENGTH = 200;

    /// <summary>Canned, so it stays English like every other app string (owner directive).</summary>
    public const string FALLBACK_PROMPT = "Your call:";

    public const string PREFIX = "❓ ";

    public static string Build(IReadOnlyList<string> questionLines, string bodyText)
    {
        var explicitQuestion = string.Join(" ", questionLines.Where(line => !string.IsNullOrWhiteSpace(line))).Trim();

        if (explicitQuestion.Length > 0)
            return $"{PREFIX}{explicitQuestion}";

        return $"{PREFIX}{Derive_OrNull(bodyText) ?? FALLBACK_PROMPT}";
    }

    /// <summary>
    /// The last sentence of the body that ends in '?'. Anything longer than MAX_DERIVED_LENGTH is
    /// rejected rather than truncated — half a question is worse than the canned prompt.
    /// </summary>
    public static string? Derive_OrNull(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
            return null;

        // Fenced blocks (mockups, snippets) are not prose and must never be mined for a question.
        var (withoutBlocks, _) = MonospaceBlocks_Formatter.Extract_Blocks(bodyText);

        var lastMark = withoutBlocks.LastIndexOf('?');

        if (lastMark < 0)
            return null;

        var start = 0;

        for (var i = lastMark - 1; i >= 0; i--)
        {
            var character = withoutBlocks[i];

            if (character == '.' || character == '!' || character == '?' || character == '\n')
            {
                start = i + 1;
                break;
            }
        }

        var sentence = withoutBlocks[start..(lastMark + 1)].Trim();

        // A speaker prefix ("🔴 Sup: ") landing at the front of the first sentence is chrome.
        sentence = Regex.Replace(sentence, @"^[^\s]{1,3}\s*[A-Za-z-]{2,8}:\s*", "");
        sentence = sentence.Trim();

        if (sentence.Length == 0 || sentence.Length > MAX_DERIVED_LENGTH)
            return null;

        return sentence;
    }

    /// <summary>
    /// The question message after the owner has tapped: the question stays visible, with the choice
    /// under it. Telegram's tap toast is transient and the keyboard disappears, so without this the
    /// chat keeps no record of WHAT was chosen.
    /// </summary>
    public static string Build_AnsweredText(string questionText, string chosenLabel)
    {
        return $"{questionText}\n\n✅ {chosenLabel}";
    }

    /// <summary>Beyond this the owner's reply is a paragraph, and the record becomes the message.</summary>
    public const int MAX_ANSWER_PREVIEW_LENGTH = 80;

    /// <summary>When they answered with something that leaves no usable preview — a photo, a sticker.</summary>
    public const string ANSWERED_IN_WRITING = "answered by message";

    /// <summary>
    /// The same record, for an answer the owner TYPED instead of tapping.
    ///
    /// Without it a question answered in writing kept no trace of having been answered: the tap path
    /// stamped its choice here and the typed path stamped nothing, so the owner scrolled back to a
    /// question that still read as open. Their words, 2026-08-24, after answering one by message
    /// because the reply needed more than a label: *"The unanswered question — or rather, the one I
    /// answered with a message instead of a tap — dated back further."*
    ///
    /// IT SAYS "answered", NOT the label of a choice, because no choice was made. The preview is
    /// their own first line, truncated: a typed answer can be a pasted conversation, and the record
    /// has to stay a record rather than become a second copy of the message.
    /// </summary>
    public static string Build_AnsweredByMessageText(string questionText, string ownerText)
    {
        return $"{questionText}\n\n✅ answered: {Preview_OrDefault(ownerText)}";
    }

    /// <summary>
    /// The question message after a HIGH-RISK option has been tapped: the choice is shown as
    /// PROPOSED, not taken, with the four digits that will take it.
    ///
    /// <para>
    /// IT NAMES THE OPERATION BACK. "You are about to: &lt;option&gt;" is the read-back — the owner
    /// confirms what they are approving, not merely that they meant to press something. That is the
    /// whole content of the second gesture, and it is why the option text is repeated here even
    /// though it is already on the button they just pressed.
    /// </para>
    /// </summary>
    public static string Build_ConfirmationText(string questionText, string chosenLabel, string code, int expiryMinutes)
    {
        return $"{questionText}\n\n🔐 You are about to: {chosenLabel}\n\nReply with the code {code} within {expiryMinutes} minutes to confirm. Anything else leaves it undone.";
    }

    /// <summary>The record left on a question that nobody answered before its deadline.</summary>
    public static string Build_TimedOutText(string questionText, string outcome)
    {
        return $"{questionText}\n\n⏳ {outcome}";
    }

    static string Preview_OrDefault(string ownerText)
    {
        var firstLine = (ownerText ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);

        if (string.IsNullOrEmpty(firstLine))
            return ANSWERED_IN_WRITING;

        return firstLine.Length <= MAX_ANSWER_PREVIEW_LENGTH
            ? firstLine
            : $"{firstLine[..MAX_ANSWER_PREVIEW_LENGTH]}…";
    }
}
