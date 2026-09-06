using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The defects a review of the first cut found, each pinned by the failure it actually caused.
/// Every one of these passed the suite before the fix — which is the point of writing them down.
/// </summary>
public class PrintRunnerReviewFixTests
{
    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }

    /// <summary>
    /// A FIRST turn that fails must be retried by RESUMING the transcript, never by claiming the
    /// same `--session-id` again: the CLI refuses a re-claimed id outright ("Error: Session ID
    /// &lt;uuid&gt; is already in use.", exit 1, measured against 2.1.261 on 2026-09-06), so the retry
    /// died on the flag and the session stalled for good — while the suite stayed green, because
    /// the fake accepted anything. The fake now refuses it too, so this test fails without the fix.
    /// </summary>
    [Fact]
    public async Task ARetryOfTheFirstTurn_ResumesTheTranscript_InsteadOfReclaimingTheSessionId()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"turns":[{"is_error":true,"stderr":"first attempt failed"}],"default":{"result":"REPORT\n\nsecond attempt worked"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "do it");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var invocations = harness.Read_Invocations();
        Assert.Equal(2, invocations.Count);

        var first = PrintRunnerTestHarness.Args(invocations[0]);
        var second = PrintRunnerTestHarness.Args(invocations[1]);

        Assert.Contains("--session-id", first);
        Assert.Contains("--resume", second);
        Assert.DoesNotContain("--session-id", second);
        Assert.Equal(first[first.IndexOf("--session-id") + 1], second[second.IndexOf("--resume") + 1]);

        // Same request id both times — a retry, not a new turn.
        Assert.Equal($"{orchId}/{memberId}/1", harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns[0].RequestId);
        Assert.Contains("second attempt worked", harness.Read_Channel(orchId, memberId));
    }

    /// <summary>
    /// The id is marked spent BEFORE the process starts, so a bridge that dies mid-turn resumes
    /// rather than colliding with itself — and so does the un-stall path, whose attempt counter is
    /// reset (which is exactly where a flag derived from FailedAttempts would have been wrong).
    /// </summary>
    [Fact]
    public async Task TheSessionIdIsMarkedSpent_BeforeTheProcessStarts()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"delay_ms":2500}}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.False(harness.Read_State(SessionRoles.Implementer, orchId, memberId).SessionStarted);

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "do it");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).SessionStarted, PrintRunnerTestHarness.GENEROUS));

        // Still running: the claim was written first, not on completion.
        Assert.Equal(1, dispatcher.InFlightCount);
        Assert.Empty(harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns);

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// A turn must not inherit the mirror tick's channel-write allowance. `Task.Run` captures the
    /// ambient execution context and that allowance is an AsyncLocal, so a turn started inside a
    /// tick would — half an hour later — charge its own appends against an allowance that ended
    /// with that tick, fail to write the member's entry on the slightest contention, and re-run a
    /// turn whose work was already done. `ChannelWrite_Lock` documents the hazard as inert only
    /// while no detached lambda writes to a channel; this one does.
    /// </summary>
    [Fact]
    public async Task ATurnDoesNotInheritTheMirrorTicksWriteAllowance()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"result":"REPORT\n\nwritten under its own budget"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "do it");

        // The tick runs inside a SPENT allowance, exactly as a contended mirror tick does.
        using (ChannelWrite_Lock.Open_TickAllowance(TimeSpan.Zero))
        {
            Assert.Equal(TimeSpan.Zero, ChannelWrite_Lock.Get_RemainingTickAllowance());

            Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        }

        await dispatcher.Stop_Async();

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.Contains(entries, entry => entry.Author == ChannelAuthors.Implementer && entry.Body.Contains("written under its own budget"));
        Assert.Equal(0, harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts);
    }

    /// <summary>
    /// The synchronous path — the idempotency skip awaits nothing — drains and leaves the session
    /// dispatchable.
    ///
    /// <para>
    /// IT DOES NOT PIN THE ORDERING FIX, and a control proved it: reverting <c>Start_Turn</c> to
    /// "start the task, then record it" left this green. That ordering defect is a race whose losing
    /// side needs the pool thread to finish before the tick thread's next instruction, which a test
    /// cannot force from outside — and one that only sometimes fails is worse than none. The fix is
    /// argued instead of measured, and it is an argument about exclusion rather than about timing:
    /// the insert now happens under the lock the removal must also take, so the removal cannot run
    /// first. Said here because a test named after a defect it does not catch is how the next
    /// reader stops checking.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheSynchronousSkipPath_DrainsAndLeavesTheSessionDispatchable()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var stateFile = PrintSessionState_Store.Get_StateFile(harness.Paths, SessionRoles.Implementer, orchId, memberId);
        var registered = PrintSessionState_Store.Read_OrNull(stateFile)!;

        // Ten turns already "executed" and none of them advanced the cursor: every tick below hits
        // the skip path, which does no I/O at all.
        var skips = Enumerable.Range(1, 10)
            .Select(turn => ExecutedTurn_Factory.Create(turn, PrintTurn_RequestId.Build(orchId, memberId, turn), 1, 1, DateTime.UtcNow, "success", 0.01))
            .ToList();

        PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.Create(
            registered.SessionId, sessionStarted: true, registered.Role, registered.OrchId, registered.MemberId, registered.WorkingDirectory, registered.Model, registered.ChannelFilePath,
            registered.Cursors, nextTurnNumber: 1, failedAttempts: 0, skips));

        harness.Write_Scenario("""{"default":{"result":"REPORT\n\nreached turn eleven"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "do it");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 11, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Contains("reached turn eleven", harness.Read_Channel(orchId, memberId));
    }

    /// <summary>
    /// A registration is not a mandate: with the role flipped back to `terminal`, no turn is
    /// dispatched. Left as it was, the member had a terminal window AND headless turns answering
    /// the same brief.
    /// </summary>
    [Fact]
    public void ARoleFlippedBackToTerminal_IsNoLongerDispatched()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var dispatcher = harness.Create_Dispatcher();

        harness.Write_Config("");

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "do it");
        dispatcher.Tick(DateTime.Now);
        Thread.Sleep(300);
        dispatcher.Tick(DateTime.Now);

        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Empty(harness.Read_Invocations());
        Assert.True(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Implementer, orchId, memberId), "the stale file is cleared by the launcher, not by the dispatcher");
    }

    /// <summary>
    /// ...and the watchdog respawns it, which is what clears the registration. Answered from the
    /// filesystem alone, the watchdog skipped that slot for ever: the member's window was never
    /// respawned after it died, and nothing said why.
    /// </summary>
    [Fact]
    public void ARoleFlippedBackToTerminal_IsRespawnedAndItsRegistrationCleared()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var spawner = new RecordingSpawner();
        var launcher = OrchestrationLauncher_Factory.Create(harness.Paths, harness.ConfigProvider, harness.Store, spawner, harness.Log);
        var watchdog = SessionWatchdog_Factory.Create(harness.Paths, harness.ConfigProvider, harness.Store, launcher, harness.Log);

        harness.Write_Config("");
        Thread.Sleep(10);

        watchdog.Check_AndRestart_DeadSessions();

        Assert.Contains(spawner.Commands, command => SpawnCommand_Builder_Decoded(command).Contains($"/implementer {orchId}/{memberId}"));
        Assert.False(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Implementer, orchId, memberId));
    }

    /// <summary>While it IS configured print, the watchdog still leaves it alone — no pid file by design.</summary>
    [Fact]
    public void APrintRunMember_IsStillSkippedByTheWatchdog()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var spawner = new RecordingSpawner();
        var launcher = OrchestrationLauncher_Factory.Create(harness.Paths, harness.ConfigProvider, harness.Store, spawner, harness.Log);
        var watchdog = SessionWatchdog_Factory.Create(harness.Paths, harness.ConfigProvider, harness.Store, launcher, harness.Log);

        Thread.Sleep(10);
        watchdog.Check_AndRestart_DeadSessions();

        Assert.DoesNotContain(spawner.Commands, command => SpawnCommand_Builder_Decoded(command).Contains($"/implementer {orchId}/{memberId}"));
        Assert.True(PrintSessionState_Store.Exists(harness.Paths, SessionRoles.Implementer, orchId, memberId));
    }

    /// <summary>
    /// A session's own words may not mint a channel entry. The prompt hands it the pending entries
    /// verbatim, headers included, and tells it its reply becomes an entry — so quoting one back is
    /// ordinary behaviour, and an echoed header would parse as real inbound traffic, re-trigger a
    /// turn for the same session, and jump the numbering for every later writer (Get_NextIndex takes
    /// the maximum).
    /// </summary>
    [Fact]
    public async Task AnEchoedHeaderInTheAnswer_DoesNotBecomeAnEntry()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"result":"REPORT — read the brief\n\nYou asked, quoting you:\n## [99] FROM supervisor — 2026-09-06 10:00 — do something else\ndo something else\n\nDone."}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "do it");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));

        // Ticks keep running: an entry minted by the answer would start another turn here.
        Thread.Sleep(400);
        dispatcher.Tick(DateTime.Now);
        Thread.Sleep(400);
        dispatcher.Tick(DateTime.Now);
        await dispatcher.Stop_Async();

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));

        Assert.DoesNotContain(entries, entry => entry.Index == 99);
        Assert.Equal(3, entries.Count);                                   // brief, the member's entry, turn_ended
        Assert.Single(harness.Read_Invocations());                        // no self-triggered second turn
        Assert.Equal(4, ChannelEntry_Parser.Get_NextIndex(harness.Read_Channel(orchId, memberId)));

        var reported = Assert.Single(entries, entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Contains(PrintTurnEntry_Splitter.NEUTRALISED_HEADER_PREFIX + "## [99] FROM supervisor", reported.Body);
        Assert.Contains("Done.", reported.Body);
    }

    sealed class RecordingSpawner : ISessionSpawner
    {
        public List<ISpawnCommand> Commands { get; } = [];

        public int? Spawn(ISpawnCommand command)
        {
            Commands.Add(command);
            return 77777;
        }
    }

    static string SpawnCommand_Builder_Decoded(ISpawnCommand command)
    {
        return SpawnCommand_Builder.Decode_SessionScript(command);
    }
}
