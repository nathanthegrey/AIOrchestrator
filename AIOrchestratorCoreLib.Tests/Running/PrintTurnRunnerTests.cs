using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The process seam's own rules, pinned WITHOUT the dispatcher — because the dispatcher guards the
/// same outcome a second time, and a test that passes through both pins neither (CLAUDE.md: never
/// assert on a state with two routes to it; a control proved that the dispatcher-level guard alone
/// kept the shutdown test green while this rule was reverted).
/// </summary>
public class PrintTurnRunnerTests
{
    static readonly IReadOnlyDictionary<string, string> NO_ENVIRONMENT = new Dictionary<string, string>();

    static (IPrintTurnRunner Runner, string WorkDir) Build()
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"aiorch-turn-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "fake-claude-scenario.json"), """{"default":{"delay_ms":20000}}""");

        return (PrintTurnRunner_Factory.Create(PrintRunnerTestHarness.Fake_Invocation()), workDir);
    }

    /// <summary>
    /// SHUTDOWN IS NOT A TIMEOUT. Both cancel the same linked source, so reported as one, closing
    /// the app while a turn ran spent an attempt, wrote a `turn_ended … timeout` entry into the
    /// member's channel, and three ordinary restarts stalled a session that had done nothing wrong.
    /// The caller's token means "stop", and stopping is not a result.
    /// </summary>
    [Fact]
    public async Task ACancelledTurn_Throws_InsteadOfReportingATimeout()
    {
        var (runner, workDir) = Build();

        try
        {
            using var cancellation = new CancellationTokenSource();
            var turn = runner.Run_Async(["-p", "--output-format", "json"], "prompt", workDir, NO_ENVIRONMENT, TimeSpan.FromMinutes(5), cancellation.Token);

            await Task.Delay(1500);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    /// <summary>...while a real timeout, with nobody cancelling, IS reported as one and kills the tree.</summary>
    [Fact]
    public async Task ATimedOutTurn_IsReportedAsATimeout()
    {
        var (runner, workDir) = Build();

        try
        {
            var result = await runner.Run_Async(["-p", "--output-format", "json"], "prompt", workDir, NO_ENVIRONMENT, TimeSpan.FromSeconds(2), CancellationToken.None);

            Assert.True(result.TimedOut);
            Assert.Equal("timeout", AIOrchestratorCoreLib.Running.TurnOutcomes.Describe(result));
            Assert.True(result.Elapsed < TimeSpan.FromSeconds(15), $"the process was not killed promptly: {result.Elapsed}");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
