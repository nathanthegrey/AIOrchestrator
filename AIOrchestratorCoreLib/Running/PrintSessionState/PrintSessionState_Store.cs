using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.PrintSessionState;

/// <summary>
/// Reads and writes <c>print-session.json</c>, and knows where it lives for each role: beside a
/// member's channel, in the general supervisor's home, and (for the roles this stage does not
/// run) beside the orchestration's session.json under a role-prefixed name. The file's presence
/// is what tells the watchdog "this slot has no pid file BY DESIGN", so the location rule lives
/// here and nowhere else.
/// </summary>
public static class PrintSessionState_Store
{
    public const string STATE_FILE_NAME = "print-session.json";

    public static string Get_StateFile(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.Implementer or SessionRoles.Reviewer or SessionRoles.Solo => Path.Combine(paths.Get_ImplementerFolder(orchId, memberId), STATE_FILE_NAME),
            SessionRoles.General => Path.Combine(paths.GeneralFolder, STATE_FILE_NAME),
            SessionRoles.Supervisor => Path.Combine(paths.Get_OrchestrationFolder(orchId), $".supervisor.{STATE_FILE_NAME}"),
            SessionRoles.Communicator => Path.Combine(paths.Get_OrchestrationFolder(orchId), $".communicator.{STATE_FILE_NAME}"),
            _ => throw new Exception($"Unhandled SessionRoles: {role}"),
        };
    }

    /// <summary>The one channel this role's turns are triggered from (see <see cref="Runner_Support"/>).</summary>
    public static string Resolve_ChannelFile(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.Implementer or SessionRoles.Reviewer => paths.Get_ImplementerChannelFile(orchId, memberId),
            SessionRoles.Solo or SessionRoles.Supervisor or SessionRoles.Communicator => paths.Get_OwnerChannelFile(orchId),
            SessionRoles.General => paths.GeneralChannelFile,
            _ => throw new Exception($"Unhandled SessionRoles: {role}"),
        };
    }

    public static bool Exists(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return File.Exists(Get_StateFile(paths, role, orchId, memberId));
    }

    /// <summary>
    /// Clears a registration — the role is no longer print-run, so the session goes back to a
    /// terminal. Returns whether a file was actually removed. Best-effort: a file that cannot be
    /// deleted is reported by the caller, never thrown at a spawn that is otherwise fine.
    /// </summary>
    public static bool Delete_IfExists(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        var stateFile = Get_StateFile(paths, role, orchId, memberId);

        try
        {
            if (!File.Exists(stateFile))
                return false;

            File.Delete(stateFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Null for an absent file. A corrupt one throws — that is a session whose identity is gone, not a default.</summary>
    public static IPrintSessionState? Read_OrNull(string stateFile)
    {
        if (!File.Exists(stateFile))
            return null;

        var root = JsonNode.Parse(File.ReadAllText(stateFile)) as JsonObject
            ?? throw new Exception($"Print session state at '{stateFile}' is not a JSON object");

        List<IExecutedTurn> executed = [];

        if (root["executed_turns"] is JsonArray turns)
        {
            foreach (var node in turns)
            {
                if (node is JsonObject turn)
                    executed.Add(Parse_Turn(turn, stateFile));
            }
        }

        return PrintSessionState_Factory.Create(
            Read_String(root, "session_id", stateFile),
            root["session_started"]?.GetValue<bool>() ?? executed.Count > 0,
            SessionRole_Names.Parse_OrNull(Read_String(root, "role", stateFile)) ?? throw new Exception($"Print session state at '{stateFile}' names an unknown role '{root["role"]}'"),
            Read_String(root, "orch_id", stateFile),
            Read_String(root, "member_id", stateFile),
            Read_String(root, "working_directory", stateFile),
            root["model"]?.GetValue<string?>(),
            Read_String(root, "channel_file", stateFile),
            root["last_handled_entry_index"]?.GetValue<int>() ?? 0,
            root["next_turn_number"]?.GetValue<int>() ?? Math.Max(1, executed.Count + 1),
            root["failed_attempts"]?.GetValue<int>() ?? 0,
            executed);
    }

    public static void Write(string stateFile, IPrintSessionState state)
    {
        var turns = new JsonArray();

        foreach (var turn in state.ExecutedTurns)
        {
            turns.Add(new JsonObject
            {
                ["turn"] = turn.TurnNumber,
                ["request_id"] = turn.RequestId,
                ["first_entry_index"] = turn.FirstEntryIndex,
                ["last_entry_index"] = turn.LastEntryIndex,
                ["ended_utc"] = turn.EndedUtc.ToString("o"),
                ["outcome"] = turn.Outcome,
                ["cost_usd"] = turn.CostUsd,
            });
        }

        var root = new JsonObject
        {
            ["session_id"] = state.SessionId,
            ["session_started"] = state.SessionStarted,
            ["role"] = SessionRole_Names.Get_ConfigKey(state.Role),
            ["orch_id"] = state.OrchId,
            ["member_id"] = state.MemberId,
            ["working_directory"] = state.WorkingDirectory,
            ["model"] = state.Model,
            ["channel_file"] = state.ChannelFilePath,
            ["last_handled_entry_index"] = state.LastHandledEntryIndex,
            ["next_turn_number"] = state.NextTurnNumber,
            ["failed_attempts"] = state.FailedAttempts,
            ["executed_turns"] = turns,
        };

        Atomic_FileWriter.Write_AllText(stateFile, root.ToJsonString(JsonWriting.INDENTED));
    }

    static IExecutedTurn Parse_Turn(JsonObject turn, string stateFile)
    {
        return ExecutedTurn_Factory.Create(
            turn["turn"]?.GetValue<int>() ?? throw new Exception($"Print session state at '{stateFile}' has an executed turn without a number"),
            Read_String(turn, "request_id", stateFile),
            turn["first_entry_index"]?.GetValue<int>() ?? 0,
            turn["last_entry_index"]?.GetValue<int>() ?? 0,
            DateTime.TryParse(turn["ended_utc"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var ended) ? ended.ToUniversalTime() : DateTime.MinValue,
            turn["outcome"]?.GetValue<string>() ?? "unknown",
            turn["cost_usd"]?.GetValue<double?>());
    }

    static string Read_String(JsonObject node, string key, string stateFile)
    {
        return node[key]?.GetValue<string>() ?? throw new Exception($"Print session state at '{stateFile}' is missing '{key}'");
    }
}
