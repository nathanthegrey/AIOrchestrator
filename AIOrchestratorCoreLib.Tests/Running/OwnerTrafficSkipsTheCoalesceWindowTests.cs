using System.Diagnostics;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE WAIVER AT ITS CALL SITE — that the dispatcher actually asks
/// <c>CoalesceWindow_Policy.Is_Waived</c> before it decides to wait, and that member traffic still
/// serves the window it was written for.
///
/// <para>
/// The policy's own tests are pure and say nothing about whether anybody consults it: delete the call
/// in <c>Consider_Session</c> and they all stay green. These two are the pair that reddens — one that
/// the owner does not wait, one that a member does — driven through the real dispatcher against the
/// FakeClaude stub.
/// </para>
/// <para>
/// FIVE SECONDS OF WINDOW, measured against a stopwatch, because the subject IS a duration. The
/// numbers are deliberately far apart: the owner's turn must start in under two and a half seconds
/// (it should start on the first pass, ~0) and the member's must not start before four and a half
/// (its window is five). A machine under load can only make the first assertion fail by an order of
/// magnitude, which is a real failure rather than a race — the shape MultiSourceSupervisorTests
/// documents from the 2026-09-06 load-42 run.
/// </para>
/// <para>
/// TURN START, not turn end: <see cref="IPrintTurnDispatcher.Is_TurnInFlight"/> is true the moment the
/// dispatcher stops waiting, so neither number carries the cost of spawning a process.
/// </para>
/// </summary>
public class OwnerTrafficSkipsTheCoalesceWindowTests
{
    /// <summary>The window under test. Long enough that "waited" and "did not wait" cannot be confused.</summary>
    const double COALESCE_SECONDS = 5;

    /// <summary>
    /// Measured on the VPS on 2026-09-09: 11–12 s median from the owner's Telegram message to their
    /// supervisor's turn starting, three of them this window. The owner's ruling was that their own
    /// message does not serve it (the aggregation window and the mirror tick are the other two thirds,
    /// each addressed in its own place).
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOwnerMessage_StartsTheTurnWithoutServingTheWindow()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", coalesceSeconds: COALESCE_SECONDS);
        harness.Register_Supervisor("repo-1", SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"on it"}}""");

        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile("repo-1"), "restart the crew.", DateTime.Now));

        var stopwatch = Stopwatch.StartNew();

        Assert.True(
            PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turn_InFlight(dispatcher), PrintRunnerTestHarness.GENEROUS),
            "the owner's message never started a turn at all.");

        var waited = stopwatch.Elapsed;

        await dispatcher.Stop_Async();

        Assert.True(
            waited < TimeSpan.FromSeconds(2.5),
            $"the owner served the coalesce window: their turn started after {waited.TotalSeconds:F2} s of a {COALESCE_SECONDS:F0} s window.");
    }

    /// <summary>
    /// THE CONTROL, and the expensive half of the rule. An extra supervisor wake costs ~1 M input
    /// tokens (measured), so a crew filing reports within a second of each other must still be read in
    /// ONE turn — the waiver is the owner's alone, and a change that dropped the window for everybody
    /// would pass the test above and reduce this one to a per-report bill.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMemberReport_StillServesTheWholeWindow()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", coalesceSeconds: COALESCE_SECONDS);
        harness.Register_Supervisor("repo-1", SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var member = harness.Store.Add_Member("repo-1", MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(ChannelAppender.Append_SessionEntry(
            harness.Paths.Get_ImplementerChannelFile("repo-1", member), ChannelAuthors.Implementer, "REPORT — parser", "Parser done.", DateTime.Now));

        var stopwatch = Stopwatch.StartNew();

        Assert.True(
            PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turn_InFlight(dispatcher), PrintRunnerTestHarness.GENEROUS),
            "the member's report never started a turn at all.");

        var waited = stopwatch.Elapsed;

        await dispatcher.Stop_Async();

        Assert.True(
            waited >= TimeSpan.FromSeconds(4.5),
            $"a member's report skipped the coalesce window: the turn started after {waited.TotalSeconds:F2} s of a {COALESCE_SECONDS:F0} s window.");
    }

    static bool Turn_InFlight(IPrintTurnDispatcher dispatcher)
    {
        return dispatcher.Is_TurnInFlight("repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
    }
}
