namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// What the startup check found. Only <see cref="Ok"/> lets sessions spawn; every other value is a
/// refusal that names itself, because "the kit is wrong" without saying HOW is the silence decision
/// 21 forbids.
/// </summary>
public enum PluginVerdicts
{
    /// <summary>The check has not run. Spawning is allowed and the log says the gate never ran.</summary>
    Unchecked,

    /// <summary>Installed, enabled, and the version the running host was built against.</summary>
    Ok,

    /// <summary>No record of the plugin under this Claude home.</summary>
    NotInstalled,

    /// <summary>Installed at a version other than the one this host expects.</summary>
    VersionMismatch,

    /// <summary>Installed at the right version, but switched off — the sessions would load nothing.</summary>
    Disabled,

    /// <summary>The files that answer the question could not be read or parsed.</summary>
    Unreadable,

    /// <summary>
    /// The right kit is installed, and a stale hand-installed copy of a role protocol is STILL in
    /// ~/.claude/commands, where it wins the slash word (measured). Sessions would read the old text
    /// while the version number said otherwise — the one failure this whole stage exists to end.
    /// </summary>
    Shadowed,
}
