namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// REMOVES THE KIT THIS APP USED TO INSTALL BY HAND, and reports what it could not remove.
///
/// This is not tidying. MEASURED on CLI 2.1.263: when ~/.claude/commands/supervisor.md and the
/// plugin's supervisor skill both answer to /supervisor, THE LOCAL FILE WINS — a session reads the
/// stale protocol and nothing says so. On every machine that upgrades to the plugin, the old files
/// are still sitting there, so without this the whole stage delivers the OPPOSITE of what it is for:
/// `claude plugin list` would report 1.0.0 while every session ran the text from whichever build
/// last copied it. That is decisions 18 and 23 again, in their worst form yet, because this time a
/// version number would be actively lying.
///
/// It removes ONLY the exact filenames this app shipped — the six role protocols, the append helper
/// and the seven hooks. Anything else in those folders belongs to the person whose machine it is and
/// is never touched. What it fails to delete it RETURNS, so the caller refuses rather than assuming.
/// </summary>
public static class LegacyKit_Remover
{
    /// <summary>The commands this app installed into ~/.claude/commands, by the names it used.</summary>
    public static readonly IReadOnlyList<string> LEGACY_COMMAND_FILES =
    [
        "supervisor.md", "implementer.md", "reviewer.md", "solo.md",
        "general-supervisor.md", "communicator.md", "channel-append.sh",
    ];

    /// <summary>The hooks this app installed into ~/.claude/hooks, by the names it used.</summary>
    public static readonly IReadOnlyList<string> LEGACY_HOOK_FILES =
    [
        "supervisor-ledger-check.sh", "run-to-the-end-check.sh", "reviewer-readonly-check.sh",
        "supervisor-awaiting-answer-check.sh", "hook-log.sh", "hook-behaviour-check.sh",
        "watcher-behaviour-check.sh",
    ];

    public const string PROVENANCE_FILE_NAME = ".installed-by.txt";

    /// <summary>
    /// Deletes what it can. Returns (removed, stillThere) — stillThere is every legacy file that is
    /// STILL on disk afterwards, whether because deleting it threw or because it reappeared. A
    /// non-empty stillThere for a COMMAND is a shadow, and the caller must refuse on it.
    /// </summary>
    public static (IReadOnlyList<string> Removed, IReadOnlyList<string> StillThere) Remove(string claudeHomeFolder)
    {
        List<string> removed = [];
        List<string> stillThere = [];

        Sweep(Path.Combine(claudeHomeFolder, "commands"), LEGACY_COMMAND_FILES, removed, stillThere);
        Sweep(Path.Combine(claudeHomeFolder, "hooks"), LEGACY_HOOK_FILES, removed, stillThere);

        // The note the old installer dropped beside the commands it owned. Cosmetic, so a failure to
        // delete it is not a reason to refuse anything — it goes to its own list and is dropped.
        Sweep(Path.Combine(claudeHomeFolder, "commands"), [PROVENANCE_FILE_NAME], removed, []);

        return (removed, stillThere);
    }

    /// <summary>
    /// Which of the six role words a stale local command would still answer for. Read separately from
    /// <see cref="Remove"/> so the check can be re-run after a removal that may have failed — the
    /// question "is a session going to read the old text" must be answered by looking, not by
    /// trusting that the delete worked.
    /// </summary>
    public static IReadOnlyList<string> Find_ShadowingCommands(string claudeHomeFolder)
    {
        var commands = Path.Combine(claudeHomeFolder, "commands");

        if (!Directory.Exists(commands))
            return [];

        return [.. LEGACY_COMMAND_FILES
            .Select(name => Path.Combine(commands, name))
            .Where(File.Exists)];
    }

    static void Sweep(string folder, IReadOnlyList<string> names, List<string> removed, List<string> stillThere)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var name in names)
        {
            var file = Path.Combine(folder, name);

            if (!File.Exists(file))
                continue;

            try
            {
                File.Delete(file);
                removed.Add(file);
            }
            catch
            {
                // Deliberately swallowed HERE and re-raised by the caller looking at what is left:
                // the exception type does not matter, only whether the file is still able to shadow.
            }

            if (File.Exists(file))
                stillThere.Add(file);
        }
    }
}
