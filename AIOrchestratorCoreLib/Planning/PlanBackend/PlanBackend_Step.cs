using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>What one orchestration's synchronisation did this tick, for the log.</summary>
/// <param name="RequestsIngested">Approved upstream requests newly written into PLAN.md.</param>
/// <param name="RowsReportedClosed">Ledger lines of upstream origin newly reported as [x].</param>
/// <param name="OrchestrationClosedReported">Whether the orchestration's own closure was reported this tick.</param>
/// <param name="Failure">A backend call that threw. The gesture is retried next tick; nothing is recorded as done.</param>
public readonly record struct PlanBackendOutcome(
    int RequestsIngested,
    int RowsReportedClosed,
    bool OrchestrationClosedReported,
    string? Failure)
{
    public bool DidAnything => RequestsIngested > 0 || RowsReportedClosed > 0 || OrchestrationClosedReported;
}

/// <summary>
/// ONE ORCHESTRATION'S ROUND TRIP WITH ITS PLAN BACKEND, one tick's worth: pull in what was approved
/// upstream, push out what has closed here.
///
/// <para>
/// A STEP RATHER THAN ENGINE CODE, for the reason this repository has written down twice already: the
/// bridge engine is <c>internal sealed</c> with a single entry point, so anything decided inside it is
/// unpinnable by construction (see <see cref="LedgerHealth_Step"/>, and
/// <see cref="PlanLedgerRows_Builder"/> on the same argument from the window's side). Everything that
/// can be got wrong is here, where the suite reaches it; the engine keeps the call and the loop.
/// </para>
/// <para>
/// PLAN.md IS WRITTEN BEFORE THE BACKEND IS TOLD, and the state file is written after each. That order
/// is the whole idempotence story: the visible effect lands first, the record of it lands last, so an
/// interruption costs at worst a REPEATED call to a backend that is required to tolerate one — never a
/// duplicated row in the owner's plan, and never a request silently dropped between the two.
/// </para>
/// <para>
/// NOTHING HERE PARSES A CHANNEL. Evidence for a closed row is supplied by the caller as a delegate and
/// asked for only when there is actually a row to report — reading a conversation on every tick to
/// build a message that is almost never sent is the cost this system spends its days removing.
/// </para>
/// </summary>
public static class PlanBackend_Step
{
    public static PlanBackendOutcome Sync(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        string displayName,
        bool isClosed,
        Func<PlanRowEvidence> readEvidence,
        DateTime nowLocal)
    {
        var state = PlanBackend_StateStore.Read(paths, orchId);

        if (isClosed)
            return Report_Closure(backend, paths, orchId, displayName, state);

        var planFile = paths.Get_PlanFile(orchId);

        // NO PLAN FILE, NO SYNCHRONISATION. Every orchestration is seeded with one at launch, so an
        // absent file means this folder is not ready (or not an orchestration at all) — and writing
        // one here would put a plan where the launcher decided there should not be one yet.
        if (!File.Exists(planFile))
            return new PlanBackendOutcome(0, 0, false, null);

        var ingested = Ingest_ApprovedRequests(backend, paths, orchId, planFile, ref state, nowLocal, out var ingestFailure);
        var closed = Report_ClosedRows(backend, paths, orchId, planFile, ref state, readEvidence, out var closureFailure);

        return new PlanBackendOutcome(ingested, closed, false, ingestFailure ?? closureFailure);
    }

    static int Ingest_ApprovedRequests(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        string planFile,
        ref PlanBackendState state,
        DateTime nowLocal,
        out string? failure)
    {
        failure = null;

        IReadOnlyList<ApprovedPlanRequest> approved;

        try
        {
            approved = backend.List_ApprovedRequests(orchId);
        }
        catch (Exception ex)
        {
            failure = $"listing approved requests failed: {ex.Message}";
            return 0;
        }

        if (approved.Count == 0)
            return 0;

        var ingested = 0;

        foreach (var request in approved)
        {
            if (string.IsNullOrWhiteSpace(request.RequestId) || state.Knows(request.RequestId))
                continue;

            var write = PlanRequest_Writer.Write_Request(Read_Text_Safe(planFile), request, nowLocal);

            if (write.LedgerRowRef.Length == 0)
                continue;

            if (write.Changed)
                Atomic_FileWriter.Write_AllText(planFile, write.PlanText);

            // PERSISTED BEFORE THE BACKEND IS TOLD. A crash here leaves a request that is in the plan
            // and unacknowledged, which the next tick fixes by acknowledging it again; the other order
            // leaves a request the app has forgotten and would ingest a second time.
            var tracked = new TrackedPlanRequest(request.RequestId, write.LedgerRowRef, write.OwnerRequestNumber, null, null);

            state = state.With(tracked);
            PlanBackend_StateStore.Write(paths, orchId, state);

            try
            {
                backend.Acknowledge_Request(orchId, request.RequestId, write.LedgerRowRef);
            }
            catch (Exception ex)
            {
                failure ??= $"acknowledging '{request.RequestId}' failed: {ex.Message}";
                continue;
            }

            state = state.With(tracked with { AcknowledgedUtc = DateTime.UtcNow });
            PlanBackend_StateStore.Write(paths, orchId, state);

            ingested++;
        }

        return ingested;
    }

