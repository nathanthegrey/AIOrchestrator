using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.SessionRunner;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A supervision root, a repo folder the fake runs in, a config.json with the print runner on for
/// the roles a test names, and a dispatcher wired to FakeClaude — everything the behavioural tests
/// share. The fake reads its scenario from the repo folder and logs every invocation there.
/// </summary>
public sealed class PrintRunnerTestHarness : IDisposable
{
    public string TempRoot { get; }
    public string RepoPath { get; }
    public ISupervisionPaths Paths { get; }
    public IOrchestrationSessionStore Store { get; }
    public IOrchestratorConfigProvider ConfigProvider { get; }
    public IOrchestrationLog Log { get; }
    public string ScenarioFile => Path.Combine(RepoPath, "fake-claude-scenario.json");
    public string InvocationLog => Path.Combine(RepoPath, "fake-claude-invocations.jsonl");

    public PrintRunnerTestHarness(string printRoles, double coalesceSeconds = 0, double turnTimeoutMinutes = 5, int maxConcurrent = 10, int maxPerOrchestration = 3, string resumeForGeneral = "fresh")
    {
        TempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-print-runner-{Guid.NewGuid():N}");
        RepoPath = Path.Combine(TempRoot, "repo");
        Directory.CreateDirectory(RepoPath);
        Paths = SupervisionPaths_Factory.Create(TempRoot);
        Store = OrchestrationSessionStore_Factory.Create(Paths);
        Log = OrchestrationLog_Factory.Create(Paths);

        var runners = new JsonObject();

        foreach (var role in printRoles.Split(',', StringSplitOptions.RemoveEmptyEntries))
            runners[role.Trim()] = new JsonObject { ["runner"] = "print", ["resume"] = role.Trim() == "general" ? resumeForGeneral : "transcript" };

        var config = new JsonObject
        {
            ["repos"] = new JsonArray(new JsonObject { ["name"] = "Repo", ["path"] = RepoPath }),
            ["runners"] = runners,
            ["printRunner"] = new JsonObject
            {
                ["maxConcurrentTurns"] = maxConcurrent,
                ["maxConcurrentTurnsPerOrchestration"] = maxPerOrchestration,
                ["turnTimeoutMinutes"] = turnTimeoutMinutes,
                ["coalesceSeconds"] = coalesceSeconds,
            },
        };

        Directory.CreateDirectory(Paths.Root);
        File.WriteAllText(Paths.ConfigFile, config.ToJsonString());
        ConfigProvider = OrchestratorConfigProvider_Factory.Create(Paths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(TempRoot, recursive: true);
        }
        catch
        {
            // A fake still exiting may hold the folder for a moment; the OS temp folder is not precious.
        }
    }

    public static IClaudeInvocation Fake_Invocation()
    {
        var testFolder = Path.GetDirectoryName(typeof(PrintRunnerTestHarness).Assembly.Location) ?? string.Empty;
        var dll = Path.Combine(testFolder, "FakeClaude.dll");

        if (!File.Exists(dll))
            throw new Exception($"FakeClaude.dll not found beside the test assembly at '{dll}' — the project reference to tools/claude-contract/FakeClaude is missing or the fake did not build");

        return ClaudeInvocation_Factory.Create("dotnet", [dll]);
    }

    public IPrintTurnDispatcher Create_Dispatcher(TimeSpan? retryBackoff = null)
    {
        return PrintTurnDispatcher_Factory.Create(Paths, Store, ConfigProvider, PrintTurnRunner_Factory.Create(Fake_Invocation()), Log, retryBackoff ?? TimeSpan.FromMilliseconds(200));
    }

    /// <summary>An orchestration with one member of the kind, registered as print-run — the launcher's path, minus the terminal.</summary>
    public (string OrchId, string MemberId) Register_Member(MemberKinds kind, string orchId = "repo-1")
    {
        if (Store.Get_Session_OrNull(orchId) == null)
            Store.Create_Orchestration(orchId, "Repo", RepoPath);

        var session = Store.Add_Member(orchId, kind);
        var memberId = session.Members[^1].MemberId;
        var role = SessionRole_Names.From_MemberKind(kind);

        SessionRunner_Factory.Create_Print(Paths, Log).Start(
            SessionLaunch_Factory.Create(role, orchId, memberId, RepoPath, "haiku", Paths.Get_ImplementerPidFile(orchId, memberId), null));

        return (orchId, memberId);
    }

    public void Register_General()
    {
        GeneralChannel_Initializer.Ensure_Exists(Paths);

        SessionRunner_Factory.Create_Print(Paths, Log).Start(
            SessionLaunch_Factory.Create(SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch_Factory.GENERAL_MEMBER_ID, RepoPath, "sonnet", Paths.GeneralPidFile, null));
    }

    public void Write_Scenario(string json)
    {
        File.WriteAllText(ScenarioFile, json);
    }

    public IReadOnlyList<JsonObject> Read_Invocations()
    {
        if (!File.Exists(InvocationLog))
            return [];

        return File.ReadAllLines(InvocationLog).Where(line => line.Length > 0).Select(line => (JsonObject)JsonNode.Parse(line)!).ToList();
    }

    public static List<string> Args(JsonObject invocation)
    {
        return invocation["args"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
    }

    public IPrintSessionState Read_State(SessionRoles role, string orchId, string memberId)
    {
        return PrintSessionState_Store.Read_OrNull(PrintSessionState_Store.Get_StateFile(Paths, role, orchId, memberId))
            ?? throw new Exception("state file missing");
    }

    public string Read_Channel(string orchId, string memberId)
    {
        return File.ReadAllText(Paths.Get_ImplementerChannelFile(orchId, memberId));
    }

    /// <summary>Ticks the dispatcher with the real clock until the condition holds or the timeout passes; returns whether it held.</summary>
    public static bool Drive_Until(IPrintTurnDispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            dispatcher.Tick(DateTime.Now);

            if (condition())
                return true;

            Thread.Sleep(100);
        }

        dispatcher.Tick(DateTime.Now);
        return condition();
    }

    public static readonly TimeSpan GENEROUS = TimeSpan.FromSeconds(60);
}
