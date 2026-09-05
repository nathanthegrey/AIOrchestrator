using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.PrintTurnDispatcher;

/// <summary>
/// THE QUEUE IS THE CHANNEL. A print session has no process to hold a queue in, so its pending
/// work is defined by the file: every inbound entry above the state's last-handled index is
/// pending, and one turn takes all of them. That gives FIFO per session, coalescing (entries that
/// land within the window ride together), and restart recovery (a bridge that died mid-turn finds
/// the same entries pending) without a queue object anywhere.
///
/// Per tick, per registered session: read the channel, select the pending entries, wait out the
/// coalesce window, then start ONE turn on a background task — never more than one per session,
/// never more than the global and per-orchestration slots allow. The task runs the process,
/// writes the session's entry from the JSON result, records a <c>turn_ended</c> entry for the
/// supervisor, and persists the state. A failed attempt (timeout, non-zero exit, <c>is_error</c>)
/// is counted and retried after a backoff under the SAME request id; at <see cref="MAX_ATTEMPTS"/>
/// the session stalls — an alert entry, no further attempts — until new traffic arrives.
///
/// Idempotency: a request id already in the executed list is skipped without running. The prompt
/// of the first resumed turn after a bridge start lists the executed turns, so a transcript that
/// remembers answering does not answer twice (decision 8).
///
/// The slot limits are read ONCE, at construction — a semaphore cannot be resized — so changing
/// them in config.json takes effect at the next app start. Everything else (runner, resume mode,
/// timeout, coalesce window) is read on every tick.
/// </summary>
internal sealed class PrintTurnDispatcherModel : IPrintTurnDispatcher
{
    const int MAX_ATTEMPTS = PrintTurn_Words.MAX_ATTEMPTS;
    const string RUNNER_ENV_VAR = PrintTurn_Words.RUNNER_ENV_VAR;
    const string TURN_ENDED_SUBJECT = PrintTurn_Words.TURN_ENDED_SUBJECT;
    const string TURN_STALLED_SUBJECT = PrintTurn_Words.TURN_STALLED_SUBJECT;
    const int ENTRY_APPEND_ATTEMPTS = 3;
    const int ENTRY_APPEND_RETRY_MILLISECONDS = 300;
    static readonly TimeSpan STOP_GRACE = TimeSpan.FromSeconds(15);

    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IPrintTurnRunner _turnRunner;
    readonly IOrchestrationLog _log;
    readonly TimeSpan _retryBackoff;

    readonly Lock _lock = new();
    readonly CancellationTokenSource _shutdown = new();
    readonly Dictionary<string, Task> _inFlight = [];
    readonly Dictionary<string, SessionTracker> _trackers = [];
    readonly Dictionary<string, SemaphoreSlim> _orchestrationSlots = [];
    readonly HashSet<string> _warnedStaleRegistrations = [];
    readonly SemaphoreSlim _globalSlots;
    readonly int _slotsPerOrchestration;

    public PrintTurnDispatcherModel(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestratorConfigProvider configProvider,
        IPrintTurnRunner turnRunner,
        IOrchestrationLog log,
        TimeSpan retryBackoff)
    {
        _paths = paths;
        _store = store;
        _configProvider = configProvider;
        _turnRunner = turnRunner;
        _log = log;
        _retryBackoff = retryBackoff;

        var limits = configProvider.Get_Current().Runners;
        _globalSlots = new SemaphoreSlim(limits.MaxConcurrentTurns, limits.MaxConcurrentTurns);
        _slotsPerOrchestration = limits.MaxConcurrentTurnsPerOrchestration;
    }

    /// <summary>What the dispatcher remembers about a session BETWEEN ticks and only until restart — nothing here is truth, the state file is.</summary>
    sealed class SessionTracker
    {
        public int NewestPendingIndex;
        public DateTime NewestPendingSeenAt;
        public DateTime? LastFailureAt;
        public int StalledAtIndex = -1;

        /// <summary>True until this dispatcher instance has run the session once — the "resumed after a restart" signal for the prompt.</summary>
        public bool FirstTurnSinceStart = true;
    }

    public int InFlightCount
    {
        get
        {
            lock (_lock)
                return _inFlight.Count;
        }
    }

