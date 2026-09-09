using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE QUOTA REFUSAL, END TO END. Measured on the VPS 2026-09-08/09: a turn refused with
/// <c>api_error_status: 429</c> and "You've hit your weekly limit · resets 5am (Europe/Berlin)"
/// was retried three times a minute apart and then sat stalled — 167 to 509 minutes of dead
/// orchestration, twice for a whole night. These pin the two halves of the fix: a reset the
/// dispatcher can read is WAITED for, and one it cannot read changes nothing at all.
/// </summary>
public class PrintTurnLimitResetTests
{
    const string WEEKLY_REFUSAL = "You've hit your weekly limit · resets 5am (Europe/Berlin)";

    /// <summary>The measured refusal on the first turn, then a normal answer — the fake's own scenario shape.</summary>
    static string Refusal_Scenario()
    {
        return """{"turns":[{"is_error":true,"api_error_status":429,"result":"REFUSAL"}],"default":{"result":"REPORT\n\nback after the reset"}}"""
            .Replace("REFUSAL", WEEKLY_REFUSAL);
    }

    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }

    static string Read_Log(PrintRunnerTestHarness harness, string orchId)
    {
        var file = harness.Paths.Get_OrchestrationLogFile(orchId);
        return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
    }

    /// <summary>
    /// <see cref="PrintRunnerTestHarness.Drive_Until"/> with the clock pushed forward — the only way
    /// to reach a reset instant that is up to a day away without waiting for it. The dispatcher takes
    /// the time as an argument precisely so a test can say when it is.
    /// </summary>
    static bool Drive_Until_At(IPrintTurnDispatcher dispatcher, TimeSpan skew, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            dispatcher.Tick(DateTime.Now + skew);

            if (condition())
                return true;

            Thread.Sleep(100);
        }

        dispatcher.Tick(DateTime.Now + skew);
        return condition();
    }

    [Fact]
    public async Task A429ThatNamesItsReset_WaitsForThatInstant_SpendsNoAttemptAndIgnoresTheBackoff()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario(Refusal_Scenario());
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc != null, PrintRunnerTestHarness.GENEROUS));

        var deferred = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        var retryAt = deferred.RetryNotBeforeUtc!.Value;

        // NOT AN ATTEMPT. Three tries a minute apart cannot outlast a quota window, so spending them
        // on one is spending the session's whole allowance on nothing.
        Assert.Equal(0, deferred.FailedAttempts);
        Assert.Single(harness.Read_Invocations());
        Assert.Equal(DateTimeKind.Utc, retryAt.Kind);
        Assert.True(retryAt > DateTime.UtcNow, $"the retry was scheduled in the past ({retryAt:O})");
        Assert.True(retryAt <= DateTime.UtcNow.AddDays(1) + PrintTurn_Words.LIMIT_RESET_MARGIN, $"the retry was scheduled more than a day out ({retryAt:O})");

        // AND NOT THE BACKOFF EITHER: several backoff windows pass and nothing starts.
        for (var pass = 0; pass < 5; pass++)
        {
            Thread.Sleep(120);
            dispatcher.Tick(DateTime.Now);
        }

        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Single(harness.Read_Invocations());
        Assert.Equal(0, harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts);

        // The channel says WHEN it resumes, and from which words it read that.
        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        var alert = Assert.Single(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_LIMITED_SUBJECT));
        Assert.Contains(retryAt.ToString("HH:mm"), alert.Subject);
        Assert.Contains("resets 5am (Europe/Berlin)", alert.Body);
        Assert.Contains($"{orchId}/{memberId}/1", alert.Body);

        // And so does the log.
        var log = Read_Log(harness, orchId);
        Assert.Contains("hit a usage limit", log);
        Assert.Contains($"retry scheduled at {retryAt:HH:mm} UTC", log);
        Assert.Contains("resets 5am (Europe/Berlin)", log);

        // Past the reset the SAME request id runs — turn 1, and reported as attempt 1 because none was spent.
        Assert.True(Drive_Until_At(dispatcher, TimeSpan.FromDays(2), () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var done = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Equal(2, harness.Read_Invocations().Count);
        Assert.Null(done.RetryNotBeforeUtc);
        Assert.Equal(0, done.FailedAttempts);
        Assert.Equal(1, Assert.Single(done.ExecutedTurns).TurnNumber);
        Assert.Contains("back after the reset", harness.Read_Channel(orchId, memberId));

        var replayed = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.DoesNotContain(replayed, entry => entry.Subject.Contains(PrintTurn_Words.TURN_STALLED_SUBJECT));
        Assert.Single(replayed, entry => entry.Author == ChannelAuthors.Implementer);
    }

    [Fact]
    public async Task TheScheduledRetry_IsInTheStateFile_SoABridgeRestartKeepsIt()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario(Refusal_Scenario());
        var first = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(first, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc != null, PrintRunnerTestHarness.GENEROUS));
        await first.Stop_Async();

        var scheduled = harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc;

        // A SECOND DISPATCHER HAS NO TRACKERS — everything it knows about this session it read from
        // print-session.json. If the wait lived only in memory the restart would run the turn at once
        // and burn another refusal, which is exactly the loop the VPS was in.
        var restarted = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        for (var pass = 0; pass < 5; pass++)
        {
            Thread.Sleep(120);
            restarted.Tick(DateTime.Now);
        }

        Assert.Single(harness.Read_Invocations());
        Assert.Equal(scheduled, harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc);

        Assert.True(Drive_Until_At(restarted, TimeSpan.FromDays(2), () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await restarted.Stop_Async();

        Assert.Equal(2, harness.Read_Invocations().Count);
        Assert.Null(harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc);
    }

    [Fact]
    public async Task A429WhoseResetCannotBeRead_KeepsTodaysThreeAttemptsAndStall_AndSaysWhatItCouldNotRead()
    {
        // DECISION 21: a guard that cannot evaluate its predicate says so and ALLOWS. The refusal is
        // real but names no clock, so nothing about the old path changes — and the log names which
        // predicate failed, because "hook error" is the silence this repo keeps paying for.
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"is_error":true,"api_error_status":429,"result":"You've hit your weekly limit","stderr":"429 rate_limit_error"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= PrintTurn_Words.MAX_ATTEMPTS, TimeSpan.FromSeconds(40)));
        await dispatcher.Stop_Async();

        var stalled = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, stalled.FailedAttempts);
        Assert.Null(stalled.RetryNotBeforeUtc);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, harness.Read_Invocations().Count);

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.Single(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_STALLED_SUBJECT));
        Assert.DoesNotContain(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_LIMITED_SUBJECT));

        // ONE LOG LINE, NAMING WHAT IT COULD NOT READ — and nothing anywhere the owner gets texted.
        var log = Read_Log(harness, orchId);
        Assert.Contains("no reset time could be read", log);
        // The log is JSONL and its writer escapes an apostrophe, so the quoted text is matched from
        // the first plain word on — asserting on the apostrophe would pin the encoder, not the line.
        Assert.Contains("hit your weekly limit", log);
        Assert.DoesNotContain("retry scheduled at", log);
    }

    /// <summary>The measured refusal, scoped to one member's <c>--name</c> so a second member in the same scenario file can behave differently.</summary>
    static string Refusal_Scenario_ForNamedSession(string orchId, string memberId)
    {
        return ("""{"sessions":{"SESSION_NAME":{"turns":[{"is_error":true,"api_error_status":429,"result":"REFUSAL"}]}},"default":{"result":"REPORT\n\nall good"}}"""
            .Replace("SESSION_NAME", $"{orchId}-{memberId}"))
            .Replace("REFUSAL", WEEKLY_REFUSAL);
    }

    /// <summary>
    /// THE GAP: /resume's own help text is "use when the usage limit resets", but a deferred session's
    /// appointment used to hold regardless of the fresh traffic /resume appends, because
    /// Consider_Session checks RetryNotBeforeUtc before it ever looks at what is pending. This pins the
    /// fix — IPrintTurnDispatcher.Clear_LimitDeferrals — end to end: still waiting on the real clock,
    /// then cleared, then the very next tick (no time skew needed — the whole point is not waiting for
    /// the real reset) runs the turn without spending an attempt.
    /// </summary>
    [Fact]
    public async Task ResumeClearsADeferredSession_SoTheVeryNextTickRunsTheTurn_WithNoWaitForTheRealReset()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario(Refusal_Scenario());
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc != null, PrintRunnerTestHarness.GENEROUS));

        var retryAt = harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc!.Value;

        // STILL WAITING on the real clock — the reset is up to a day out, and nothing but /resume changes that.
        for (var pass = 0; pass < 3; pass++)
        {
            Thread.Sleep(120);
            dispatcher.Tick(DateTime.Now);
        }

        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Single(harness.Read_Invocations());
        Assert.NotNull(harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc);

        // /resume's override.
        dispatcher.Clear_LimitDeferrals();
        Assert.Null(harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc);

        // The very next tick runs it — real clock, no skew.
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var done = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Equal(2, harness.Read_Invocations().Count);
        Assert.Equal(0, done.FailedAttempts);
        Assert.Null(done.RetryNotBeforeUtc);

        var log = Read_Log(harness, orchId);
        Assert.Contains(memberId, log);
        Assert.Contains("was waiting on a usage limit until", log);
        Assert.Contains(retryAt.ToString("HH:mm"), log);
        Assert.Contains("resume cleared it", log);
    }

    /// <summary>
    /// KEEP IT HONEST: a session /resume finds with no appointment is not rewritten and gets no line —
    /// inventing a clear that did not happen is the same lie /resume exists to end, in the other
    /// direction.
    /// </summary>
    [Fact]
    public async Task ResumeClear_LeavesANonDeferredSessions_StateFileUntouched_AndLogsNothingForIt()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, deferredMember) = harness.Register_Member(MemberKinds.Implementer);
        var (_, normalMember) = harness.Register_Member(MemberKinds.Implementer, orchId);
        harness.Write_Scenario(Refusal_Scenario_ForNamedSession(orchId, deferredMember));
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, deferredMember, "BRIEF", "start on the parser");
        Append_Supervisor(harness, orchId, normalMember, "BRIEF", "start on the docs");

        Assert.True(PrintRunnerTestHarness.Drive_Until(
            dispatcher,
            () => harness.Read_State(SessionRoles.Implementer, orchId, deferredMember).RetryNotBeforeUtc != null
                && harness.Read_State(SessionRoles.Implementer, orchId, normalMember).ExecutedTurns.Count == 1,
            PrintRunnerTestHarness.GENEROUS));

        var normalStateFile = PrintSessionState_Store.Get_StateFile(harness.Paths, SessionRoles.Implementer, orchId, normalMember);
        var normalBefore = File.ReadAllText(normalStateFile);
        var retryAt = harness.Read_State(SessionRoles.Implementer, orchId, deferredMember).RetryNotBeforeUtc!.Value;

        dispatcher.Clear_LimitDeferrals();

        // UNTOUCHED, BYTE FOR BYTE — not just "same values", the file was never opened for writing.
        Assert.Equal(normalBefore, File.ReadAllText(normalStateFile));
        Assert.Null(harness.Read_State(SessionRoles.Implementer, orchId, deferredMember).RetryNotBeforeUtc);

        var log = Read_Log(harness, orchId);
        Assert.Contains(deferredMember, log);
        Assert.Contains(retryAt.ToString("HH:mm"), log);
        Assert.Equal(1, log.Split("was waiting on a usage limit until").Length - 1);

        await dispatcher.Stop_Async();
    }
}
