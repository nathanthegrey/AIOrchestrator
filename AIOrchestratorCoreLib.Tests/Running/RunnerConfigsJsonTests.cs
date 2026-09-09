using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The config.json shape of the runner configuration, both ways. The one property that matters
/// most is the first test: an existing config.json, with no runners block at all, means terminal
/// everywhere — nothing changes for anyone who did not ask.
/// </summary>
public class RunnerConfigsJsonTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public RunnerConfigsJsonTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-runner-config-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void AbsentBlock_MeansTerminalEverywhere_AndDefaultLimits()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"repos":[]}""") as JsonObject);

        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(role).Runner);

        Assert.Equal(ResumeModes.Fresh, configs.Get_ForRole(SessionRoles.General).Resume);
        Assert.Equal(ResumeModes.Transcript, configs.Get_ForRole(SessionRoles.Implementer).Resume);
        Assert.Null(configs.Get_ForRole(SessionRoles.Implementer).PermissionMode);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS, configs.MaxConcurrentTurns);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS_PER_ORCHESTRATION, configs.MaxConcurrentTurnsPerOrchestration);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_TURN_TIMEOUT, configs.TurnTimeout);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_COALESCE_WINDOW, configs.CoalesceWindow);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW, configs.MemberDigestWindow);
    }

    /// <summary>
    /// THE DIGEST WINDOW THE VPS WILL RUN ON, pinned at the surface the owner edits (spec C4). Five
    /// minutes is the spec's proposal, and the test harness deliberately does NOT default to it (see
    /// its <c>memberDigestMinutes</c> parameter) — so this is the only place that says what production
    /// gets when nobody writes the key.
    /// </summary>
    [Fact]
    public void TheMemberDigestWindow_IsFiveMinutesUnlessTheOwnerSaysOtherwise()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW);

        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""
            {"printRunner": { "memberDigestMinutes": 2.5 }}
            """) as JsonObject);

        Assert.Equal(TimeSpan.FromMinutes(2.5), configs.MemberDigestWindow);
    }

    /// <summary>
    /// AND ZERO MEANS OFF, not "unreadable, have the default back". It is the owner's way back to one
    /// entry one turn — the behaviour before 2026-09-09 — without a deployment, which is why the key
    /// is read with the NON-NEGATIVE reader rather than the positive one that governs a timeout.
    /// </summary>
    [Fact]
    public void AZeroMemberDigest_TurnsTheDigestOffRatherThanRestoringTheDefault()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""
            {"printRunner": { "memberDigestMinutes": 0 }}
            """) as JsonObject);

        Assert.Equal(TimeSpan.Zero, configs.MemberDigestWindow);
    }

    [Fact]
    public void NoConfigAtAll_IsTheSameDefault()
    {
        Assert.Equal(SessionRunners.Terminal, RunnerConfigs_Json.Parse(null).Get_ForRole(SessionRoles.Solo).Runner);
        Assert.Equal(SessionRunners.Terminal, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Runners.Get_ForRole(SessionRoles.Implementer).Runner);
    }

    [Fact]
    public void PerRoleBlocks_AndLimits_AreRead()
    {
        var root = JsonNode.Parse("""
            {
              "runners": {
                "implementer": { "runner": "print", "resume": "transcript", "permission_mode": "acceptEdits" },
                "general": { "runner": "print", "resume": "fresh" },
                "supervisor": { "runner": "terminal" }
              },
              "printRunner": { "maxConcurrentTurns": 4, "maxConcurrentTurnsPerOrchestration": 2, "turnTimeoutMinutes": 0.5, "coalesceSeconds": 1, "memberDigestMinutes": 7 }
            }
            """) as JsonObject;

        var configs = RunnerConfigs_Json.Parse(root);

        Assert.Equal(SessionRunners.Print, configs.Get_ForRole(SessionRoles.Implementer).Runner);
        Assert.Equal("acceptEdits", configs.Get_ForRole(SessionRoles.Implementer).PermissionMode);
        Assert.Equal(SessionRunners.Print, configs.Get_ForRole(SessionRoles.General).Runner);
        Assert.Equal(ResumeModes.Fresh, configs.Get_ForRole(SessionRoles.General).Resume);
        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Supervisor).Runner);
        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Reviewer).Runner);
        Assert.Equal(4, configs.MaxConcurrentTurns);
        Assert.Equal(2, configs.MaxConcurrentTurnsPerOrchestration);
        Assert.Equal(TimeSpan.FromSeconds(30), configs.TurnTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), configs.CoalesceWindow);
        Assert.Equal(TimeSpan.FromMinutes(7), configs.MemberDigestWindow);
    }

    [Fact]
    public void UnknownWords_AndBadLimits_FallBackToDefaults_RatherThanThrow()
    {
        var root = JsonNode.Parse("""
            {
              "runners": { "implementer": { "runner": "daemon", "resume": "sometimes" }, "solo": "print" },
              "printRunner": { "maxConcurrentTurns": 0, "turnTimeoutMinutes": -3, "coalesceSeconds": "soon", "memberDigestMinutes": "later" }
            }
            """) as JsonObject;

        var configs = RunnerConfigs_Json.Parse(root);

        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Implementer).Runner);
        Assert.Equal(ResumeModes.Transcript, configs.Get_ForRole(SessionRoles.Implementer).Resume);
        Assert.Equal(SessionRunners.Terminal, configs.Get_ForRole(SessionRoles.Solo).Runner);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS, configs.MaxConcurrentTurns);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_TURN_TIMEOUT, configs.TurnTimeout);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_COALESCE_WINDOW, configs.CoalesceWindow);
        Assert.Equal(RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW, configs.MemberDigestWindow);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsEveryRoleAndLimit()
    {
        var runners = RunnerConfigs_Factory.Create_WithLimits(
            RunnerConfigs_Factory.Create_WithRole(
                RunnerConfigs_Factory.Create_Default(), SessionRoles.Reviewer, RoleRunnerConfig_Factory.Create(SessionRunners.Print, ResumeModes.Transcript, "plan")),
            7, 2, TimeSpan.FromMinutes(12), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(3));

        var config = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Repo", "/tmp/repo")], "opus", "opus", "sonnet", "sonnet", null, null, null, null, null, null, runners);

        OrchestratorConfig_Loader.Save(config, _paths);
        var reloaded = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Runners;

        Assert.Equal(SessionRunners.Print, reloaded.Get_ForRole(SessionRoles.Reviewer).Runner);
        Assert.Equal("plan", reloaded.Get_ForRole(SessionRoles.Reviewer).PermissionMode);
        Assert.Equal(SessionRunners.Terminal, reloaded.Get_ForRole(SessionRoles.Implementer).Runner);
        Assert.Equal(ResumeModes.Fresh, reloaded.Get_ForRole(SessionRoles.General).Resume);
        Assert.Equal(7, reloaded.MaxConcurrentTurns);
        Assert.Equal(2, reloaded.MaxConcurrentTurnsPerOrchestration);
        Assert.Equal(TimeSpan.FromMinutes(12), reloaded.TurnTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), reloaded.CoalesceWindow);
        Assert.Equal(TimeSpan.FromMinutes(3), reloaded.MemberDigestWindow);

        // Written explicitly: every role appears, so the owner sees the whole surface.
        var written = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject;
        Assert.NotNull(written!["runners"]!["solo"]);
        Assert.Equal("terminal", written["runners"]!["solo"]!["runner"]!.GetValue<string>());
        Assert.Equal(12, written["printRunner"]![RunnerConfigs_Json.TURN_TIMEOUT_MINUTES_KEY]!.GetValue<double>());
    }

    /// <summary>
    /// The /screenshots toggle rebuilds the config from an existing one; the Settings window passes
    /// the runners through explicitly. Neither may reset a hand-edited block. (There were two such
    /// toggles until 2026-09-09, when /italian and the layer it drove were abolished.)
    /// </summary>
    [Fact]
    public void ToggleCopies_KeepTheRunners()
    {
        var runners = RunnerConfigs_Factory.Create_WithRole(RunnerConfigs_Factory.Create_Default(), SessionRoles.Implementer, RoleRunnerConfig_Factory.Create(SessionRunners.Print, ResumeModes.Transcript, null));
        var source = OrchestratorConfig_Factory.Create([], null, null, null, null, null, null, null, null, null, null, runners);

        Assert.Same(runners, OrchestratorConfig_Factory.Create_WithStatusScreenshots(source, true).Runners);
    }

    [Fact]
    public void TheOverloadWithoutRunners_DefaultsToTerminal()
    {
        var config = OrchestratorConfig_Factory.Create([], null, null, null, null, null, null, null, null, null, null);

        Assert.Equal(SessionRunners.Terminal, config.Runners.Get_ForRole(SessionRoles.Implementer).Runner);
    }
}
