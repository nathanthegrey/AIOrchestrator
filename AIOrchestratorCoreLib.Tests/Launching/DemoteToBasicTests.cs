using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Launching;

/// <summary>
/// THE WAY BACK — a full crew becomes one session again.
///
/// It did not exist until 2026-08-25. `SupervisorSpawnedUtc` is the shape and nothing could clear
/// it, so the promotion prompt, `solo.md` and three code comments all called promotion one-way, and
/// they were right. The owner asked for one command that goes both ways: *"with the same command I
/// transform an orchestra into solo, and a solo into an orchestra."*
///
/// These mirror <see cref="PromoteToFullCrewTests"/> case for case, because the claim is the same
/// claim in reverse: NOTHING MOVES, and the session that takes over inherits the conversation by
/// opening the file it was always going to open.
/// </summary>
public class DemoteToBasicTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;

    public DemoteToBasicTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-demote-basic-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _spawner = new RecordingSpawner_Fake();

        _launcher = OrchestrationLauncher_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, _spawner, OrchestrationLog_Factory.Create(_paths));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    static string Script(AIOrchestratorCoreLib.Spawning.SpawnCommand.ISpawnCommand command)
    {
        return SpawnCommand_Builder.Decode_SessionScript(command);
    }

    /// <summary>
    /// THE SHAPE AFTERWARDS: a solo running, every member of the old crew closed. Asserted on the
    /// role command the spawned session will actually run.
    /// </summary>
    [Fact]
    public void ASoloReplacesTheWholeCrew()
    {
        var crew = _launcher.Start_Orchestration("repo", _tempRepo);

        _spawner.SpawnedCommands.Clear();

        var basic = _launcher.Demote_ToBasic(crew.OrchId);

        Assert.Contains(_spawner.SpawnedCommands, c => Script(c).Contains($"/solo {crew.OrchId}", StringComparison.Ordinal));

        Assert.All(
            basic.Members.Where(m => MemberKind_Ids.Resolve_Kind(m.MemberId) != MemberKinds.Solo),
            member => Assert.NotNull(member.ClosedUtc));

        var solo = Assert.Single(basic.Members, m => MemberKind_Ids.Resolve_Kind(m.MemberId) == MemberKinds.Solo && m.ClosedUtc == null);
        Assert.NotNull(solo);
    }

    /// <summary>
    /// IT READS AS BASIC AGAIN — the property that was impossible to express before, because nothing
    /// could clear the stamp. Without it the watchdog would keep respawning a supervisor into an
    /// orchestration that is supposed to have one session.
    /// </summary>
    [Fact]
    public void TheDemotedOrchestrationReadsAsBasicAgain()
    {
        var crew = _launcher.Start_Orchestration("repo", _tempRepo);

        Assert.False(OrchestrationShape.Is_BasicOrchestration(crew.SupervisorSpawnedUtc));

        var basic = _launcher.Demote_ToBasic(crew.OrchId);

        Assert.True(OrchestrationShape.Is_BasicOrchestration(basic.SupervisorSpawnedUtc));

        // The pid goes with the stamp: a supervisor pid on an orchestration with no supervisor slot
        // is one the watchdog must never respawn and the terminator has already killed.
        Assert.Null(basic.SupervisorPid);
    }

    /// <summary>
    /// AND IT SURVIVES THE ROUND TRIP TO DISK. The clear is expressed through a `wasSet` flag because
    /// null cannot mean both "unchanged" and "cleared"; if that flag were dropped the in-memory
    /// object would look right and the persisted one would still say crew.
    /// </summary>
    [Fact]
    public void TheClearedStamp_IsPersisted_NotOnlyInMemory()
    {
        var crew = _launcher.Start_Orchestration("repo", _tempRepo);

        _launcher.Demote_ToBasic(crew.OrchId);

        var reloaded = OrchestrationSessionStore_Factory.Create(_paths).Get_Session(crew.OrchId);

        Assert.Null(reloaded.SupervisorSpawnedUtc);
        Assert.True(OrchestrationShape.Is_BasicOrchestration(reloaded.SupervisorSpawnedUtc));
    }

    /// <summary>
    /// NOTHING MOVES: the owner channel is the same file with the same contents, and the Telegram
    /// topic binding is untouched — so the owner keeps reading one thread and the solo inherits the
    /// supervisor's handover entry.
    /// </summary>
    [Fact]
    public void TheOwnerChannelAndTheTopicAreUntouched()
    {
        var crew = _launcher.Start_Orchestration("repo", _tempRepo);
        _store.Set_TelegramTopicId(crew.OrchId, 4242);

        var channelFile = _paths.Get_OwnerChannelFile(crew.OrchId);
        File.AppendAllText(channelFile, "\n## [7] FROM supervisor — 2026-08-25 13:00 — HANDOVER — imp-2's branch is unmerged\n");

        var before = File.ReadAllText(channelFile);

        var basic = _launcher.Demote_ToBasic(crew.OrchId);

        Assert.Equal(before, File.ReadAllText(channelFile));
        Assert.Equal(4242, basic.TelegramTopicId);
    }

    /// <summary>
    /// A SECOND DEMOTION DOES NOTHING — the mirror of AlreadyACrew, and it stops the same failure:
    /// two parked requests both pass the park check, and the second must not put a second solo beside
    /// the first.
    /// </summary>
    [Fact]
    public void DemotingTwice_DoesNotSpawnASecondSolo()
    {
        var crew = _launcher.Start_Orchestration("repo", _tempRepo);

        _launcher.Demote_ToBasic(crew.OrchId);

        _spawner.SpawnedCommands.Clear();

        var again = _launcher.Demote_ToBasic(crew.OrchId);

        Assert.Empty(_spawner.SpawnedCommands);
        Assert.Single(again.Members, m => MemberKind_Ids.Resolve_Kind(m.MemberId) == MemberKinds.Solo && m.ClosedUtc == null);
    }

    /// <summary>
    /// THE REPO IS CHECKED BEFORE ANYTHING IS DESTROYED. The promotion learned this the hard way and
    /// only flipped a flag; here the cost of getting it wrong is a killed crew with no solo able to
    /// replace it, so the crew must still be running afterwards.
    /// </summary>
    [Fact]
    public void AMissingRepo_RefusesBeforeKillingAnything()
    {
        var crew = _launcher.Start_Orchestration("repo", _tempRepo);

        Directory.Delete(_tempRepo, recursive: true);

        Assert.Throws<Exception>(() => _launcher.Demote_ToBasic(crew.OrchId));

        var after = _store.Get_Session(crew.OrchId);

        Assert.False(OrchestrationShape.Is_BasicOrchestration(after.SupervisorSpawnedUtc));
        Assert.All(after.Members, member => Assert.Null(member.ClosedUtc));
    }

    /// <summary>
    /// ROUND TRIP. The owner's command is one verb that goes both ways, so the two halves have to
    /// compose: a promoted orchestration demotes, and a demoted one promotes again.
    /// </summary>
    [Fact]
    public void PromoteThenDemoteThenPromote_LeavesACrew()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        Assert.False(OrchestrationShape.Is_BasicOrchestration(_launcher.Promote_ToFullCrew(basic.OrchId).SupervisorSpawnedUtc));
        Assert.True(OrchestrationShape.Is_BasicOrchestration(_launcher.Demote_ToBasic(basic.OrchId).SupervisorSpawnedUtc));
        Assert.False(OrchestrationShape.Is_BasicOrchestration(_launcher.Promote_ToFullCrew(basic.OrchId).SupervisorSpawnedUtc));
    }
}
