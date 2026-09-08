namespace AIOrchestratorCoreLib.Bridge;

/// <summary>What the app decides about an <c>ATTACH: &lt;path&gt;</c> line.</summary>
public enum AttachmentVerdicts
{
    Send,
    MissingFile,
    OutsideAllowedRoots,
    TooLarge,
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

    public static AttachmentVerdicts Decide(string path, IReadOnlyList<string> allowedRoots, bool exists, long lengthBytes)
    {
        if (!exists)
            return AttachmentVerdicts.MissingFile;

        if (!Is_UnderAnyRoot(path, allowedRoots))
            return AttachmentVerdicts.OutsideAllowedRoots;

        if (lengthBytes > MAX_BYTES)
            return AttachmentVerdicts.TooLarge;

        return AttachmentVerdicts.Send;
    }

    public static string Describe(AttachmentVerdicts verdict, string path, IReadOnlyList<string> allowedRoots)
    {
        return verdict switch
        {
            AttachmentVerdicts.MissingFile =>
                $"ATTACH refused — no file at {path}. Write the file first, then the ATTACH: line.",
            AttachmentVerdicts.OutsideAllowedRoots =>
                $"ATTACH refused — {path} is outside the folders a file may be attached from: "
                + (allowedRoots.Count == 0 ? "(none known for this orchestration)" : string.Join(" · ", allowedRoots))
                + ". Put the file under the orchestration's repository or its supervision folder and attach it from there.",
            AttachmentVerdicts.TooLarge =>
                $"ATTACH refused — {path} is over {MAX_BYTES / (1024 * 1024)} MB, Telegram's document cap. Split it or summarise it.",
            _ => $"ATTACH: {path}",
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