    static int Report_ClosedRows(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        string planFile,
        ref PlanBackendState state,
        Func<PlanRowEvidence> readEvidence,
        out string? failure)
    {
        failure = null;

        var owed = state.Requests.Where(request => request.ClosedReportedUtc == null).ToList();

        if (owed.Count == 0)
            return 0;

        var progress = PlanLedger_Parser.Parse_OrNull(Read_Text_Safe(planFile));

        if (progress == null)
            return 0;

        // TOP-LEVEL LINES ONLY, matched on text — the same identity LedgerTransition_Detector uses, so
        // the two readers of this file cannot disagree about which line is which.
        var doneTexts = progress.Lines
            .Where(line => !line.IsSubTask && line.Marker == "x")
            .Select(line => line.Text)
            .ToHashSet(StringComparer.Ordinal);

        var reported = 0;

        foreach (var request in owed)
        {
            if (!doneTexts.Contains(request.LedgerRowRef))
                continue;

            try
            {
                backend.Report_RowClosed(orchId, request.LedgerRowRef, readEvidence());
            }
            catch (Exception ex)
            {
                failure ??= $"reporting '{request.LedgerRowRef}' closed failed: {ex.Message}";
                continue;
            }

            // RECORDED ONLY AFTER THE CALL RETURNED. A second sighting of the same transition — the
            // next tick, or the tick after a restart — finds this stamp and says nothing.
            state = state.With(request with { ClosedReportedUtc = DateTime.UtcNow });
            PlanBackend_StateStore.Write(paths, orchId, state);

            reported++;
        }

        return reported;
    }

    static PlanBackendOutcome Report_Closure(
        IPlanBackend backend,
        ISupervisionPaths paths,
        string orchId,
        string displayName,
        PlanBackendState state)
    {
        // NEVER ENGAGED WITH THIS ORCHESTRATION, so there is nothing to close upstream. An orchestration
        // that ran entirely on PLAN.md must not announce itself to a backend it never spoke to.
        if (state.Requests.Count == 0 || state.OrchestrationClosedReportedUtc != null)
            return new PlanBackendOutcome(0, 0, false, null);

        var progress = PlanLedger_Parser.Parse_OrNull(Read_Text_Safe(paths.Get_PlanFile(orchId)));

        try
        {
            backend.Report_OrchestrationClosed(orchId, Describe_Summary(displayName, progress));
        }
        catch (Exception ex)
        {
            return new PlanBackendOutcome(0, 0, false, $"reporting the orchestration closed failed: {ex.Message}");
        }

        PlanBackend_StateStore.Write(paths, orchId, state with { OrchestrationClosedReportedUtc = DateTime.UtcNow });

        return new PlanBackendOutcome(0, 0, true, null);
    }

    /// <summary>
    /// One line, and it says what the ledger SAYS rather than what the orchestration achieved — the
    /// app has no opinion on the second, and a summary that implied one would be the "merged is not
    /// verified" mistake in a sentence.
    /// </summary>
    public static string Describe_Summary(string displayName, PlanProgress.IPlanProgress? progress)
    {
        if (progress == null)
            return $"{displayName}: closed with no task ledger.";

        var dropped = progress.NotDoing > 0 ? $", {progress.NotDoing} not doing" : string.Empty;
        var open = progress.Total - progress.Done;
        var unfinished = open > 0 ? $", {open} still open" : string.Empty;

        return $"{displayName}: closed with {progress.Done}/{progress.Total} ledger lines done{dropped}{unfinished}.";
    }

    static string Read_Text_Safe(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return string.Empty;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}
