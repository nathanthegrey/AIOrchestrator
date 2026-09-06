using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running.TurnExecutor;

/// <summary>
/// HOW ONE TURN REACHES THE MODEL — the seam between the dispatcher's scheduling and the transport.
///
/// <para>
/// The dispatcher owns everything that is the same for every transport, and that list is the whole
/// argument for this interface existing: the queue (which IS the channel), the coalesce window, the
/// per-session serialisation, the slot limits, idempotency by request id, the attempt counter and
/// its stall alert, the channel entry, the <c>turn_ended</c> record. An executor owns only the part
/// that differs — a process per turn with the prompt on stdin, or one living process with the
/// prompt on a pipe. Duplicating the first list per transport is how two schedulers drift into two
/// different answers about whether a turn already ran.
/// </para>
/// <para>
/// It takes the PENDING ENTRIES rather than a finished prompt because the two transports disagree
/// about the first turn: print boots by putting the role command on the command line and sends no
/// prompt at all, while a stream has no command line to put it on and boots by sending it as its
/// first message. That decision belongs to whoever knows the transport.
/// </para>
/// </summary>
public interface ITurnExecutor
{
    SessionRunners Kind { get; }

    Task<ITurnResult> Execute_Async(
        IPrintSessionState state,
        IRoleRunnerConfig roleConfig,
        string sessionId,
        bool resumeTranscript,
        string requestId,
        IReadOnlyList<IChannelEntry> pending,
        IReadOnlyList<int> alreadyExecutedTurns,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases whatever the executor is holding for this session — a resident process, in the only
    /// implementation that holds anything. Called when a turn's transport failed and the next
    /// attempt must start clean, and when the session is closed.
    /// </summary>
    void Release(string orchId, string memberId);

    /// <summary>Ends every resident process. The app is exiting; a stream left running would outlive it.</summary>
    Task Stop_Async();
}
