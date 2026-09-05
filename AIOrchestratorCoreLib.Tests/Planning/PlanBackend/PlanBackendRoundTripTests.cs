using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// THE WHOLE ROUND TRIP, END TO END, against a second backend: two approved requests become two rows
/// and two ledger lines, one of those lines closes, and the closure is reported ONCE — however many
/// times the tick sees it.
///
/// <para>
/// "Once, however many times it is seen" is the requirement worth pinning hardest, because the failure
/// is invisible from inside the app: the second report costs nothing here and lands upstream as a
/// duplicate closure on somebody else's board. The app's guard is a persisted stamp, so the test
/// re-runs the sync exactly as the tick would rather than asserting on an in-memory field.
/// </para>
/// </summary>
public class PlanBackendRoundTripTests : IDisposable
{
    const string ORCH_ID = "repo-slug-1";

    const string SEEDED_PLAN = """
        # PLAN — repo (repo-slug-1)

        - [>] agree the direction with the owner

        ## PARKED — found, not asked for

        """;

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly RecordingPlanBackend _backend = new();

    public PlanBackendRoundTripTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-planbackend-trip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, ORCH_ID));
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        File.WriteAllText(_paths.Get_PlanFile(ORCH_ID), SEEDED_PLAN);

        _backend.Approved.Add(new ApprovedPlanRequest("FIN-D-100", "add the export button", "I want an export button"));
        _backend.Approved.Add(new ApprovedPlanRequest("FIN-D-101", "cache the report query", "the report page is slow"));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    PlanBackendOutcome Sync(bool isClosed = false)
    {
        return PlanBackend_Step.Sync(
            _backend,
            _paths,
            ORCH_ID,
            "repo-slug-1",
            isClosed,
            () => new PlanRowEvidence(DateTime.UtcNow, "owner-channel #12 FROM supervisor", "export button shipped"),
            DateTime.Now);
    }

    string Plan()
    {
        return File.ReadAllText(_paths.Get_PlanFile(ORCH_ID));
    }

    void Mark_Done(string ledgerRowRef)
    {
        File.WriteAllText(_paths.Get_PlanFile(ORCH_ID), Plan().Replace($"- [ ] {ledgerRowRef}", $"- [x] {ledgerRowRef}"));
    }

    /// <summary>Two approved requests, two rows in the table, two lines in the ledger, two acknowledgements.</summary>
    [Fact]
    public void TwoApprovedRequestsBecomeTwoRowsAndTwoLedgerLines()
    {
        var outcome = Sync();

        Assert.Equal(2, outcome.RequestsIngested);
        Assert.Null(outcome.Failure);

        var progress = PlanLedger_Parser.Parse_OrNull(Plan());

        Assert.Equal(3, progress!.Total);
        Assert.Contains(progress.Lines, line => line.Text == "add the export button");
        Assert.Contains(progress.Lines, line => line.Text == "cache the report query");

        Assert.Contains("| 1 |", Plan());
        Assert.Contains("| 2 |", Plan());
        Assert.Contains("I want an export button", Plan());

        Assert.Equal(
            [("FIN-D-100", "add the export button"), ("FIN-D-101", "cache the report query")],
            _backend.Acknowledged.Select(call => (call.RequestId, call.LedgerRowRef)));
    }

    /// <summary>A second tick with the same approved list writes nothing and says nothing.</summary>
    [Fact]
    public void ASecondTickIngestsNothing()
    {
        Sync();
        var planAfterFirst = Plan();

        var second = Sync();

        Assert.Equal(0, second.RequestsIngested);
        Assert.Equal(planAfterFirst, Plan());
        Assert.Equal(2, _backend.Acknowledged.Count);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The line closes, the transition is seen twice — the tick runs every
    /// minute and the ledger does not change back — and the backend is told once.
    /// </summary>
    [Fact]
    public void AClosedRowIsReportedOnceEvenWhenTheTransitionIsSeenTwice()
    {
        Sync();
        Mark_Done("add the export button");

        var first = Sync();
        var second = Sync();

        Assert.Equal(1, first.RowsReportedClosed);
        Assert.Equal(0, second.RowsReportedClosed);

        var call = Assert.Single(_backend.Closed);

        Assert.Equal(ORCH_ID, call.OrchId);
        Assert.Equal("add the export button", call.LedgerRowRef);
        Assert.Equal("owner-channel #12 FROM supervisor", call.Evidence.ChannelEntryRef);
    }

    /// <summary>
    /// And it survives a restart, because the stamp is on disk rather than in the process. A
    /// memory-only guard passes the test above and re-reports every closed line on the next launch —
    /// which is the ordinary case, not the exotic one: this app is restarted daily.
    /// </summary>
    [Fact]
    public void AndItIsStillReportedOnlyOnceAfterARestart()
    {
        Sync();
        Mark_Done("add the export button");
        Sync();

        // A "restart" is a fresh backend and a fresh step call — the only thing that carries over is
        // the state file, which is exactly the claim.
        var afterRestart = new RecordingPlanBackend();
        afterRestart.Approved.AddRange(_backend.Approved);

        PlanBackend_Step.Sync(
            afterRestart,
            _paths,
            ORCH_ID,
            "repo-slug-1",
            isClosed: false,
            () => new PlanRowEvidence(DateTime.UtcNow, null, null),
            DateTime.Now);

        Assert.Empty(afterRestart.Closed);
        Assert.Empty(afterRestart.Acknowledged);
    }

    /// <summary>
    /// The other line is still open, so it is not reported — the closure report follows the MARKER,
    /// not the fact that the request was ingested.
    /// </summary>
    [Fact]
    public void AnOpenRowIsNotReported()
    {
        Sync();
        Mark_Done("add the export button");
        Sync();

        Assert.DoesNotContain(_backend.Closed, call => call.LedgerRowRef == "cache the report query");
    }

    /// <summary>
    /// A throwing backend costs this tick and nothing more: nothing is recorded as reported, and the
    /// next tick sends it again. At-least-once is what the wire guarantees.
    /// </summary>
    [Fact]
    public void AFailedReportIsRetriedOnTheNextTick()
    {
        Sync();
        Mark_Done("add the export button");

        _backend.ThrowOnReportClosed = new InvalidOperationException("upstream unreachable");

        var failed = Sync();

        Assert.Equal(0, failed.RowsReportedClosed);
        Assert.Contains("upstream unreachable", failed.Failure);

        _backend.ThrowOnReportClosed = null;

        Assert.Equal(1, Sync().RowsReportedClosed);
        Assert.Single(_backend.Closed);
    }

    /// <summary>
    /// PARKED IS NEVER SYNCHRONISED. A discovery nobody asked for is local by definition (decision 22),
    /// so a parked item that happens to be marked done reaches no backend — and a plan whose parked
    /// section grows produces no ingestion either.
    /// </summary>
    [Fact]
    public void AParkedItemIsNotAnUpstreamRow()
    {
        Sync();

        File.WriteAllText(
            _paths.Get_PlanFile(ORCH_ID),
            Plan().Replace(
                "## PARKED — found, not asked for",
                "## PARKED — found, not asked for\n\n- [x] the tailer's retry count is unbounded"));

        Sync();

        Assert.DoesNotContain(_backend.Closed, call => call.LedgerRowRef.Contains("retry count"));
    }

    /// <summary>
    /// The orchestration closes: one summary, once, and it reads what the LEDGER says rather than
    /// claiming anything was verified.
    /// </summary>
    [Fact]
    public void ClosingTheOrchestrationIsReportedOnce()
    {
        Sync();
        Mark_Done("add the export button");
        Sync();

        var closing = Sync(isClosed: true);
        var again = Sync(isClosed: true);

        Assert.True(closing.OrchestrationClosedReported);
        Assert.False(again.OrchestrationClosedReported);

        var call = Assert.Single(_backend.OrchestrationsClosed);

        Assert.Contains("1/3 ledger lines done", call.Summary);
        Assert.Contains("2 still open", call.Summary);
    }
}