    public void Tick(DateTime nowLocal)
    {
        if (_shutdown.IsCancellationRequested)
            return;

        var configs = _configProvider.Get_Current().Runners;

        foreach (var registered in Discover_RegisteredSessions())
        {
            // A STALE REGISTRATION IS NOT A MANDATE. The state file says a session WAS print-run;
            // config.json says whether it still is. Flipped back to terminal, the launcher spawns a
            // window for this member — and without this check the dispatcher would keep firing
            // `claude -p` turns into the same channel, two sessions answering one brief. The file is
            // deleted by the launcher at the next spawn; until then, this is the gate.
            if (!Is_StillPrintRun(configs, registered.Role))
            {
                if (_warnedStaleRegistrations.Add($"{registered.OrchId}/{registered.MemberId}"))
                    _log.Log_Warning(registered.OrchId, $"'{registered.MemberId}' has a print-session registration but role '{SessionRole_Names.Get_ConfigKey(registered.Role)}' is no longer configured runner: print — no turns are dispatched for it (the registration is cleared at its next spawn)");

                continue;
            }

            try
            {
                Consider_Session(registered.StateFile, registered.Role, registered.OrchId, registered.MemberId, nowLocal, configs);
            }
            catch (Exception ex)
            {
                _log.Log_Error(registered.OrchId, $"Print dispatcher: '{registered.MemberId}' could not be considered this tick", ex);
            }
        }
    }

    public async Task Stop_Async()
    {
        _shutdown.Cancel();

        Task[] pending;

        lock (_lock)
            pending = [.. _inFlight.Values];

        if (pending.Length == 0)
            return;

        try
        {
            await Task.WhenAll(pending).WaitAsync(STOP_GRACE);
        }
        catch
        {
            // Turns end by cancellation or are abandoned after the grace — the app is exiting either way.
        }
    }

    // ----- discovery -----

    IReadOnlyList<(string StateFile, SessionRoles Role, string OrchId, string MemberId)> Discover_RegisteredSessions()
    {
        List<(string, SessionRoles, string, string)> found = [];

        var generalFile = PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch.SessionLaunch_Factory.GENERAL_MEMBER_ID);

        if (File.Exists(generalFile))
            found.Add((generalFile, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch.SessionLaunch_Factory.GENERAL_MEMBER_ID));

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var role = SessionRole_Names.From_MemberKind(MemberKind_Ids.Resolve_Kind(member.MemberId));
                var stateFile = PrintSessionState_Store.Get_StateFile(_paths, role, session.OrchId, member.MemberId);

