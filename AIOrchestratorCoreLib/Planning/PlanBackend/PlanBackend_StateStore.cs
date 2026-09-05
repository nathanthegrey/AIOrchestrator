using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// Reads and writes <c>.plan-backend.json</c> beside the orchestration's PLAN.md.
///
/// <para>
/// AN UNREADABLE FILE READS AS EMPTY, NEVER AS AN ERROR THAT STOPS THE TICK — but "empty" is not
/// free: it means the next tick re-ingests, which is why the writer keyed on the request id refuses
/// to add a row twice. The two guards are deliberately different (one persisted, one derived from the
/// file itself), so a lost state file degrades to a duplicate <c>Acknowledge_Request</c> call rather
/// than a duplicated row in the owner's plan.
/// </para>
/// <para>
/// WRITTEN THROUGH <see cref="Atomic_FileWriter"/>. A truncated state file is exactly the memory loss
/// above, and this one is rewritten on every ingestion.
/// </para>
/// </summary>
public static class PlanBackend_StateStore
{
    public static PlanBackendState Read(ISupervisionPaths paths, string orchId)
    {
        try
        {
            var file = paths.Get_PlanBackendStateFile(orchId);

            if (!File.Exists(file))
                return PlanBackendState.Empty();

            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject root)
                return PlanBackendState.Empty();

            List<TrackedPlanRequest> requests = [];

            if (root["requests"] is JsonArray array)
            {
                foreach (var node in array)
                {
                    if (node is not JsonObject entry)
                        continue;

                    var requestId = entry["requestId"]?.GetValue<string?>();
                    var ledgerRowRef = entry["ledgerRowRef"]?.GetValue<string?>();

                    if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(ledgerRowRef))
                        continue;

                    requests.Add(new TrackedPlanRequest(
                        requestId,
                        ledgerRowRef,
                        entry["ownerRequestNumber"]?.GetValue<int>() ?? 0,
                        Read_Utc_OrNull(entry, "acknowledgedUtc"),
                        Read_Utc_OrNull(entry, "closedReportedUtc")));
                }
            }

            return new PlanBackendState(requests, Read_Utc_OrNull(root, "orchestrationClosedReportedUtc"));
        }
        catch
        {
            return PlanBackendState.Empty();
        }
    }

    public static void Write(ISupervisionPaths paths, string orchId, PlanBackendState state)
    {
        var array = new JsonArray();

        foreach (var request in state.Requests)
        {
            array.Add(new JsonObject
            {
                ["requestId"] = request.RequestId,
                ["ledgerRowRef"] = request.LedgerRowRef,
                ["ownerRequestNumber"] = request.OwnerRequestNumber,
                ["acknowledgedUtc"] = Write_Utc_OrNull(request.AcknowledgedUtc),
                ["closedReportedUtc"] = Write_Utc_OrNull(request.ClosedReportedUtc),
            });
        }

        var root = new JsonObject
        {
            ["requests"] = array,
            ["orchestrationClosedReportedUtc"] = Write_Utc_OrNull(state.OrchestrationClosedReportedUtc),
        };

        Atomic_FileWriter.Write_AllText(paths.Get_PlanBackendStateFile(orchId), root.ToJsonString(JsonWriting.INDENTED));
    }

    static DateTime? Read_Utc_OrNull(JsonObject root, string key)
    {
        var text = root[key]?.GetValue<string?>();

        return DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    static string? Write_Utc_OrNull(DateTime? value)
    {
        return value?.ToUniversalTime().ToString("o");
    }
}
