using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.TurnLiveness;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// WHICH CEILING A TURN HAS, in one place — because two exist since 2026-09-11 and every reader of
/// "the turn timeout" has to agree on which: the dispatcher that starts the turn, the drain that
/// waits for it at shutdown, and the host that must not SIGKILL the drain. A reader left on the old
/// single value would drain for thirty minutes a turn allowed to run for two hours, and cut it —
/// the 2026-09-09 incident again, by a different road.
/// </summary>
public static class TurnTimeout_Rule
{
    /// <summary>
    /// A braked member — implementer or reviewer, with the silence brake on — gets the member ceiling;
    /// everyone else, and a member whose brake is off, keeps <see cref="IRunnerConfigs.TurnTimeout"/>.
    /// The long ceiling is only safe BECAUSE something else catches a hang sooner.
    /// </summary>
    public static TimeSpan Resolve_ForRole(SessionRoles role, IRunnerConfigs configs)
    {
        return Is_BrakedMember(role, configs) ? configs.MemberTurnTimeout : configs.TurnTimeout;
    }

    /// <summary>The longest any turn may run under these settings — what a shutdown has to wait for.</summary>
    public static TimeSpan Resolve_Longest(IRunnerConfigs configs)
    {
        if (configs.MemberSilenceLimit <= TimeSpan.Zero)
            return configs.TurnTimeout;

        return configs.MemberTurnTimeout > configs.TurnTimeout ? configs.MemberTurnTimeout : configs.TurnTimeout;
    }

    static bool Is_BrakedMember(SessionRoles role, IRunnerConfigs configs)
    {
        return TurnSilenceBrake_Factory.Applies_ToRole(role) && configs.MemberSilenceLimit > TimeSpan.Zero;
    }
}
