using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.SessionRunner;

/// <summary>
/// Starting a print-run session starts NO PROCESS. It writes the session's state file — a fresh
/// session id, the role command, the cwd — and from then on the print dispatcher turns every
/// inbound channel entry into one <c>claude -p</c> invocation. A restart finds the file already
/// there and keeps it: the session id is the transcript, and keeping it IS the resume.
/// </summary>
internal sealed class PrintSessionRunnerModel(ISupervisionPaths paths, IOrchestrationLog log) : ISessionRunner
{
    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestrationLog _log = log;

    public SessionRunners Kind => SessionRunners.Print;

    public void Start(ISessionLaunch launch)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(_paths, launch.Role, launch.OrchId, launch.MemberId);
        var existing = PrintSessionState_Store.Read_OrNull(stateFile);

        if (existing != null)
        {
            _log.Log_Info(launch.OrchId, $"{launch.Role} '{launch.MemberId}' is print-run — already registered (session {existing.SessionId}, {existing.ExecutedTurns.Count} turn(s) executed); its transcript resumes on the next inbound entry");
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

        _log.Log_Info(launch.OrchId, $"{launch.Role} '{launch.MemberId}' is print-run — registered (session {state.SessionId}); no window, no process until its first inbound entry");
    }
}
