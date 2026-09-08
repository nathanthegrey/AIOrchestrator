namespace AIOrchestratorCoreLib.Bridge;

/// <summary>What the app decides about an <c>ATTACH: &lt;path&gt;</c> line.</summary>
public enum AttachmentVerdicts
{
    Send,
    MissingFile,
    OutsideAllowedRoots,
    TooLarge,

    /// <summary>An <c>IMAGE:</c> line pointing at something Telegram cannot render as a photo.</summary>
    NotAPicture,
}

/// <summary>
/// WHETHER A FILE AN AGENT NAMES MAY LEAVE THE MACHINE — decided here, enforced at the point of
/// effect in the engine (decision 21), and never by the agent's good behaviour.
///
/// <para>
/// <c>IMAGE:</c> uploads any readable path with no containment at all, and Telegram's own photo
/// validation is the only thing that stops a non-image. A DOCUMENT has no such backstop: every
/// session runs as the app's OS user, so <c>ATTACH: ~/.ssh/id_rsa</c> would upload a key. The rule
/// is therefore containment: a file may be attached only from the orchestration's own repository
/// or its supervision folder — the two places an agent legitimately produces files for the owner.
/// </para>
/// <para>
/// A REFUSAL IS SAID, NEVER SWALLOWED, and it says which rule and what would satisfy it, because a
/// silent drop looks exactly like "the app cannot send files" — the belief this feature exists to
/// correct (2026-09-07: HTML mockups were left on a disk the owner had to go and open).
/// </para>
/// </summary>
public static class EntryAttachment_Policy
{
    /// <summary>Telegram Bot API cap for <c>sendDocument</c> uploads (documented: 50 MB).</summary>
    public const long MAX_BYTES = 50L * 1024 * 1024;

    /// <summary>Telegram's own cap for <c>sendPhoto</c>, a fifth of the document one.</summary>
    public const long MAX_PICTURE_BYTES = 10L * 1024 * 1024;

    /// <summary>
    /// What Telegram will actually render as a photo. Checked BEFORE the upload because the failure
    /// on the other side is `400 IMAGE_PROCESS_FAILED`, which arrives as a log line nobody reads.
    /// </summary>
    static readonly HashSet<string> PICTURE_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp",
    };

    /// <param name="asPicture">
    /// True for an <c>IMAGE:</c> line, false for an <c>ATTACH:</c> one. The two markers differ in
    /// what Telegram will accept and in what it caps, and in nothing else — so they share this
    /// decision rather than each carrying half of it.
    /// </param>
    public static AttachmentVerdicts Decide(string path, IReadOnlyList<string> allowedRoots, bool exists, long lengthBytes, bool asPicture = false)
    {
        if (!exists)
            return AttachmentVerdicts.MissingFile;

        if (!Is_UnderAnyRoot(path, allowedRoots))
            return AttachmentVerdicts.OutsideAllowedRoots;

        // BEFORE THE SIZE, because "that is not a picture" is the more useful thing to be told about
        // a 30 KB HTML file — and because the answer is to use the other marker, not to shrink it.
        if (asPicture && !PICTURE_EXTENSIONS.Contains(Path.GetExtension(path)))
            return AttachmentVerdicts.NotAPicture;

        if (lengthBytes > (asPicture ? MAX_PICTURE_BYTES : MAX_BYTES))
            return AttachmentVerdicts.TooLarge;

        return AttachmentVerdicts.Send;
    }

    public static string Describe(AttachmentVerdicts verdict, string path, IReadOnlyList<string> allowedRoots, bool asPicture = false)
    {
        var marker = asPicture ? "IMAGE" : "ATTACH";
        var cap = (asPicture ? MAX_PICTURE_BYTES : MAX_BYTES) / (1024 * 1024);

        return verdict switch
        {
            AttachmentVerdicts.MissingFile =>
                $"{marker} refused — no file at {path}. Write the file first, then the {marker}: line.",
            AttachmentVerdicts.OutsideAllowedRoots =>
                $"{marker} refused — {path} is outside the folders a file may be sent from: "
                + (allowedRoots.Count == 0 ? "(none known for this orchestration)" : string.Join(" · ", allowedRoots))
                + $". Write it under one of those and point the {marker}: line there.",
            AttachmentVerdicts.NotAPicture =>
                $"IMAGE refused — {path} is not a picture, and Telegram answers a photo upload of one with "
                + "`400 IMAGE_PROCESS_FAILED`. Use `ATTACH: " + path + "` instead: it sends the file as a document, "
                + "which is what an HTML mockup, a CSV or a report needs.",
            AttachmentVerdicts.TooLarge =>
                $"{marker} refused — {path} is over {cap} MB, Telegram's cap for "
                + (asPicture ? "a photo. Send it with ATTACH: instead (50 MB), or shrink it." : "a document. Split it or summarise it."),
            _ => $"{marker}: {path}",
        };
    }

    /// <summary>
    /// Full-path prefix with a separator appended to the root, so <c>/repo-evil/x</c> is not "under"
    /// <c>/repo</c>, and <c>/repo/../id_rsa</c> normalises out before the comparison. Case follows
    /// the platform: Windows paths compare case-insensitively.
    /// </summary>
    static bool Is_UnderAnyRoot(string path, IReadOnlyList<string> allowedRoots)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var full = Path.GetFullPath(path);

        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (full.StartsWith(rootFull, comparison))
                return true;
        }

        return false;
    }
}
