namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Turns a reading of the installed kit into a verdict AND the sentence a human is shown. Pure: no
/// files, no clock, no logging — so every branch below is asserted rather than argued.
///
/// The message always names the same four things, because those are exactly what the evenings lost
/// to decisions 18 and 23 were spent guessing: what was EXPECTED, what was FOUND, WHERE the found
/// copy lives, and the COMMAND that fixes it. "Plugin mismatch" on its own is the silence again.
/// </summary>
public static class PluginVersion_Verifier
{
    public static PluginVerdicts Decide(InstalledPluginReading reading, string expectedVersion)
    {
        if (reading.UnreadableReason != null)
            return PluginVerdicts.Unreadable;

        if (!reading.Is_Installed)
            return PluginVerdicts.NotInstalled;

        if (reading.Version != expectedVersion)
            return PluginVerdicts.VersionMismatch;

        // Checked LAST on purpose: an operator whose plugin is both out of date and switched off is
        // better told about the version, because updating is what they have to do either way.
        return reading.Enabled ? PluginVerdicts.Ok : PluginVerdicts.Disabled;
    }

    /// <summary>
    /// The refusal a human reads, in the log and on their phone. Null when nothing is wrong —
    /// callers use that null as "spawning is allowed" rather than re-deciding the verdict.
    /// </summary>
    public static string? Describe(PluginVerdicts verdict, InstalledPluginReading reading, string expectedVersion, string pluginId)
    {
        var found = reading.Version ?? "nothing installed";
        var where = reading.InstallPath ?? "no install path on record";

        return verdict switch
        {
            PluginVerdicts.Ok => null,

            PluginVerdicts.Unchecked => null,

            PluginVerdicts.NotInstalled =>
                $"The {pluginId} kit is NOT INSTALLED under this Claude home, so no session would read a role protocol. "
                + $"Expected {expectedVersion}. Install it: {KitPlugin.INSTALL_COMMAND}",

            PluginVerdicts.VersionMismatch =>
                $"The installed {pluginId} kit is the WRONG VERSION — expected {expectedVersion}, found {found}, at {where}. "
                + $"Update it: {KitPlugin.UPDATE_COMMAND}",

            PluginVerdicts.Disabled =>
                $"The {pluginId} kit is installed at {found} ({where}) but DISABLED, so sessions would load none of it. "
                + $"Enable it: claude plugin enable {KitPlugin.NAME}",

            PluginVerdicts.Unreadable =>
                $"The {pluginId} kit's install record could not be read, so this host CANNOT TELL which protocols its "
                + $"sessions would follow: {reading.UnreadableReason}",

            _ => $"Unhandled plugin verdict '{verdict}' — refusing rather than guessing.",
        };
    }
}
