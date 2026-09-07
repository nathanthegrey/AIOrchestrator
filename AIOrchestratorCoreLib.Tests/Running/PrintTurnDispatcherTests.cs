using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The print runner end to end on the FakeClaude stub — delays and errors injected through its
/// scenario file, the flags it received read back from its invocation log. Nothing here touches
/// the real CLI.
/// </summary>
public class PrintTurnDispatcherTests
{
    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }

    [Fact]
    public async Task FirstTurn_RunsTheRoleCommandUnderTheBridgesSessionId_AndWritesEntryPlusTurnEnded()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"result":"imp-1 online\n\nBrief received, starting on the parser.","total_cost_usd":0.0321,"duration_ms":4200}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF — parser", "Write the parser.");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var invocation = Assert.Single(harness.Read_Invocations());
        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        var args = PrintRunnerTestHarness.Args(invocation);

        // A1.2 — the command line.
        Assert.Equal(["-p", "--output-format", "json", "--name", $"{orchId}-{memberId}", "--session-id", state.SessionId, "--model", "haiku", "--dangerously-skip-permissions", $"/implementer {orchId}/{memberId}"], args);
        Assert.Equal("argument", invocation["prompt_source"]!.GetValue<string>());
        Assert.Equal("implementer", invocation["env"]!["AIORCH_ROLE"]!.GetValue<string>());
        Assert.Equal(orchId, invocation["env"]!["AIORCH_ID"]!.GetValue<string>());
        Assert.Equal(memberId, invocation["env"]!["AIORCH_MEMBER"]!.GetValue<string>());
        Assert.Equal("print", invocation["env"]!["AIORCH_RUNNER"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(invocation["cwd"]!.GetValue<string>(), "fake-claude-scenario.json")), "cwd is not the repo");

        // The member's entry, written by the bridge with header, index and time.
        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        var memberEntry = Assert.Single(entries, entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Equal("imp-1 online", memberEntry.Subject);
        Assert.Equal("Brief received, starting on the parser.", memberEntry.Body);
        Assert.Equal(2, memberEntry.Index);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", memberEntry.DateText);

        // turn_ended for the supervisor: agent audience, outcome, cost, duration, api_error_status.
        var ended = Assert.Single(entries, entry => entry.Author == ChannelAuthors.App);
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(ended.Subject));
        Assert.Contains($"{PrintTurn_Words.TURN_ENDED_SUBJECT} {memberId} turn 1 — success", ended.Subject);
        Assert.Contains($"request_id: {orchId}/{memberId}/1", ended.Body);
        Assert.Contains("cost_usd: 0.0321", ended.Body);
        Assert.Contains("duration_ms: 4200", ended.Body);
        Assert.Contains("api_error_status: none", ended.Body);

        // The cursor is the delivered IDENTITIES of its one source, not a number — one entry was handed over.
        Assert.Single(Assert.Single(state.Cursors).Delivered);
        Assert.Equal(2, state.NextTurnNumber);
        Assert.Equal($"{orchId}/{memberId}/1", state.ExecutedTurns[0].RequestId);
    }

    [Fact]
    public async Task SecondTurn_ResumesTheTranscript_WithThePromptOnStdin()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"turns":[{"result":"online\n\nwaiting"},{"result":"REPORT\n\ndone"}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "first");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));

        Append_Supervisor(harness, orchId, memberId, "GO AHEAD", "second");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var invocations = harness.Read_Invocations();
        Assert.Equal(2, invocations.Count);
        var second = PrintRunnerTestHarness.Args(invocations[1]);
        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);

        Assert.Contains("--resume", second);
        Assert.Equal(state.SessionId, second[second.IndexOf("--resume") + 1]);
        Assert.DoesNotContain("--session-id", second);
        Assert.DoesNotContain($"/implementer {orchId}/{memberId}", second);
        Assert.Equal("stdin", invocations[1]["prompt_source"]!.GetValue<string>());
        var prompt = invocations[1]["prompt"]!.GetValue<string>();
        Assert.Contains($"[bridge turn {orchId}/{memberId}/2]", prompt);
        Assert.Contains("GO AHEAD", prompt);
        Assert.Contains("second", prompt);
        Assert.DoesNotContain(PrintTurnPrompt_Builder.ALREADY_EXECUTED_PREFIX, prompt);
    }

    [Fact]
    public async Task EntriesWithinTheCoalesceWindow_RideOneTurn()
    {
        using var harness = new PrintRunnerTestHarness("implementer", coalesceSeconds: 1.5);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "one", "a");
        dispatcher.Tick(DateTime.Now);
        Thread.Sleep(300);
        Append_Supervisor(harness, orchId, memberId, "two", "b");
        dispatcher.Tick(DateTime.Now);
        Assert.Equal(0, dispatcher.InFlightCount);

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Single(harness.Read_Invocations());
        var executed = harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns[0];
        Assert.Equal(1, executed.FirstEntryIndex);
        Assert.Equal(2, executed.LastEntryIndex);
    }

    [Fact]
    public async Task OneTurnAtATimePerMember_AnEntryDuringATurnWaitsForTheNext()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"delay_ms":1500}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "one", "a");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => dispatcher.InFlightCount == 1, PrintRunnerTestHarness.GENEROUS));

        Append_Supervisor(harness, orchId, memberId, "two", "b");
        dispatcher.Tick(DateTime.Now);
        Assert.Equal(1, dispatcher.InFlightCount);

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var invocations = harness.Read_Invocations();
        Assert.Equal(2, invocations.Count);
        var firstStart = DateTime.Parse(invocations[0]["at"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var secondStart = DateTime.Parse(invocations[1]["at"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(secondStart - firstStart >= TimeSpan.FromMilliseconds(1400), $"second turn started {(secondStart - firstStart).TotalMilliseconds} ms after the first — it overlapped");

        // The second brief landed DURING the first turn, so it is [2]; the member's own entry and
        // the turn_ended record follow it as [3] and [4]. The second turn answers [2] alone.
        var executed = harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns;
        Assert.Equal(1, executed[0].LastEntryIndex);
        Assert.Equal(2, executed[1].FirstEntryIndex);
        Assert.Equal(2, executed[1].LastEntryIndex);
    }

    [Fact]
    public async Task TheGlobalSlotLimit_SerialisesTurnsAcrossMembers()
    {
        using var harness = new PrintRunnerTestHarness("implementer", maxConcurrent: 1);
        var (orchId, first) = harness.Register_Member(MemberKinds.Implementer);
        var (_, second) = harness.Register_Member(MemberKinds.Implementer, orchId);
        harness.Write_Scenario("""{"default":{"delay_ms":1200}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, first, "go", "a");
        Append_Supervisor(harness, orchId, second, "go", "b");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher,
            () => harness.Read_State(SessionRoles.Implementer, orchId, first).ExecutedTurns.Count == 1 && harness.Read_State(SessionRoles.Implementer, orchId, second).ExecutedTurns.Count == 1,
            PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var starts = harness.Read_Invocations().Select(invocation => DateTime.Parse(invocation["at"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind)).OrderBy(at => at).ToList();
        Assert.Equal(2, starts.Count);
        Assert.True(starts[1] - starts[0] >= TimeSpan.FromMilliseconds(1100), $"the second member's turn started {(starts[1] - starts[0]).TotalMilliseconds} ms after the first with one global slot");
    }

    [Fact]
    public async Task ATurnThatOutlivesTheTimeout_IsKilledAndRetried_AndStallsWithAnAlertOnTheThird()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: 1.0 / 60);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"delay_ms":8000}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "slow", "a");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= PrintTurn_Words.MAX_ATTEMPTS, TimeSpan.FromSeconds(40)));

        // Stalled: more ticks start nothing.
        var invocationsAtStall = harness.Read_Invocations().Count;
        Thread.Sleep(500);
        dispatcher.Tick(DateTime.Now);
        Thread.Sleep(500);
        dispatcher.Tick(DateTime.Now);
        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Equal(invocationsAtStall, harness.Read_Invocations().Count);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, invocationsAtStall);

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.Equal(3, entries.Count(entry => entry.Subject.Contains($"{PrintTurn_Words.TURN_ENDED_SUBJECT} {memberId} turn 1 — timeout")));
        var alert = Assert.Single(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_STALLED_SUBJECT));
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(alert.Subject));
        Assert.Contains("(killed on timeout)", entries.First(entry => entry.Subject.Contains("timeout")).Body);
        Assert.Empty(entries.Where(entry => entry.Author == ChannelAuthors.Implementer));
        Assert.Equal(1, harness.Read_State(SessionRoles.Implementer, orchId, memberId).NextTurnNumber);

        // New traffic un-stalls it, and the fast scenario now succeeds.
        harness.Write_Scenario("""{"default":{"result":"REPORT\n\nfine now"}}""");
        Append_Supervisor(harness, orchId, memberId, "again", "b");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Equal(0, harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts);
        Assert.Contains("fine now", harness.Read_Channel(orchId, memberId));
    }

    [Fact]
    public async Task AnErrorExit_IsRecordedWithItsApiStatus_AndWritesNoMemberEntry()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"turns":[{"is_error":true,"api_error_status":529,"stderr":"overloaded"}],"default":{"result":"ok\n\nrecovered"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "go", "a");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        var failed = entries.First(entry => entry.Subject.Contains("turn 1 — error"));
        Assert.Contains("api_error_status: 529", failed.Body);
        Assert.Contains("attempt: 1", failed.Body);
        var succeeded = entries.First(entry => entry.Subject.Contains("turn 1 — success"));
        Assert.Contains("attempt: 2", succeeded.Body);
        Assert.Single(entries.Where(entry => entry.Author == ChannelAuthors.Implementer));
        Assert.Equal(2, harness.Read_Invocations().Count);
    }

    [Fact]
    public async Task ARequestIdAlreadyExecuted_IsNeverRunAgain()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var stateFile = PrintSessionState_Store.Get_StateFile(harness.Paths, SessionRoles.Implementer, orchId, memberId);
        var registered = PrintSessionState_Store.Read_OrNull(stateFile)!;

        // The state says turn 1 ran but its index was never advanced — the crash-between-writes shape.
        var poisoned = PrintSessionState_Factory.Create(
            registered.SessionId, sessionStarted: true, registered.Role, registered.OrchId, registered.MemberId, registered.WorkingDirectory, registered.Model, registered.ChannelFilePath,
            registered.Cursors, nextTurnNumber: 1, failedAttempts: 0,
            [ExecutedTurn_Factory.Create(1, PrintTurn_RequestId.Build(orchId, memberId, 1), 1, 1, DateTime.UtcNow, "success", 0.01)]);
        PrintSessionState_Store.Write(stateFile, poisoned);
        harness.Write_Scenario("""{"default":{"result":"turn two\n\nran"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "go", "a");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        // TURN 1 WAS SKIPPED WITHOUT A PROCESS, AND THAT IS ASSERTED FROM THE TURNS, NOT FROM A COUNT OF
        // PROCESSES. It used to be `Assert.Single(invocations)`, which stopped meaning "turn 1 ran nothing"
        // the moment anything else could legitimately add an invocation — and something can: this state
        // says the id is spent while the CLI has never heard of it, so the first attempt resumes, is
        // refused ("No conversation found…"), and the recovery claims a fresh id and succeeds. Every
        // invocation here belongs to turn 2; none belongs to turn 1, which is the actual claim.
        var invocations = harness.Read_Invocations();

        Assert.All(invocations, line => Assert.DoesNotContain($"[bridge turn {orchId}/{memberId}/1]", line["prompt"]?.GetValue<string>() ?? string.Empty));
        Assert.Contains(invocations, line => (line["prompt"]?.GetValue<string>() ?? string.Empty).Contains($"[bridge turn {orchId}/{memberId}/2]", StringComparison.Ordinal));

        // And turn 1 produced no entry of its own: exactly one report reached the channel.
        Assert.Single(ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)), entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Equal(3, harness.Read_State(SessionRoles.Implementer, orchId, memberId).NextTurnNumber);
    }

    [Fact]
    public async Task AfterABridgeRestart_TheResumePromptListsTheExecutedTurns()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"result":"ok\n\nfine"}}""");

        var before = harness.Create_Dispatcher();
        Append_Supervisor(harness, orchId, memberId, "one", "a");
        Assert.True(PrintRunnerTestHarness.Drive_Until(before, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        Append_Supervisor(harness, orchId, memberId, "two", "b");
        Assert.True(PrintRunnerTestHarness.Drive_Until(before, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS));
        await before.Stop_Async();

        // "Restart": a new dispatcher instance over the same state on disk.
        var after = harness.Create_Dispatcher();
        Append_Supervisor(harness, orchId, memberId, "three", "c");
        Assert.True(PrintRunnerTestHarness.Drive_Until(after, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 3, PrintRunnerTestHarness.GENEROUS));
        Append_Supervisor(harness, orchId, memberId, "four", "d");
        Assert.True(PrintRunnerTestHarness.Drive_Until(after, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 4, PrintRunnerTestHarness.GENEROUS));
        await after.Stop_Async();

        var prompts = harness.Read_Invocations().Select(invocation => invocation["prompt"]!.GetValue<string>()).ToList();
        Assert.Equal(4, prompts.Count);
        Assert.DoesNotContain(PrintTurnPrompt_Builder.ALREADY_EXECUTED_PREFIX, prompts[1]);
        Assert.Contains($"{PrintTurnPrompt_Builder.ALREADY_EXECUTED_PREFIX} 1, 2", prompts[2]);
        Assert.DoesNotContain(PrintTurnPrompt_Builder.ALREADY_EXECUTED_PREFIX, prompts[3]);
    }

    [Fact]
    public async Task FreshMode_StartsANewSessionEveryTurn_WithTheRoleCommand()
    {
        using var harness = new PrintRunnerTestHarness("general");
        harness.Register_General();
        harness.Write_Scenario("""{"default":{"result":"general supervisor online\n\nnothing open"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.GeneralChannelFile, "status?", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, "general").ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.GeneralChannelFile, "and now?", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, "general").ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var invocations = harness.Read_Invocations();
        Assert.Equal(2, invocations.Count);

        foreach (var invocation in invocations)
        {
            var args = PrintRunnerTestHarness.Args(invocation);
            Assert.Contains("--session-id", args);
            Assert.DoesNotContain("--resume", args);
            Assert.Equal("/general-supervisor", args[^1]);
            Assert.Equal("general", args[args.IndexOf("--name") + 1]);
            Assert.Equal("general", invocation["env"]!["AIORCH_ROLE"]!.GetValue<string>());
        }

        Assert.NotEqual(invocations[0]["session_id"]!.GetValue<string>(), invocations[1]["session_id"]!.GetValue<string>());

        // The general's entries are signed 'supervisor' — the word its channel has always carried.
        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.GeneralChannelFile));
        Assert.Equal(2, entries.Count(entry => entry.Author == ChannelAuthors.Supervisor && entry.Subject == "general supervisor online"));
    }

    [Fact]
    public async Task StopAsync_KillsAnInFlightTurn_AndLeavesTheEntryPending()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"delay_ms":20000}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "slow", "a");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => dispatcher.InFlightCount == 1, PrintRunnerTestHarness.GENEROUS));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await dispatcher.Stop_Async();
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"stop took {stopwatch.Elapsed}");
        Assert.Equal(0, dispatcher.InFlightCount);
        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Empty(state.ExecutedTurns);
        Assert.Empty(Assert.Single(state.Cursors).Delivered);

        // A SHUTDOWN IS NOT A TIMEOUT. Closing the app while a turn runs cancels the same linked
        // token the per-turn timeout uses; read as one, three ordinary restarts spent all three
        // attempts and stalled a session that had done nothing wrong — with a `turn_ended … timeout`
        // in the member's channel each time, describing a failure that never happened.
        Assert.Equal(0, state.FailedAttempts);
        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.DoesNotContain(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_ENDED_SUBJECT));
        Assert.DoesNotContain(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_STALLED_SUBJECT));
    }

    [Fact]
    public void AClosedMember_IsNotDispatched()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Store.Close_Member(orchId, memberId);
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "go", "a");
        dispatcher.Tick(DateTime.Now);
        Thread.Sleep(300);
        dispatcher.Tick(DateTime.Now);

        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Empty(harness.Read_Invocations());
    }
}
