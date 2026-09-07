namespace AIOrchestratorCoreLib.Translation;

/// <summary>
/// WHEN THE TRANSLATOR GIVES UP, THE OWNER IS TOLD — because until now they were not.
///
/// <para>
/// Every failure path in <c>MessageTranslatorModel</c> — a timeout, a non-zero exit, empty output,
/// any exception — returns the ORIGINAL English text and writes a warning to a log nobody reads. So
/// an English message arrived on an Italian-speaking owner's phone looking exactly like a translated
/// one, with nothing to distinguish it. In the 2026-09-07 topic one whole message came through in
/// English mid-conversation and read as a deliberate switch of language.
/// </para>
/// <para>
/// <b>A PREFIX, NOT A SECOND MESSAGE.</b> The noise budget is the thing this codebase is trying to
/// win back; an extra "translation failed" message would spend it to say something the marker says
/// in one glyph.
/// </para>
/// <para>
/// <b>ONLY PROSE IS MARKED.</b> A status line, a tick, a pulse figure or an emoji row comes back
/// unchanged because there was nothing to translate, and stamping those would put a warning glyph on
/// most of the traffic. The test is deliberately crude — enough words, and enough of them long
/// enough to be a sentence — because the cost of missing one untranslated message is far lower than
/// the cost of marking every heartbeat.
/// </para>
/// </summary>
public static class UntranslatedText_Marker
{
    /// <summary>Reads as "this one is in English", in one character, in any locale.</summary>
    public const string MARKER = "🇬🇧";

    /// <summary>Shortest text that can plausibly be a sentence worth translating.</summary>
    public const int PROSE_MINIMUM_CHARACTERS = 40;

    /// <summary>How many word-shaped tokens make prose rather than a status line.</summary>
    public const int PROSE_MINIMUM_WORDS = 5;

    /// <summary>
    /// Whether an unchanged translation result is worth flagging to the owner. Called only when the
    /// translator has already returned the input untouched.
    /// </summary>
    public static bool Should_Mark(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < PROSE_MINIMUM_CHARACTERS)
            return false;

        if (Is_AlreadyMarked(text))
            return false;

        var words = 0;

        foreach (var token in text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var letters = 0;

            foreach (var character in token)
            {
                if (char.IsLetter(character))
                    letters++;
            }

            if (letters >= 3)
                words++;

            if (words >= PROSE_MINIMUM_WORDS)
                return true;
        }

        return false;
    }

    /// <summary>Idempotent: a message that already carries the marker is returned untouched.</summary>
    public static string Mark(string text)
    {
        return Is_AlreadyMarked(text) ? text : $"{MARKER} {text}";
    }

    static bool Is_AlreadyMarked(string text)
    {
        return text.StartsWith(MARKER, StringComparison.Ordinal);
    }
}