                if (File.Exists(stateFile))
                    found.Add((stateFile, role, session.OrchId, member.MemberId));
            }
        }

        return found;
    }

    static bool Is_StillPrintRun(IRunnerConfigs configs, SessionRoles role)
    {
        return configs.Get_ForRole(role).Runner == SessionRunners.Print && PrintRunner_Support.Supports(role);
    }

    // ----- per-session decision -----

    void Consider_Session(string stateFile, SessionRoles role, string orchId, string memberId, DateTime nowLocal, IRunnerConfigs configs)
    {
        var key = $"{orchId}/{memberId}";

        lock (_lock)
        {
            if (_inFlight.ContainsKey(key))
                return;
        }

        var state = PrintSessionState_Store.Read_OrNull(stateFile);

        if (state == null || !File.Exists(state.ChannelFilePath))
            return;

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(state.ChannelFilePath));
        var pending = PrintTurn_Trigger.Select_Pending(role, entries, state.LastHandledEntryIndex);

        if (pending.Count == 0)
            return;

        var tracker = Get_Tracker(key);
        var newest = pending[^1].Index;

        if (tracker.NewestPendingIndex != newest)
        {
            tracker.NewestPendingIndex = newest;
            tracker.NewestPendingSeenAt = nowLocal;
        }

        // Entries still landing ride the same turn: wait until the newest has been quiet for the window.
        if (nowLocal - tracker.NewestPendingSeenAt < configs.CoalesceWindow)
            return;

        // Stalled after MAX_ATTEMPTS: nothing runs until an entry newer than the one it stalled on arrives.
        if (state.FailedAttempts >= MAX_ATTEMPTS && newest <= tracker.StalledAtIndex)
            return;

        if (tracker.LastFailureAt != null && nowLocal - tracker.LastFailureAt.Value < _retryBackoff)
            return;

        Start_Turn(key, stateFile, state, pending, tracker, configs);
    }

    /// <summary>
    /// Starts the turn and registers it as in-flight AS ONE STEP, and detaches it from the mirror
    /// tick's ambient state. Both halves are load-bearing:
    ///
    /// <para>
    /// REGISTERED UNDER THE LOCK, BEFORE the task can finish. Written the other way round — start,
    /// then record — a turn that completes synchronously (the idempotency skip does no I/O, and an
    /// uncontended semaphore completes inline) removes the key BEFORE this inserts it, leaving a
    /// finished task in the map that nothing will ever remove: that session is then skipped by the
    /// guard above for the life of the process. The removal in <c>Execute_Turn_Async</c>'s finally
    /// takes the same lock, so it simply waits for this insert.
    /// </para>
    /// <para>
    /// EXECUTION CONTEXT SUPPRESSED, because <c>ChannelWrite_Lock</c>'s allowance is an
    /// <c>AsyncLocal</c> and <c>Task.Run</c> captures the ambient context. This tick opened one;
    /// a turn inheriting it would, half an hour later, charge its channel appends against an
    /// allowance that ended with that tick — and that file says in as many words that the hazard is
    /// inert only while no detached lambda writes to a channel. This one writes the member's entry,
    /// so it must not inherit. A turn is its own flow: it opens no allowance and pays the plain
    /// per-call budget.
    /// </para>
    /// </summary>
    void Start_Turn(string key, string stateFile, IPrintSessionState state, IReadOnlyList<IChannelEntry> pending, SessionTracker tracker, IRunnerConfigs configs)
    {
        lock (_lock)
        {
            if (_inFlight.ContainsKey(key))
                return;

            using var suppressed = ExecutionContext.SuppressFlow();

            _inFlight[key] = Task.Run(() => Execute_Turn_Async(key, stateFile, state, pending, tracker, configs));
        }
    }

    SessionTracker Get_Tracker(string key)
    {
        lock (_lock)
        {
            if (!_trackers.TryGetValue(key, out var tracker))
            {
                tracker = new SessionTracker();
                _trackers[key] = tracker;
            }

            return tracker;
        }
    }

    SemaphoreSlim Get_OrchestrationSlots(string orchId)
    {
        lock (_lock)
        {
            if (!_orchestrationSlots.TryGetValue(orchId, out var slots))
            {
                slots = new SemaphoreSlim(_slotsPerOrchestration, _slotsPerOrchestration);
                _orchestrationSlots[orchId] = slots;
            }

            return slots;
        }
    }

    // ----- the turn -----

    async Task Execute_Turn_Async(string key, string stateFile, IPrintSessionState state, IReadOnlyList<IChannelEntry> pending, SessionTracker tracker, IRunnerConfigs configs)
    {
        var cancellationToken = _shutdown.Token;
        var orchestrationSlots = Get_OrchestrationSlots(state.OrchId);

        try
        {
            await _globalSlots.WaitAsync(cancellationToken);

            try
            {
                await orchestrationSlots.WaitAsync(cancellationToken);

                try
                {
                    await Run_Turn_Async(stateFile, state, pending, tracker, configs, cancellationToken);
                }
                finally
                {
                    orchestrationSlots.Release();
                }
            }
            finally
            {
                _globalSlots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown: the state was not advanced, so the same entries are pending at the next start.
        }
        catch (Exception ex)
        {
            _log.Log_Error(state.OrchId, $"Print turn for '{state.MemberId}' failed outside the process", ex);
            tracker.LastFailureAt = DateTime.Now;
        }
        finally
        {
            lock (_lock)
                _inFlight.Remove(key);
        }
    }

    async Task Run_Turn_Async(string stateFile, IPrintSessionState state, IReadOnlyList<IChannelEntry> pending, SessionTracker tracker, IRunnerConfigs configs, CancellationToken cancellationToken)
    {
        if (state.FailedAttempts >= MAX_ATTEMPTS)
        {
            // New traffic after a stall: the counter starts over for this turn.
            state = PrintSessionState_Factory.CreateFrom_Existing_AttemptsReset(state);
            tracker.StalledAtIndex = -1;
        }

        var turnNumber = state.NextTurnNumber;
        var requestId = PrintTurn_RequestId.Build(state.OrchId, state.MemberId, turnNumber);

        if (state.ExecutedTurns.Any(turn => turn.RequestId == requestId))
        {
            _log.Log_Warning(state.OrchId, $"Print turn {requestId} was already executed — skipped, not re-run");
            PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_TurnSkipped(state));
            return;
        }

        var roleConfig = configs.Get_ForRole(state.Role);
        var fresh = roleConfig.Resume == ResumeModes.Fresh;
        var hasHistory = state.ExecutedTurns.Count > 0;

        // WHETHER THE ID IS SPENT, not whether a turn has succeeded. `--session-id` with a uuid the
        // CLI has already seen is refused outright ("Error: Session ID <uuid> is already in use.",
        // exit 1, measured on 2.1.261), so a first turn that timed out could otherwise never be
        // retried: attempts 2 and 3 would die on the flag and the session would stall for good.
        // Resuming a transcript whose turn was killed mid-flight works (measured the same day), so
        // the retry resumes — which also keeps whatever the killed attempt had already done.
        var sessionUsed = state.SessionStarted || hasHistory;
        var resumeTranscript = !fresh && sessionUsed;
        var sessionId = fresh && sessionUsed ? Guid.NewGuid().ToString() : state.SessionId;

        var arguments = PrintTurnCommand_Builder.Build_Arguments(state, roleConfig, sessionId, resumeTranscript, null);

        // Claimed BEFORE the process starts, so a bridge that dies mid-turn still knows the id is
        // spent and resumes instead of colliding with itself.
        if (!resumeTranscript)
        {
            state = PrintSessionState_Factory.CreateFrom_Existing_SessionClaimed(state, sessionId);
            PrintSessionState_Store.Write(stateFile, state);
        }

        var alreadyExecuted = tracker.FirstTurnSinceStart && hasHistory
            ? state.ExecutedTurns.Select(turn => turn.TurnNumber).ToList()
            : [];

        var prompt = resumeTranscript ? PrintTurnPrompt_Builder.Build_FollowUp(requestId, pending, alreadyExecuted) : null;
        tracker.FirstTurnSinceStart = false;

        Dictionary<string, string> environment = new()
        {
            ["AIORCH_ROLE"] = SessionRole_Names.Get_EnvWord(state.Role),
            ["AIORCH_ID"] = state.OrchId,
            ["AIORCH_MEMBER"] = state.MemberId,
            [RUNNER_ENV_VAR] = SessionRunner_Names.PRINT,
        };

        var attempt = state.FailedAttempts + 1;
        _log.Log_Info(state.OrchId, $"Print turn {requestId} started — attempt {attempt}, entries [{pending[0].Index}]–[{pending[^1].Index}], {(resumeTranscript ? "resume" : fresh ? "fresh session" : "first turn")} {sessionId}");

        var result = await _turnRunner.Run_Async(arguments, prompt, state.WorkingDirectory, environment, configs.TurnTimeout, cancellationToken);

        // The runner rethrows on shutdown rather than reporting a timeout, so nothing below runs
        // for a turn the app cancelled: no failure counted, no turn_ended entry, no attempt spent.
        cancellationToken.ThrowIfCancellationRequested();

        var outcome = TurnOutcomes.Describe(result);

        if (TurnOutcomes.Is_Success(result))
        {
            var (subject, body) = PrintTurnEntry_Splitter.Split(result.ResultText);

            if (!Append_SessionEntry_WithRetry(state, subject, body))
            {
                _log.Log_Error(state.OrchId, $"Print turn {requestId} completed but its entry could not be appended — the channel stayed locked; the turn will be retried", null);
                Record_Failure(stateFile, state, pending, tracker, result, requestId, attempt, "entry not appended (channel locked)");
                return;
            }

            Append_TurnEnded(state, requestId, attempt, pending, result, outcome, null);

            var executed = ExecutedTurn_Factory.Create(turnNumber, requestId, pending[0].Index, pending[^1].Index, DateTime.UtcNow, outcome, result.TotalCostUsd);
            PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(state, executed, result.SessionId ?? sessionId));
            tracker.LastFailureAt = null;

            _log.Log_Info(state.OrchId, $"Print turn {requestId} ended — {outcome}, {Describe_Cost(result)}, {result.Elapsed.TotalSeconds:F1} s wall");
            return;
        }

        Record_Failure(stateFile, state, pending, tracker, result, requestId, attempt, null);
    }

    void Record_Failure(string stateFile, IPrintSessionState state, IReadOnlyList<IChannelEntry> pending, SessionTracker tracker, ITurnResult result, string requestId, int attempt, string? note)
    {
        var outcome = note == null ? TurnOutcomes.Describe(result) : TurnOutcomes.ERROR;

        Append_TurnEnded(state, requestId, attempt, pending, result, outcome, note);

        var failed = PrintSessionState_Factory.CreateFrom_Existing_AttemptFailed(state);
        PrintSessionState_Store.Write(stateFile, failed);
        tracker.LastFailureAt = DateTime.Now;

        if (failed.FailedAttempts >= MAX_ATTEMPTS)
        {
            tracker.StalledAtIndex = pending[^1].Index;

            var alert = $"Print turn {requestId} failed {failed.FailedAttempts} times ({outcome}{(note == null ? string.Empty : $": {note}")}) — not retried until new traffic arrives in the channel";
            _log.Log_Error(state.OrchId, alert, null);

            ChannelAppender.Append_AppEntry(
                state.ChannelFilePath,
                AppEntryAudiences.Agent,
                $"{TURN_STALLED_SUBJECT} {state.MemberId} turn {state.NextTurnNumber} — {outcome} × {failed.FailedAttempts}",
                $"{alert}\n\nrequest_id: {requestId}\nlast exit_code: {result.ExitCode}\napi_error_status: {Describe_ApiErrorStatus(result)}\nstderr (tail): {Tail(result.RawStderr, 600)}",
                DateTime.Now);

            return;
        }

        _log.Log_Warning(state.OrchId, $"Print turn {requestId} attempt {attempt} {outcome}{(note == null ? string.Empty : $" ({note})")} — retry after {_retryBackoff.TotalSeconds:F0} s; {Tail(result.RawStderr, 200)}");
    }

    bool Append_SessionEntry_WithRetry(IPrintSessionState state, string subject, string body)
    {
        var author = SessionRole_Names.Get_Author(state.Role);

        for (var attempt = 0; attempt < ENTRY_APPEND_ATTEMPTS; attempt++)
        {
            if (ChannelAppender.Append_SessionEntry(state.ChannelFilePath, author, subject, body, DateTime.Now))
                return true;

            Thread.Sleep(ENTRY_APPEND_RETRY_MILLISECONDS);
        }

        return false;
    }

    /// <summary>
    /// The record the supervisor reads (agent audience — the owner cannot act on it, decision 15):
    /// outcome, cost, duration, api_error_status. A member that "forgets" to report does not
    /// vanish; a turn that failed is visible where the brief was written.
    /// </summary>
    void Append_TurnEnded(IPrintSessionState state, string requestId, int attempt, IReadOnlyList<IChannelEntry> pending, ITurnResult result, string outcome, string? note)
    {
        var body =
            $"request_id: {requestId}\n" +
            $"outcome: {outcome}{(note == null ? string.Empty : $" — {note}")}\n" +
            $"attempt: {attempt}\n" +
            $"entries: [{pending[0].Index}]–[{pending[^1].Index}]\n" +
            $"cost_usd: {(result.TotalCostUsd?.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}\n" +
            $"duration_ms: {(result.DurationMs?.ToString() ?? "unknown")} (api {(result.DurationApiMs?.ToString() ?? "unknown")}), wall {result.Elapsed.TotalSeconds:F1} s\n" +
            $"api_error_status: {Describe_ApiErrorStatus(result)}\n" +
            $"exit_code: {result.ExitCode}{(result.TimedOut ? " (killed on timeout)" : string.Empty)}\n" +
            $"session_id: {result.SessionId ?? state.SessionId}";

        if (!ChannelAppender.Append_AppEntry(state.ChannelFilePath, AppEntryAudiences.Agent, $"{TURN_ENDED_SUBJECT} {state.MemberId} turn {requestId[(requestId.LastIndexOf('/') + 1)..]} — {outcome}", body, DateTime.Now))
            _log.Log_Warning(state.OrchId, $"turn_ended for {requestId} could not be appended (channel locked) — the turn itself is recorded in the state file");
    }

    static string Describe_ApiErrorStatus(ITurnResult result)
    {
        return result.ApiErrorStatus?.ToString() ?? "none";
    }

    static string Describe_Cost(ITurnResult result)
    {
        return result.TotalCostUsd == null ? "cost unknown" : $"{result.TotalCostUsd.Value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} USD";
    }

    static string Tail(string text, int maxLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : "…" + trimmed[^maxLength..];
    }
}
