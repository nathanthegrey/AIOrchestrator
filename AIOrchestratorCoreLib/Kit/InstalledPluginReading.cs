namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// What the two files under a Claude home say about one installed plugin. Immutable, and every field
/// is something a human can be shown: the refusal message quotes them rather than paraphrasing.
/// </summary>
public sealed class InstalledPluginReading(string? version, string? installPath, bool enabled, string? unreadableReason)
{
    /// <summary>Null when the plugin has no install record.</summary>
    public string? Version { get; } = version;

    /// <summary>Where the installed copy lives — the fourth-copy question, answered.</summary>
    public string? InstallPath { get; } = installPath;

    /// <summary>settings.json enabledPlugins. A disabled plugin is installed and loads nothing.</summary>
    public bool Enabled { get; } = enabled;

    /// <summary>Non-null when a file existed but could not be read or parsed. Never guessed.</summary>
    public string? UnreadableReason { get; } = unreadableReason;

    public bool Is_Installed => Version != null;
}
