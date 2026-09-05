using AIOrchestratorCoreLib.Running.ExecutedTurn;

namespace AIOrchestratorCoreLib.Running.PrintSessionState;

/// <summary>
/// Everything the bridge knows about one print-run session, persisted beside its channel
/// (<c>print-session.json</c>). It is the session's whole identity from the bridge's side: the
/// transcript id to resume, where to run, which channel entry was last handed to it, and the
/// turns already executed. Immutable — every change is a new instance written whole.
/// </summary>
public interface IPrintSessionState
{
    /// <summary>The transcript: chosen by the bridge, passed as --session-id once and --resume after (Fresh mode replaces it every turn).</summary>
    string SessionId { get; }

    /// <summary>
    /// Whether a process has already been launched under <see cref="SessionId"/> — i.e. the CLI has
    /// claimed that transcript. MEASURED, not assumed: a second <c>--session-id</c> with the same
    /// uuid is refused outright (<c>Error: Session ID &lt;uuid&gt; is already in use.</c>, exit 1), so a
    /// first turn that fails could never be retried without this flag; and <c>--resume</c> of a
    /// transcript whose turn was killed mid-flight works (both measured against 2.1.261 on
    /// 2026-09-06). It is written BEFORE the process starts, so a bridge that dies mid-turn still
    /// knows the id is spent.
    /// </summary>
    bool SessionStarted { get; }
    SessionRoles Role { get; }
    string OrchId { get; }
    string MemberId { get; }
    string WorkingDirectory { get; }
    string? Model { get; }

    /// <summary>The one channel whose inbound entries trigger this session's turns.</summary>
    string ChannelFilePath { get; }

    /// <summary>The highest inbound entry index a completed turn was handed. Entries above it are pending.</summary>
    int LastHandledEntryIndex { get; }

    /// <summary>The number the next turn will carry (and therefore its request id).</summary>
    int NextTurnNumber { get; }

    /// <summary>Consecutive failed attempts (timeout / error) of the CURRENT turn; reset when a turn completes.</summary>
    int FailedAttempts { get; }
    IReadOnlyList<IExecutedTurn> ExecutedTurns { get; }
}
