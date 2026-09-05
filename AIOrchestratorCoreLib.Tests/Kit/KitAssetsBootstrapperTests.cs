using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// The self-install sequence that used to live in the WPF App, now shared with the daemon: given
/// a shipped kit folder and a Claude home, commands, hooks and the status line land where Claude
/// Code reads them, and settings.json is wired — with the status line script picked for the OS.
/// </summary>
public class KitAssetsBootstrapperTests : IDisposable
{
    readonly string _temp;
    readonly string _kit;
    readonly string _claudeHome;
    readonly ISupervisionPaths _paths;

    public KitAssetsBootstrapperTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"aiorch-bootstrap-{Guid.NewGuid():N}");
        _kit = Path.Combine(_temp, "kit");
        _claudeHome = Path.Combine(_temp, "claude-home");
        _paths = SupervisionPaths_Factory.Create(Path.Combine(_temp, "supervision"));

        Directory.CreateDirectory(Path.Combine(_kit, "commands"));
        Directory.CreateDirectory(Path.Combine(_kit, "hooks"));
        Directory.CreateDirectory(Path.Combine(_kit, "statusline"));
        File.WriteAllText(Path.Combine(_kit, "commands", "supervisor.md"), "# supervisor\n");
        File.WriteAllText(Path.Combine(_kit, "commands", "channel-append.sh"), "#!/usr/bin/env bash\n");
        File.WriteAllText(Path.Combine(_kit, "hooks", "supervisor-ledger-check.sh"), "#!/usr/bin/env bash\n");
        File.WriteAllText(Path.Combine(_kit, "statusline", "statusline.ps1"), "# ps1\n");
        File.WriteAllText(Path.Combine(_kit, "statusline", "statusline.sh"), "#!/usr/bin/env bash\n");
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    [Fact]
    public void WindowsGetsThePowerShellScript_EveryOtherOSGetsTheBashTwin()
    {
        Assert.Equal("statusline.ps1", KitAssets_Bootstrapper.Pick_StatuslineScriptName(isWindows: true));
        Assert.Equal("statusline.sh", KitAssets_Bootstrapper.Pick_StatuslineScriptName(isWindows: false));
    }

    [Fact]
    public void Commands_Hooks_StatusLineAndSettings_AllLandWhereClaudeCodeReadsThem()
    {
        var log = OrchestrationLog_Factory.Create(_paths);

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, log);

        Assert.True(File.Exists(Path.Combine(_claudeHome, "commands", "supervisor.md")));
        Assert.True(File.Exists(Path.Combine(_claudeHome, "commands", "channel-append.sh")));
        Assert.True(File.Exists(Path.Combine(_claudeHome, "hooks", "supervisor-ledger-check.sh")));

        var expectedScript = KitAssets_Bootstrapper.Pick_StatuslineScriptName(OperatingSystem.IsWindows());
        var installedScript = Path.Combine(_paths.Root, expectedScript);
        Assert.True(File.Exists(installedScript), $"expected the {expectedScript} twin installed at {installedScript}");

        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(_claudeHome, "settings.json"))) as JsonObject
            ?? throw new Exception("settings.json should be a JSON object");
        var statusLineCommand = settings["statusLine"]?["command"]?.GetValue<string>() ?? "";
        Assert.Contains(expectedScript, statusLineCommand);
        Assert.NotNull(settings["hooks"]?["Stop"]);
        Assert.NotNull(settings["hooks"]?["PreToolUse"]);
    }

    [Fact]
    public void AMissingKit_IsLogged_AndNeverThrows_BecauseTheBridgeMustStartAnyway()
    {
        var log = OrchestrationLog_Factory.Create(_paths);
        var warnings = new List<string>();
        log.EntryLogged += entry => { if (entry.Level != AIOrchestratorCoreLib.Logging.LogLevels.Info) warnings.Add(entry.Message); };

        KitAssets_Bootstrapper.Ensure_Installed(Path.Combine(_temp, "no-such-kit"), _claudeHome, _paths, log);

        Assert.Contains(warnings, message => message.Contains("role commands NOT installed"));
        Assert.Contains(warnings, message => message.Contains("status line NOT installed"));
    }
}
