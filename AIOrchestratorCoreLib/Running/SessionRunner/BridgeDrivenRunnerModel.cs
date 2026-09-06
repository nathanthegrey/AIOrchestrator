using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.SessionRunner;

/// <summary>
/// Starting a bridge-driven session starts NO PROCESS HERE. It writes the session's state file — a
/// fresh session id, the role, the cwd, the channel it is woken by — and from then on the turn
/// dispatcher owns it: for <see cref="SessionRunners.Print"/> every inbound entry becomes one
/// <c>claude -p</c> invocation, for <see cref="SessionRunners.Stream"/> one living process is fed
/// the entries on its stdin. A restart finds the file already there and keeps it: the session id is
/// the transcript, and keeping it IS the resume.
///
/// One model for both because the registration is genuinely identical — the difference between the
/// two runners is entirely in how a turn reaches the model, which is
/// <see cref="TurnExecutor.ITurnExecutor"/>'s business and not this one's.
/// </summary>
internal sealed class BridgeDrivenRunnerModel(SessionRunners kind, ISupervisionPaths paths, IOrchestrationLog log) : ISessionRunner
{
    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestrationLog _log = log;

    public SessionRunners Kind { get; } = kind;

    public void Start(ISessionLaunch launch)
    {
        var word = SessionRunner_Names.Get_Word(Kind);
        var stateFile = PrintSessionState_Store.Get_StateFile(_paths, launch.Role, launch.OrchId, launch.MemberId);
        var existing = PrintSessionState_Store.Read_OrNull(stateFile);

        if (existing != null)
        {
            _log.Log_Info(launch.OrchId, $"{launch.Role} '{launch.MemberId}' is {word}-run — already registered (session {existing.SessionId}, {existing.ExecutedTurns.Count} turn(s) executed); its transcript resumes on the next inbound entry");
            Warn_AboutSingleSource(launch);
            return;
        }

        var state = PrintSessionState_Factory.Create_New(
            Guid.NewGuid().ToString(),
            launch.Role,
            launch.OrchId,
            launch.MemberId,
            launch.WorkingDirectory,
            launch.Model,
            PrintSessionState_Store.Resolve_ChannelFile(_paths, launch.Role, launch.OrchId, launch.MemberId));

        PrintSessionState_Store.Write(stateFile, state);

        _log.Log_Info(launch.OrchId, $"{launch.Role} '{launch.MemberId}' is {word}-run — registered (session {state.SessionId}); no window, no process until its first inbound entry");
        Warn_AboutSingleSource(launch);
    }

    /// <summary>
    /// Said EVERY time the session is registered, not once per app life: it is the one thing about
    /// this stage a supervisor's owner has to know, and a line that appears only on the very first
    /// registration is a line nobody reads.
    /// </summary>
    void Warn_AboutSingleSource(ISessionLaunch launch)
    {
        if (Runner_Support.Describe_SingleSourceLimit_OrNull(launch.Role) is string limit)
            _log.Log_Warning(launch.OrchId, $"'{launch.MemberId}' is {SessionRunner_Names.Get_Word(Kind)}-run and {limit}");
    }
}
