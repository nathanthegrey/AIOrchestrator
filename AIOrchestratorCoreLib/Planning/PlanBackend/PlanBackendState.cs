namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// One upstream request this orchestration has taken on, and how far its round trip has got.
/// </summary>
/// <param name="RequestId">The upstream identifier — the key that makes ingestion happen once.</param>
/// <param name="LedgerRowRef">The ledger line's text, as written into PLAN.md.</param>
/// <param name="OwnerRequestNumber">Its row in the OWNER REQUESTS table, for a human following the trail.</param>
/// <param name="AcknowledgedUtc">When the backend was told the request became a ledger line, or null if that call has not landed yet.</param>
/// <param name="ClosedReportedUtc">When the backend was told the line reached [x]. Null while it is still owed.</param>
public sealed record TrackedPlanRequest(
    string RequestId,
    string LedgerRowRef,
    int OwnerRequestNumber,
    DateTime? AcknowledgedUtc,
    DateTime? ClosedReportedUtc);

/// <summary>
/// WHAT THIS ORCHESTRATION HAS ALREADY SAID UPSTREAM — the app's half of the idempotence contract.
///
/// <para>
/// It is persisted rather than remembered because the alternative fails in the ordinary case, not an
/// exotic one: the app is restarted daily, and a memory-only record would re-ingest every request and
/// re-report every closed line on the next launch. Same class as the resume/`--continue` defect the
/// general supervisor was made stateless to avoid — a boot that re-runs a completed gesture.
/// </para>
/// </summary>
/// <param name="Requests">Every request ever ingested here, in ingestion order. Append-only.</param>
/// <param name="OrchestrationClosedReportedUtc">When the closure of the whole orchestration was reported, or null.</param>
public sealed record PlanBackendState(
    IReadOnlyList<TrackedPlanRequest> Requests,
    DateTime? OrchestrationClosedReportedUtc)
{
    public static PlanBackendState Empty()
    {
        return new PlanBackendState([], null);
    }

    public bool Knows(string requestId)
    {
        return Requests.Any(request => request.RequestId == requestId);
    }

    public PlanBackendState With(TrackedPlanRequest tracked)
    {
        List<TrackedPlanRequest> requests = [.. Requests.Where(request => request.RequestId != tracked.RequestId), tracked];

        return this with { Requests = requests };
    }
}
