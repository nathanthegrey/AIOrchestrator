namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// WHO THE KIT IS, as the app expects to find it installed. These four words are the whole contract
/// between a built host and the protocols its sessions will actually read.
///
/// EXPECTED_VERSION is not decoration and it is not a build number: it is the assertion "the kit I
/// was built against is the kit that is installed". <see cref="KitPluginIdentityTests"/> holds it
/// equal to kit/.claude-plugin/plugin.json, so a protocol change that forgets to bump the manifest
/// fails the suite rather than shipping a host that certifies a kit it has never seen.
///
/// Decisions 17/18/23 are why this exists at all. The kit used to travel as four derived copies —
/// branch source, build output, installed folder, and whatever binary happened to be running — and
/// each of the three decisions was written after an evening lost to telling them apart. A plugin has
/// ONE installed copy with a version stamped on it, so the question becomes a comparison instead of
/// an investigation.
/// </summary>
public static class KitPlugin
{
    public const string NAME = "aiorch";
    public const string MARKETPLACE = "aiorch-local";

    /// <summary>The id both `claude plugin list --json` and installed_plugins.json key on.</summary>
    public const string ID = $"{NAME}@{MARKETPLACE}";

    /// <summary>Must equal the "version" in kit/.claude-plugin/plugin.json. Bump both together.</summary>
    public const string EXPECTED_VERSION = "1.0.0";

    /// <summary>What to tell a human to run when the installed copy is not this one.</summary>
    public const string UPDATE_COMMAND = $"claude plugin update {NAME}";

    /// <summary>What to tell a human to run when it is not installed at all.</summary>
    public const string INSTALL_COMMAND = "bash kit/install.sh   (Windows: pwsh kit/install.ps1)";
}
