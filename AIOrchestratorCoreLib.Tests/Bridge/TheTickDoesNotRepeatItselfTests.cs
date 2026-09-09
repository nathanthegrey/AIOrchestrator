using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Diagnostics;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.Sessions;
using Xunit;
using Xunit.Abstractions;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHAT ONE MIRROR TICK COSTS THE DISK, over a supervision root the size of a real working day:
/// three orchestrations, three members each, every channel populated.
///
/// <para>
/// THE WASTE THIS PINS IS PURE REPETITION. Inside one 2-second tick the engine asked the store for
/// the whole roster from twenty-four places, offered every channel to the compactor (which read and
/// parsed all of them to discover they were nowhere near the 90-entry threshold), and re-read every
/// member's live-and-archive history from a dozen unrelated sweeps. Every one of those answers was
/// identical to the one before it, which is exactly why no behaviour test could see the difference:
/// the tick produced the same Telegram traffic, the same log and the same files either way. A count
/// is the only assertion that can tell one read from twenty-four.
/// </para>
/// <para>
/// THE NUMBERS ARE REPORTED, NOT ONLY ASSERTED. <see cref="OneTick_FileOpens_AreReported"/> prints
/// what the tick actually cost, so the next person to change this does not have to reconstruct the
/// measurement to know whether they made it worse. The assertions are ceilings with room in them —
/// a tick is not deterministic to the syscall (a topic gets created on the first tick, a status line
/// is composed once) — and they are set far below what the unfixed engine spends, so they fail on a
/// regression rather than on a slow machine.
/// </para>
/// <para>
/// ONE TICK, DETERMINISTICALLY: the run is cancelled as soon as the tick's last statement has landed,
/// which is <c>Persist_BridgeState</c> writing the cursor file. The inter-tick delay is 2 seconds and
/// the detection loop polls at 20 ms, so a second tick cannot start inside the window.
/// </para>
/// </summary>
public class TheTickDoesNotRepeatItselfTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;

    const int ORCHESTRATIONS = 3;
    const int MEMBERS_PER_ORCHESTRATION = 3;

    /// <summary>Short of the compaction threshold (90) on purpose: that is the shape of a live channel.</summary>
    const int ENTRIES_PER_CHANNEL = 40;

    readonly ITestOutputHelper _output;
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly LoadAllCounting_Store_Fake _store;
    readonly IBridgeEngine _engine;

    public TheTickDoesNotRepeatItselfTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-tick-cost-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The inbound loop needs the chat and owner ids. The Italian layer is pinned OFF because it
        // defaults ON and would reach the real translator.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = new LoadAllCounting_Store_Fake(OrchestrationSessionStore_Factory.Create(_paths));

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);

        // A Telegram client is what makes the tick do its real work: half the sweeps return
        // immediately when there is none, and the status-line pass — the heaviest reader of channel
        // history — is one of them. A file-only engine would measure a tick nobody runs.
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, _store, _launcher, log, new FailableTelegram_Fake());

        Seed_SupervisionRoot();
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE MEASUREMENT. Not an assertion about a target number — those move — but the two ceilings
    /// that separate "read once and reused" from "read again by every asker".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task OneTick_FileOpens_AreReported()
    {
        var counts = await Run_OneTick_Async();

        _output.WriteLine(
            $"ONE MIRROR TICK over {ORCHESTRATIONS} orchestrations x {MEMBERS_PER_ORCHESTRATION} members, "
            + $"{ENTRIES_PER_CHANNEL} entries per channel: Load_All calls={_store.LoadAll_Calls}, {counts}");

        // MEASURED ON THIS BRANCH, 2026-09-09, this scenario, same seam on both sides (the reverted
        // tree was measured by re-instating only the four counter increments):
        //   before: Load_All=16, session.json reads=93, text-file reads=115
        //   after:  Load_All=5,  session.json reads=0,  text-file reads=78
        // The ceilings below sit between the two, so each of them fails on a regression rather than
        // on a slow machine.

        // The roster: the tick takes ONE. What is left over are the passes that legitimately read the
        // disk outside the snapshot — the plan-backend pass runs on its own task and outlives the
        // tick, the watchdog runs on its own loop, and the general dashboard's report is also a
        // Telegram command handler, so none of the three may be handed the tick's snapshot.
        Assert.InRange(_store.LoadAll_Calls, 1, 8);

        // session.json: none at all here, because the roster was already parsed while this test
        // seeded the root and nothing has changed it since. That is the app's normal state — it is a
        // long-running process — and it is what the unfixed engine spent 93 reads on.
        Assert.InRange(counts.SessionFileReads, 0, 15);

        Assert.InRange(counts.TextFileReads, 1, 95);

        // The cursor is written at the end of the tick — the first tick baselines every channel, so
        // it genuinely changed — and again, forced, when the run is cancelled.
        Assert.InRange(counts.BridgeStateWrites, 1, 3);
    }

    /// <summary>
    /// A SECOND TICK OVER AN UNCHANGED ROOT MUST BE CHEAPER THAN THE FIRST, and must not write the
    /// cursor at all. This is the assertion that actually pins the caches: the first tick has to read
    /// everything whatever the caching, so a fixed engine and an unfixed one are hard to tell apart
    /// there. The second tick is where they diverge — nothing on disk changed, so a tick that still
    /// reads the channels is reading them for no reason, and a tick that still writes the cursor is
    /// rewriting bytes identical to the ones already in the file.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASecondTickOverAnUnchangedRoot_ReadsAlmostNothing_AndWritesNoCursor()
    {
        var (first, second, shutdown) = await Run_TwoTicks_Async();

        _output.WriteLine($"TICK 1: {first}");
        _output.WriteLine($"TICK 2 (nothing changed on disk): {second}");
        _output.WriteLine($"AFTER SHUTDOWN: {shutdown}");

        // MEASURED, 2026-09-09: the unfixed engine's second tick cost 93 session.json reads and 102
        // text-file reads — within three of its first — because nothing was reused across ticks.
        Assert.True(
            second.TextFileReads < first.TextFileReads,
            $"the second tick read as much as the first ({second.TextFileReads} vs {first.TextFileReads}) "
            + "over a root that did not change — the channel parses are not being reused across ticks.");

        Assert.InRange(second.TextFileReads, 0, 70);
        Assert.InRange(second.SessionFileReads, 0, 15);

        // D, THE ASSERTION THAT COSTS THE WRITE: nothing on disk moved, so the cursor says exactly
        // what the file already says, so the file is not rewritten. The unfixed engine rewrote it.
        Assert.Equal(0, second.BridgeStateWrites);

        // AND THE EXCEPTION TO IT. The last write of the process is forced, because a remembered
        // cursor that has drifted from the file has no later tick left to correct it.
        Assert.True(
            shutdown.BridgeStateWrites > second.BridgeStateWrites,
            "the bridge cursor was not written on shutdown. Every tick skipping an unchanged cursor is "
            + "only safe while the last word belongs to a forced write.");
    }

    async Task<Reading> Run_OneTick_Async()
    {
        using var counting = TickIo_Counters.Begin_Scope();
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await Wait_For_FirstTickEnd_Async();
        await cancellation.CancelAsync();
        await Await_Loop_Async(loop);

        return Reading.Of(counting.Counts);
    }

    async Task<(Reading First, Reading Second, Reading AfterShutdown)> Run_TwoTicks_Async()
    {
        using var counting = TickIo_Counters.Begin_Scope();
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await Wait_For_FirstTickEnd_Async();

        var first = Reading.Of(counting.Counts);

        counting.Counts.Reset();

        // The second tick starts after the loop's own 2-second delay; it has no cursor write to
        // announce its end, which is the point of the test, so this waits out the delay and then a
        // little more than a tick's worth of work.
        await Task.Delay(3500);

        var second = Reading.Of(counting.Counts);

        await cancellation.CancelAsync();
        await Await_Loop_Async(loop);

        return (first, second, Reading.Of(counting.Counts));
    }

    /// <summary>
    /// Waits for the cursor file to be written, which is the mirror tick's last statement. Used only
    /// for the FIRST tick, whose baselining always changes the cursor.
    /// </summary>
    async Task Wait_For_FirstTickEnd_Async()
    {
        for (var waited = 0; waited < 20_000; waited += 20)
        {
            if (File.Exists(_paths.BridgeStateFile))
                return;

            await Task.Delay(20);
        }

        throw new Exception(
            "no mirror tick ever reached its last statement, so nothing was measured. The counts below "
            + "would be an empty run reported as a saving.");
    }

    /// <summary>
    /// A FROZEN COPY of the live counts. The scope's counter object keeps counting — that is what
    /// makes a background loop's syscalls visible — so a second reading taken later would otherwise
    /// be the same object as the first and the two would compare equal whatever happened.
    /// </summary>
    readonly record struct Reading(long SessionFileReads, long TextFileReads, long BridgeStateWrites)
    {
        public static Reading Of(TickIo_Counters.Counts live)
        {
            return new Reading(live.SessionFileReads, live.TextFileReads, live.BridgeStateWrites);
        }

        public override string ToString()
        {
            return $"session.json reads={SessionFileReads}, text-file reads={TextFileReads}, "
                + $"bridge-state writes={BridgeStateWrites}";
        }
    }

    static async Task Await_Loop_Async(Task loop)
    {
        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }
    }

    /// <summary>
    /// Three orchestrations, each with a supervisor and two implementers, each channel carrying a
    /// working day's traffic and a PLAN.md beside it. This is the shape the tick's cost scales with.
    /// </summary>
    void Seed_SupervisionRoot()
    {
        for (var orchestration = 0; orchestration < ORCHESTRATIONS; orchestration++)
        {
            var session = _launcher.Start_Orchestration($"Repo{orchestration}", _tempRepo);

            while (session.Members.Count < MEMBERS_PER_ORCHESTRATION)
                session = _launcher.Add_Implementer(session.OrchId);

            Write_Channel(_paths.Get_OwnerChannelFile(session.OrchId));

            foreach (var member in session.Members)
                Write_Channel(_paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId));
        }
    }

    static void Write_Channel(string channelFile)
    {
        var text = string.Concat(Enumerable.Range(1, ENTRIES_PER_CHANNEL).Select(index =>
            $"## [{index}] FROM {(index % 2 == 0 ? "supervisor" : "implementer")} — 2026-09-09 10:{index:D2} — entry {index}\n"
            + $"body of entry {index}, long enough to be worth reading twice.\n\n"));

        File.WriteAllText(channelFile, text);
    }
}
