using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Running.RunnerConfigs;

/// <summary>
/// The config.json shape of the runner configuration, in one place for the loader and the saver:
/// <code>
/// "runners": {
///   "implementer": { "runner": "print", "resume": "transcript", "permission_mode": null },
///   "general":     { "runner": "print", "resume": "fresh" }
/// },
/// "printRunner": { "maxConcurrentTurns": 10, "maxConcurrentTurnsPerOrchestration": 3,
///                  "turnTimeoutMinutes": 30, "coalesceSeconds": 3 }
/// </code>
/// Tolerant on the way in — an absent block, an absent role, an unknown word all read as the
/// default, because a typo in a hand-edited file must not stop the app from starting — and
/// explicit on the way out: every role and every limit is written, so the owner sees the whole
/// surface and its current values without reading this file.
/// </summary>
public static class RunnerConfigs_Json
{
    public const string RUNNERS_KEY = "runners";
    public const string LIMITS_KEY = "printRunner";
    public const string RUNNER_KEY = "runner";
    public const string RESUME_KEY = "resume";
    public const string PERMISSION_MODE_KEY = "permission_mode";
    public const string MAX_CONCURRENT_TURNS_KEY = "maxConcurrentTurns";
    public const string MAX_CONCURRENT_TURNS_PER_ORCHESTRATION_KEY = "maxConcurrentTurnsPerOrchestration";
    public const string TURN_TIMEOUT_MINUTES_KEY = "turnTimeoutMinutes";
    public const string COALESCE_SECONDS_KEY = "coalesceSeconds";

    public static IRunnerConfigs Parse(JsonObject? configRoot)
    {
        var defaults = RunnerConfigs_Factory.Create_Default();

        if (configRoot == null)
            return defaults;

        Dictionary<SessionRoles, IRoleRunnerConfig> roles = [];

        if (configRoot[RUNNERS_KEY] is JsonObject runnersNode)
        {
            foreach (var role in SessionRole_Names.ALL)
            {
                if (runnersNode[SessionRole_Names.Get_ConfigKey(role)] is JsonObject roleNode)
                    roles[role] = Parse_Role(role, roleNode);
            }
        }

        var limits = configRoot[LIMITS_KEY] as JsonObject;

        return RunnerConfigs_Factory.Create(
            roles,
            Read_PositiveInt_OrDefault(limits, MAX_CONCURRENT_TURNS_KEY, defaults.MaxConcurrentTurns),
            Read_PositiveInt_OrDefault(limits, MAX_CONCURRENT_TURNS_PER_ORCHESTRATION_KEY, defaults.MaxConcurrentTurnsPerOrchestration),
            TimeSpan.FromMinutes(Read_PositiveDouble_OrDefault(limits, TURN_TIMEOUT_MINUTES_KEY, defaults.TurnTimeout.TotalMinutes)),
            TimeSpan.FromSeconds(Read_NonNegativeDouble_OrDefault(limits, COALESCE_SECONDS_KEY, defaults.CoalesceWindow.TotalSeconds)));
    }

    /// <summary>Sets both blocks on <paramref name="configRoot"/>, replacing whatever was there.</summary>
    public static void Write(JsonObject configRoot, IRunnerConfigs configs)
    {
        var runnersNode = new JsonObject();

        foreach (var role in SessionRole_Names.ALL)
        {
            var roleConfig = configs.Get_ForRole(role);

            runnersNode[SessionRole_Names.Get_ConfigKey(role)] = new JsonObject
            {
                [RUNNER_KEY] = SessionRunner_Names.Get_Word(roleConfig.Runner),
                [RESUME_KEY] = ResumeMode_Names.Get_Word(roleConfig.Resume),
                [PERMISSION_MODE_KEY] = roleConfig.PermissionMode,
            };
        }

        configRoot[RUNNERS_KEY] = runnersNode;
        configRoot[LIMITS_KEY] = new JsonObject
        {
            [MAX_CONCURRENT_TURNS_KEY] = configs.MaxConcurrentTurns,
            [MAX_CONCURRENT_TURNS_PER_ORCHESTRATION_KEY] = configs.MaxConcurrentTurnsPerOrchestration,
            [TURN_TIMEOUT_MINUTES_KEY] = configs.TurnTimeout.TotalMinutes,
            [COALESCE_SECONDS_KEY] = configs.CoalesceWindow.TotalSeconds,
        };
    }

    static IRoleRunnerConfig Parse_Role(SessionRoles role, JsonObject roleNode)
    {
        var defaults = RoleRunnerConfig_Factory.Create_Default(role);

        return RoleRunnerConfig_Factory.Create(
            SessionRunner_Names.Parse_OrNull(Read_String_OrNull(roleNode, RUNNER_KEY)) ?? defaults.Runner,
            ResumeMode_Names.Parse_OrNull(Read_String_OrNull(roleNode, RESUME_KEY)) ?? defaults.Resume,
            Read_String_OrNull(roleNode, PERMISSION_MODE_KEY));
    }

    static string? Read_String_OrNull(JsonObject node, string key)
    {
        try
        {
            return node[key]?.GetValue<string?>();
        }
        catch
        {
            return null;
        }
    }

    static int Read_PositiveInt_OrDefault(JsonObject? node, string key, int fallback)
    {
        var value = Read_PositiveDouble_OrDefault(node, key, fallback);
        return value >= 1 ? (int)value : fallback;
    }

    static double Read_PositiveDouble_OrDefault(JsonObject? node, string key, double fallback)
    {
        var value = Read_NonNegativeDouble_OrDefault(node, key, fallback);
        return value > 0 ? value : fallback;
    }

    static double Read_NonNegativeDouble_OrDefault(JsonObject? node, string key, double fallback)
    {
        try
        {
            var value = node?[key]?.GetValue<double>();
            return value != null && value.Value >= 0 ? value.Value : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
