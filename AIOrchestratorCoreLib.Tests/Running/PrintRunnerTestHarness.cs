using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.TurnExecutor;
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

    readonly double _coalesceSeconds;
    readonly double _turnTimeoutMinutes;
    readonly int _maxConcurrent;
    readonly int _maxPerOrchestration;
    readonly string _resumeForGeneral;
    readonly double _streamSilenceSeconds;

    public PrintRunnerTestHarness(string printRoles, double coalesceSeconds = 0, double turnTimeoutMinutes = 5, int maxConcurrent = 10, int maxPerOrchestration = 3, string resumeForGeneral = "fresh", double streamSilenceSeconds = 120)
    {
        TempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-print-runner-{Guid.NewGuid():N}");
        RepoPath = Path.Combine(TempRoot, "repo");
        Directory.CreateDirectory(RepoPath);
        Paths = SupervisionPaths_Factory.Create(TempRoot);
        Store = OrchestrationSessionStore_Factory.Create(Paths);
        Log = OrchestrationLog_Factory.Create(Paths);

        _coalesceSeconds = coalesceSeconds;
        _turnTimeoutMinutes = turnTimeoutMinutes;
        _maxConcurrent = maxConcurrent;
        _maxPerOrchestration = maxPerOrchestration;
        _resumeForGeneral = resumeForGeneral;
        _streamSilenceSeconds = streamSilenceSeconds;

        Directory.CreateDirectory(Paths.Root);
        Write_Config(printRoles);
        ConfigProvider = OrchestratorConfigProvider_Factory.Create(Paths);
    }

    /// <summary>
    /// Rewrites config.json with these roles on print — so a test can flip a role BACK to terminal
    /// the way the owner would. The write stamp is pushed forward because the provider reloads on
    /// it, and two writes inside one filesystem tick would otherwise serve the stale config.
    /// </summary>
    /// <summary>
    /// <paramref name="printRoles"/> is a comma-separated list, each entry either a role
    /// ("implementer") or a role and its runner ("supervisor:stream"). The bare form still means
    /// print, so every test written before the stream runner existed says exactly what it meant.
    /// </summary>
    public void Write_Config(string printRoles)
    {
        var runners = new JsonObject();

        foreach (var entry in printRoles.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':', 2);
            var role = parts[0].Trim();

            runners[role] = new JsonObject
            {
                ["runner"] = parts.Length > 1 ? parts[1].Trim() : "print",
                ["resume"] = role == "general" ? _resumeForGeneral : "transcript",
            };
        }

        var config = new JsonObject
        {
            ["repos"] = new JsonArray(new JsonObject { ["name"] = "Repo", ["path"] = RepoPath }),
            ["runners"] = runners,
            ["printRunner"] = new JsonObject
            {
                ["maxConcurrentTurns"] = _maxConcurrent,
                ["maxConcurrentTurnsPerOrchestration"] = _maxPerOrchestration,
                ["turnTimeoutMinutes"] = _turnTimeoutMinutes,
                ["coalesceSeconds"] = _coalesceSeconds,
                ["streamSilenceSeconds"] = _streamSilenceSeconds,
            },
        };

        File.WriteAllText(Paths.ConfigFile, config.ToJsonString());
        File.SetLastWriteTimeUtc(Paths.ConfigFile, DateTime.UtcNow.AddSeconds(1));
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
        return PrintTurnDispatcher_Factory.Create(Paths, Store, ConfigProvider, Create_Executors(), Log, retryBackoff ?? TimeSpan.FromMilliseconds(200));
    }

    /// <summary>The production ladder, against the fake: print, and stream falling back to it.</summary>
    public IReadOnlyList<ITurnExecutor> Create_Executors()
    {
        return TurnExecutor_Factory.Create_All(Paths, Fake_Invocation(), Log, ConfigProvider);
    }

    /// <summary>An orchestration with one member of the kind, registered bridge-driven — the launcher's path, minus the terminal.</summary>
    public (string OrchId, string MemberId) Register_Member(MemberKinds kind, string orchId = "repo-1", SessionRunners runner = SessionRunners.Print)
    {
        if (Store.Get_Session_OrNull(orchId) == null)
            Store.Create_Orchestration(orchId, "Repo", RepoPath);

        var session = Store.Add_Member(orchId, kind);
        var memberId = session.Members[^1].MemberId;
        var role = SessionRole_Names.From_MemberKind(kind);

        Create_Runner(runner).Start(
            SessionLaunch_Factory.Create(role, orchId, memberId, RepoPath, "haiku", Paths.Get_ImplementerPidFile(orchId, memberId), null));

        return (orchId, memberId);
    }

    /// <summary>The orchestration's SUPERVISOR, registered bridge-driven — woken by the owner channel.</summary>
    public string Register_Supervisor(string orchId = "repo-1", SessionRunners runner = SessionRunners.Stream)
    {
        if (Store.Get_Session_OrNull(orchId) == null)
            Store.Create_Orchestration(orchId, "Repo", RepoPath);

        // The stamp is what makes the orchestration a CREW rather than a basic one, and the launcher
        // writes it for every supervisor whether it spawns a window or registers a state file.
        Store.Set_SupervisorPid(orchId, null);

        Create_Runner(runner).Start(
            SessionLaunch_Factory.Create(SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID, RepoPath, "haiku", Paths.Get_SupervisorPidFile(orchId), null));

        return SessionLaunch_Factory.SUPERVISOR_MEMBER_ID;
    }

    public void Register_General(SessionRunners runner = SessionRunners.Print)
    {
        GeneralChannel_Initializer.Ensure_Exists(Paths);

        Create_Runner(runner).Start(
            SessionLaunch_Factory.Create(SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch_Factory.GENERAL_MEMBER_ID, RepoPath, "sonnet", Paths.GeneralPidFile, null));
    }

    ISessionRunner Create_Runner(SessionRunners runner)
    {
        return runner == SessionRunners.Stream ? SessionRunner_Factory.Create_Stream(Paths, Log) : SessionRunner_Factory.Create_Print(Paths, Log);
    }

    /// <summary>
    /// Pushes the supervisor's spawn stamp into the past, so the watchdog's 90 s grace is over. The
    /// store has no setter for it (nothing in the app needs one), and a test that waited it out for
    /// real would take a minute and a half.
    /// </summary>
    public void Age_SupervisorSpawn(string orchId, TimeSpan by)
    {
        var file = Paths.Get_SessionFile(orchId);
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;

        root["supervisorSpawnedUtc"] = DateTime.UtcNow.Subtract(by).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(file, root.ToJsonString());
    }

    public void Write_Scenario(string json)
    {
        File.WriteAllText(ScenarioFile, json);
    }

    /// <summary>
    /// RETRIED, because a test may read this WHILE a fake is writing it. The fake takes the log
    /// under an exclusive handle (it has to: ten turns share one file and the counters are computed
    /// from what is really there), so a plain read throws "used by another process" — which is
    /// exactly what happened once in seven runs of a test that polls this file inside its wait
    /// predicate, and reads as a failure of the code under test rather than of the reader.
    /// </summary>
    public IReadOnlyList<JsonObject> Read_Invocations()
    {
        for (var attempt = 0; ; attempt++)
        {
            if (!File.Exists(InvocationLog))
                return [];

            try
            {
                return File.ReadAllLines(InvocationLog).Where(line => line.Length > 0).Select(line => (JsonObject)JsonNode.Parse(line)!).ToList();
            }
            catch (IOException) when (attempt < 100)
            {
                Thread.Sleep(20);
            }
        }
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
