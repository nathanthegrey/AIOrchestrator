namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// Which roles the print runner can run IN THIS STAGE: the ones whose inbound traffic is a single
/// channel — a member reads its spoke, a solo and the general supervisor read their owner channel.
/// An orchestration supervisor reads the owner channel AND every spoke, and the communicator reads
/// the supervisor's transcript; both need a multi-source trigger this stage does not build (the
/// study's verdict puts the supervisor on <c>--bg</c> in stage 2). A role configured
/// <c>runner: print</c> that is not supported here is launched in a terminal, with a warning that
/// says so — never silently.
/// </summary>
public static class PrintRunner_Support
{
    public static bool Supports(SessionRoles role)
    {
        return role is SessionRoles.Implementer or SessionRoles.Reviewer or SessionRoles.Solo or SessionRoles.General;
    }

    public static string Describe_Unsupported(SessionRoles role)
    {
        return $"role '{SessionRole_Names.Get_ConfigKey(role)}' is configured runner: print, which this stage supports only for implementer, reviewer, solo and general — launched in a terminal instead";
    }
}
