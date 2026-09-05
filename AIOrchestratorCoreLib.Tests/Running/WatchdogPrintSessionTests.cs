using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>A print-run session has no pid file by design; the watchdog must not read that as death.</summary>
public class WatchdogPrintSessionTests
{
    sealed class RecordingLauncher : IOrchestrationLauncher
    {
        public List<string> Calls { get; } = [];

        public IOrchestrationSession Start_Orchestration(string repoName, string repoPath) => throw new NotSupportedException();
        public IOrchestrationSession Start_BasicOrchestration(string repoName, string repoPath) => throw new NotSupportedException();
        public IOrchestrationSession Add_Implementer(string orchId) => throw new NotSupportedException();
        public IOrchestrationSession Promote_ToFullCrew(string orchId) => throw new NotSupportedException();
        public IOrchestrationSession Demote_ToBasic(string orchId) => throw new NotSupportedException();
        public IOrchestrationSession Add_Member(string orchId, MemberKinds kind) => throw new NotSupportedException();
        public void Respawn_Supervisor(string orchId) => Calls.Add($"sup:{orchId}");
        public void Respawn_Communicator(string orchId) => Calls.Add($"com:{orchId}");
        public void Respawn_Implementer(string orchId, string memberId) => Calls.Add($"imp:{orchId}/{memberId}");
        public void Spawn_GeneralSupervisor() => Calls.Add("general");
    }

    [Fact]
    public void APrintMember_WithNoPidFile_IsNotRespawned_ButATerminalOneIs()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, printMember) = harness.Register_Member(MemberKinds.Implementer);
        var terminalMember = harness.Store.Add_Member(orchId, MemberKinds.Reviewer).Members[^1].MemberId;
        harness.Register_General();

        var launcher = new RecordingLauncher();
        var watchdog = SessionWatchdog_Factory.Create(harness.Paths, harness.Store, launcher, harness.Log);

        // Past the spawn grace, so a missing pid file counts.
        Thread.Sleep(10);
        watchdog.Check_AndRestart_DeadSessions();

        Assert.DoesNotContain($"imp:{orchId}/{printMember}", launcher.Calls);
        Assert.DoesNotContain("general", launcher.Calls);
        Assert.Contains(launcher.Calls, call => call.StartsWith("sup:") || call == $"imp:{orchId}/{terminalMember}");
    }
}
