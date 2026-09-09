using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Tests.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// F3, PROBE (a), END TO END — the probe that parked a healthy session for fourteen hours with
/// <c>FailedAttempts = 0</c>.
///
/// <para>
/// A turn can SUCCEED and still reach <c>Record_Failure</c>: <c>Run_Turn_Async</c> routes a turn
/// whose reply could not be appended (the channel was locked for the whole append budget) into the
/// failure path so the same traffic is retried. The result it carries is the SUCCESSFUL one, so
/// <c>result.ResultText</c> is the MODEL'S OWN WORDS — and the usage-limit gate was a
/// case-insensitive <c>Contains("limit")</c> over exactly that text. A report mentioning a limit
/// and a reset clause therefore bought the session an appointment, spent no attempt, and left it
/// out of the world until somebody typed <c>/resume</c>.
/// </para>
/// <para>
/// It is its own file because it has to HOLD the channel gate, which is process-wide state — the
/// <see cref="CHANNEL_LOCK_COLLECTION"/> is what keeps it from colliding with the other tests that
/// do the same.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class ASucceededTurnIsNeverParkedForALimitTests
{
    /// <summary>
    /// A perfectly ordinary report that happens to discuss a limit and quote a reset clause — which
    /// is what a session working on THIS feature writes all day.
    /// </summary>
    const string REPORT_ABOUT_A_LIMIT =
        "REPORT\n\nThe parser now reads the refusal 'You've hit your weekly limit · resets 5am (Europe/Berlin)' "
        + "and the suite is green. Nothing is blocked.";

    [Fact]
    public async Task ATurnThatExitedZero_ButWhoseReplyTheChannelRefused_SpendsAnAttemptAndIsNeverParked()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var channelFile = harness.Paths.Get_ImplementerChannelFile(orchId, memberId);

        harness.Write_Scenario(
            """{"default":{"result":"REPORT_TEXT"}}"""
                .Replace("REPORT_TEXT", REPORT_ABOUT_A_LIMIT.Replace("\n", "\\n")));

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        // The brief goes in BEFORE the gate is taken — the turn has to have something to answer.
        Assert.True(ChannelAppender.Append_SessionEntry(channelFile, ChannelAuthors.Supervisor, "BRIEF", "start on the parser", DateTime.Now));

        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var holder = Task.Run(() => ChannelWrite_Lock.Try_Run_Serialised(channelFile, TimeSpan.FromSeconds(30), () =>
        {
            held.Set();
            release.Wait(TimeSpan.FromMinutes(2));
        }, out _));

        Assert.True(held.Wait(TimeSpan.FromSeconds(10)), "the test could not take the channel gate it is testing against");

        bool counted;

        try
        {
            // Every append the turn makes now fails: its reply, and then its turn_ended record. The
            // turn itself exits 0 with is_error false and no api_error_status.
            counted = PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= 1, TimeSpan.FromSeconds(60));
        }
        finally
        {
            release.Set();
            await holder;
        }

        await dispatcher.Stop_Async();

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);

        // THE WHOLE FINDING: the appointment must not exist. Before the fix this was a stamp up to a
        // day out, with FailedAttempts still 0 — so the session could never stall, never alert, and
        // never be retried by anything except the owner noticing.
        Assert.Null(state.RetryNotBeforeUtc);
        Assert.True(counted, $"the successful turn was PARKED instead of counted — failed_attempts {state.FailedAttempts}, retry_not_before {state.RetryNotBeforeUtc:O}");
        Assert.True(state.FailedAttempts >= 1, "the attempt was not counted, so this turn can never stall or alert");
    }
}
