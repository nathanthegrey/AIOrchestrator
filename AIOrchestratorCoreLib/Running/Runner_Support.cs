namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// WHICH RUNNER CAN RUN WHICH ROLE IN THIS STAGE — one table, read by the launcher (what to spawn),
/// the watchdog (which slots have no pid file by design) and the dispatcher (which registrations
/// still mean anything). Three readers of one rule; when they disagreed the symptom was two
/// sessions answering one brief.
///
/// <para>
/// The limit is never the transport, it is the TRIGGER. A bridge-driven session is woken by ONE
/// channel — <see cref="PrintSessionState_Store.Resolve_ChannelFile"/> names it — so a role whose
/// inbound traffic arrives on several (an orchestration supervisor also reads every spoke) is
/// supported only in the sense that the owner's channel drives it: member traffic does NOT wake a
/// bridge-driven supervisor in this stage, and its role command is told to read its spokes at the
/// end of every turn because of it. The communicator, whose input is the supervisor's transcript
/// rather than a channel at all, is not supported by either.
/// </para>
/// <para>
/// <see cref="SessionRunners.Bg"/> supports NOTHING here: the transport is not implemented in this
/// stage (its wake-up needs the messaging socket, deliberately out of scope), so a role configured
/// for it is launched in a terminal with a warning rather than silently doing nothing.
/// </para>
/// </summary>
public static class Runner_Support
{
    public static bool Supports(SessionRunners runner, SessionRoles role)
    {
        return runner switch
        {
            SessionRunners.Terminal => true,
            SessionRunners.Print => role is SessionRoles.Implementer or SessionRoles.Reviewer or SessionRoles.Solo or SessionRoles.General,
            // The supervisor is the reason the stream exists: it is the owner's phone line, and the
            // study measured its turn at 1.27 s there against 5.77 s in print.
            SessionRunners.Stream => role is SessionRoles.Implementer or SessionRoles.Reviewer or SessionRoles.Solo or SessionRoles.General or SessionRoles.Supervisor,
            SessionRunners.Bg => false,
            _ => false,
        };
    }

    /// <summary>
    /// Runners whose sessions are driven by the bridge rather than by a shell: no pid file, no
    /// window, no watcher. The watchdog exempts these slots and the dispatcher owns them.
    /// </summary>
    public static bool Is_BridgeDriven(SessionRunners runner)
    {
        return runner is SessionRunners.Print or SessionRunners.Stream;
    }

    public static string Describe_Unsupported(SessionRunners runner, SessionRoles role)
    {
        var supported = string.Join(", ", SessionRole_Names.ALL.Where(known => Supports(runner, known)).Select(SessionRole_Names.Get_ConfigKey));
        var what = supported.Length == 0 ? "no role in this stage" : $"only {supported}";

        return $"role '{SessionRole_Names.Get_ConfigKey(role)}' is configured runner: {SessionRunner_Names.Get_Word(runner)}, which this stage supports for {what} — launched in a terminal instead";
    }

    /// <summary>
    /// Said once when a bridge-driven session starts on a role whose traffic arrives on more than
    /// one channel. Not a warning about a defect — a statement of what this stage's trigger does
    /// and does not do, where the person reading the log can act on it.
    /// </summary>
    public static string? Describe_SingleSourceLimit_OrNull(SessionRoles role)
    {
        return role == SessionRoles.Supervisor
            ? "it is woken by the OWNER channel only: a member writing in its spoke does not start a turn in this stage, so its role command reads the spokes itself at the end of every turn"
            : null;
    }
}
