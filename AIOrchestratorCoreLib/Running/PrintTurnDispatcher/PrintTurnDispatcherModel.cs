using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnExecutor;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Running.PrintTurnDispatcher;

/// <summary>
/// THE QUEUE IS THE CHANNELS. A bridge-driven session has no process to hold a queue in, so its
/// pending work is defined by the files: every inbound entry of every channel it is woken by whose
/// identity is not yet in that channel's cursor is pending, and one turn takes all of them. That gives
/// FIFO per session, coalescing (entries that land within the window ride together, ACROSS channels),
/// and restart recovery (a bridge that died mid-turn finds the same entries pending) without a queue
/// object anywhere.
///
/// Per tick, per registered session: resolve its channels, read them, select the pending entries, wait
/// out the coalesce window, then start ONE turn on a background task — never more than one per session,
/// never more than the global and per-orchestration slots allow. The task runs the process, writes the
/// session's entries from the result — each part into the channel it was addressed to — records a
/// <c>turn_ended</c> entry, and persists the cursors. A failed attempt (timeout, non-zero exit,
/// <c>is_error</c>) is counted and retried after a backoff under the SAME request id; at
/// <see cref="MAX_ATTEMPTS"/> the session stalls — an alert entry, no further attempts — until the
/// pending set changes.
///
/// Idempotency: a request id already in the executed list is skipped without running. The prompt
/// of the first resumed turn after a bridge start lists the executed turns, so a transcript that
/// remembers answering does not answer twice (decision 8).
///
/// The slot limits are read ONCE, at construction — a semaphore cannot be resized — so changing
/// them in config.json takes effect at the next app start. Everything else (runner, resume mode,
/// timeout, coalesce window) is read on every tick.
///
/// <para>
/// HOW a turn reaches the model is NOT this class's business — that is
/// <see cref="ITurnExecutor"/>, picked per role from the configured runner. Everything above stays
/// identical whether the turn is one <c>claude -p</c> process or a line on a living one's stdin,
/// and that is the point: two transports must never grow two answers to "has this turn already
/// run".
/// </para>
/// <para>
/// NOR IS "WHICH CHANNELS" — that is <see cref="TurnSources_Resolver"/>, resolved fresh every tick so a
/// member added mid-life becomes a source of its supervisor's next turn with nothing re-registered.
/// </para>
/// </summary>
internal sealed class PrintTurnDispatcherModel : IPrintTurnDispatcher
{
    const int MAX_ATTEMPTS = PrintTurn_Words.MAX_ATTEMPTS;
    const string RUNNER_ENV_VAR = PrintTurn_Words.RUNNER_ENV_VAR;
    const string TURN_ENDED_SUBJECT = PrintTurn_Words.TURN_ENDED_SUBJECT;
    const string TURN_STALLED_SUBJECT = PrintTurn_Words.TURN_STALLED_SUBJECT;
    const string MISADDRESSED_SUBJECT = PrintTurn_Words.MISADDRESSED_SUBJECT;
    const int ENTRY_APPEND_ATTEMPTS = 3;
    const int ENTRY_APPEND_RETRY_MILLISECONDS = 300;
    static readonly TimeSpan STOP_GRACE = TimeSpan.FromSeconds(15);

    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IReadOnlyDictionary<SessionRunners, ITurnExecutor> _executors;
    readonly IOrchestrationLog _log;
    readonly TimeSpan _retryBackoff;

    readonly Lock _lock = new();
    readonly CancellationTokenSource _shutdown = new();
    readonly Dictionary<string, Task> _inFlight = [];
    readonly Dictionary<string, SessionTracker> _trackers = [];
    readonly Dictionary<string, SemaphoreSlim> _orchestrationSlots = [];
    readonly HashSet<string> _warnedStaleRegistrations = [];
    readonly HashSet<string> _warnedArchiveGaps = [];
    readonly SemaphoreSlim _globalSlots;
    readonly int _slotsPerOrchestration;

    public PrintTurnDispatcherModel(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestratorConfigProvider configProvider,
        IReadOnlyList<ITurnExecutor> executors,
        IOrchestrationLog log,
        TimeSpan retryBackoff)
    {
        _paths = paths;
        _store = store;
        _configProvider = configProvider;
        _executors = executors.ToDictionary(executor => executor.Kind);
        _log = log;
        _retryBackoff = retryBackoff;

        var limits = configProvider.Get_Current().Runners;
        _globalSlots = new SemaphoreSlim(limits.MaxConcurrentTurns, limits.MaxConcurrentTurns);
        _slotsPerOrchestration = limits.MaxConcurrentTurnsPerOrchestration;
    }

    /// <summary>What the dispatcher remembers about a session BETWEEN ticks and only until restart — nothing here is truth, the state file is.</summary>
    sealed class SessionTracker
    {
        /// <summary>
        /// The pending set as one string, so "has anything changed since I last looked" is one
        /// comparison across every channel at once. It is built from entry IDENTITIES and never from
        /// indices or counts — the same reason the cursor is (see <see cref="ChannelEntry_Digest"/>).
        /// </summary>
        public string PendingSignature = string.Empty;

        public DateTime PendingSeenAt;
        public DateTime? LastFailureAt;

        /// <summary>The pending set a stall happened on; null while nothing is stalled.</summary>
        public string? StalledOnSignature;

        /// <summary>True until this dispatcher instance has run the session once — the "resumed after a restart" signal for the prompt.</summary>
        public bool FirstTurnSinceStart = true;
    }

    /// <summary>One source, as it stands this tick: what it holds, how far it has been delivered, what is pending.</summary>
    readonly record struct SourceRead(ITurnSource Source, IReadOnlyList<IChannelEntry> Entries, ITurnCursor Cursor, IReadOnlyList<IChannelEntry> Pending);

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
            // A STALE REGISTRATION IS NOT A MANDATE. The state file says a session WAS bridge-driven;
            // config.json says whether it still is. Flipped back to terminal, the launcher spawns a
            // window for this member — and without this check the dispatcher would keep firing
            // turns into the same channel, two sessions answering one brief. The file is
            // deleted by the launcher at the next spawn; until then, this is the gate.
            if (!Is_StillBridgeDriven(configs, registered.Role))
            {
                if (_warnedStaleRegistrations.Add($"{registered.OrchId}/{registered.MemberId}"))
                    _log.Log_Warning(registered.OrchId, $"'{registered.MemberId}' has a bridge-driven registration but role '{SessionRole_Names.Get_ConfigKey(registered.Role)}' is now configured runner: {SessionRunner_Names.Get_Word(configs.Get_ForRole(registered.Role).Runner)} — no turns are dispatched for it (the registration is cleared at its next spawn)");

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

        try
        {
            if (pending.Length > 0)
                await Task.WhenAll(pending).WaitAsync(STOP_GRACE);
        }
        catch
        {
            // Turns end by cancellation or are abandoned after the grace — the app is exiting either way.
        }

        // AFTER the turns, never before: a resident process killed while its turn is still being
        // awaited turns an orderly shutdown into a structural failure and a fallback nobody asked
        // for. The app is exiting, so the ladder's memory would not survive to be wrong — but the
        // channel entry it writes on the way out would.
        foreach (var executor in _executors.Values)
        {
            try
            {
                await executor.Stop_Async();
            }
            catch (Exception ex)
            {
                _log.Log_Error(string.Empty, $"The {SessionRunner_Names.Get_Word(executor.Kind)} executor did not stop cleanly", ex);
            }
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

            // THE SUPERVISOR IS NOT IN THE MEMBER ROSTER — it is the orchestration itself — so a
            // loop over Members alone finds every implementer and never the one role the stream
            // runner exists for. Its registration lives under a role-prefixed name beside
            // session.json (PrintSessionState_Store knows where); found here or found nowhere.
            var supervisorFile = PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Supervisor, session.OrchId, SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

            if (File.Exists(supervisorFile))
                found.Add((supervisorFile, SessionRoles.Supervisor, session.OrchId, SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID));

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

    /// <summary>
    /// Whether this registration is still one this dispatcher owns — the configured runner must be
    /// bridge-driven, must support the role, AND must be wired here. The third question is what
    /// stops a role configured for a transport nothing implements from being dispatched into a
    /// missing executor.
    /// </summary>
    bool Is_StillBridgeDriven(IRunnerConfigs configs, SessionRoles role)
    {
        var runner = configs.Get_ForRole(role).Runner;

        return Runner_Support.Is_BridgeDriven(runner) && Runner_Support.Supports(runner, role) && _executors.ContainsKey(runner);
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

        if (state == null)
            return;

        var sources = TurnSources_Resolver.Resolve(_paths, _store, role, orchId, memberId);
        var reads = Read_Sources(stateFile, ref state, role, sources);

        if (reads.Count == 0)
            return;

        var ordered = PendingTraffic_Orderer.Order([.. reads.Select(read => (read.Source, read.Pending))]);

        if (ordered.Count == 0)
            return;

        var tracker = Get_Tracker(key);
        var signature = Describe_PendingSignature(ordered);

        if (tracker.PendingSignature != signature)
        {
            tracker.PendingSignature = signature;
            tracker.PendingSeenAt = nowLocal;
        }

        // Entries still landing ride the same turn, whichever channel they land on: wait until the set
        // has been unchanged for the window.
        if (nowLocal - tracker.PendingSeenAt < configs.CoalesceWindow)
            return;

        // Stalled after MAX_ATTEMPTS: nothing runs until the pending set changes — which is what "new
        // traffic arrives" means once a cursor is a set of identities rather than a number.
        if (state.FailedAttempts >= MAX_ATTEMPTS && signature == tracker.StalledOnSignature)
            return;

        if (tracker.LastFailureAt != null && nowLocal - tracker.LastFailureAt.Value < _retryBackoff)
            return;

        Start_Turn(key, stateFile, state, ordered, sources, tracker, configs);
    }

    /// <summary>
    /// Reads every source, baselining the ones this session has never seen and dropping the cursors of
    /// sources it no longer has. A change to the cursor set is PERSISTED HERE, before any turn: a
    /// baseline taken on a tick that starts no turn must survive a restart, or the same history is
    /// absorbed and announced again on every tick for ever.
    /// </summary>
    IReadOnlyList<SourceRead> Read_Sources(string stateFile, ref IPrintSessionState state, SessionRoles role, IReadOnlyList<ITurnSource> sources)
    {
        var known = state.Cursors.ToDictionary(cursor => cursor.SourceKey, StringComparer.Ordinal);

        List<SourceRead> reads = [];
        List<ITurnCursor> cursors = [];
        var changed = false;

        foreach (var source in sources)
        {
            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(source.ChannelFilePath));

            if (!known.TryGetValue(source.Key, out var cursor))
            {
                // A SOURCE THAT APPEARS WHILE THE SESSION IS RUNNING STARTS EMPTY, so everything in it
                // is traffic and none of it is absorbed. This is the opposite of the baseline taken at
                // REGISTRATION, and deliberately: a channel that turns up now belongs to a member that
                // was created now, and its very first entry — the member's boot greeting, landing
                // between its spawn and the next tick — is exactly what a supervisor is here to answer.
                // Absorbing it would swallow the one entry this rule can ever see.
                cursor = TurnCursor_Factory.Create_Empty(source);
                changed = true;

                _log.Log_Info(state.OrchId, $"'{state.MemberId}' is now also woken by channel '{source.Key}'");
            }

            cursors.Add(cursor);
            Warn_IfEntriesWereArchivedUndelivered(state, source, cursor, entries);

            reads.Add(new SourceRead(source, entries, cursor, PrintTurn_Trigger.Select_Pending(role, entries, cursor)));
        }

        // A cursor whose source is gone (a closed member) is dropped with it — kept, it would be one
        // more key nothing matches and one more channel path nobody reads.
        if (changed || cursors.Count != state.Cursors.Count)
        {
            state = PrintSessionState_Factory.CreateFrom_Existing_Cursors(state, cursors);
            PrintSessionState_Store.Write(stateFile, state);
        }

        return reads;
    }

    /// <summary>
    /// THE ONE HOLE THIS CURSOR HAS, MADE AUDIBLE. Only the LIVE file is read, so entries compaction
    /// archived before the bridge ever handed them over are gone from its view — the same one-way hole
    /// <see cref="Bridge.BridgeState_Store"/> describes for the mirror, reachable here only if a session
    /// went undelivered for more than <see cref="Channel_Compactor.COMPACT_ABOVE_ENTRIES"/> entries.
    /// Reading the archive every tick is exactly what compaction exists to avoid, so this detects the
    /// gap instead of closing it: everything still live sitting above the highest index ever delivered
    /// means entries left in between. Said once per channel per app life.
    /// </summary>
    void Warn_IfEntriesWereArchivedUndelivered(IPrintSessionState state, ITurnSource source, ITurnCursor cursor, IReadOnlyList<IChannelEntry> entries)
    {
        if (cursor.Delivered.Count == 0 || entries.Count == 0)
            return;

        var lowestLive = entries.Min(entry => entry.Index);

        if (lowestLive <= cursor.HighWaterIndex + 1)
            return;

        if (!_warnedArchiveGaps.Add($"{state.OrchId}/{state.MemberId}/{source.Key}"))
            return;

        _log.Log_Warning(state.OrchId, $"'{state.MemberId}': channel '{source.Key}' now starts at entry [{lowestLive}] but nothing above [{cursor.HighWaterIndex}] was ever delivered to this session — entries [{cursor.HighWaterIndex + 1}]–[{lowestLive - 1}] were archived without starting a turn and are only in '{Path.GetFileName(Channel_Compactor.Build_ArchiveFilePath(source.ChannelFilePath))}'");
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
    void Start_Turn(string key, string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources, SessionTracker tracker, IRunnerConfigs configs)
    {
        lock (_lock)
        {
            if (_inFlight.ContainsKey(key))
                return;

            using var suppressed = ExecutionContext.SuppressFlow();

            _inFlight[key] = Task.Run(() => Execute_Turn_Async(key, stateFile, state, pending, sources, tracker, configs));
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

    async Task Execute_Turn_Async(string key, string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources, SessionTracker tracker, IRunnerConfigs configs)
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
                    await Run_Turn_Async(stateFile, state, pending, sources, tracker, configs, cancellationToken);
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
            // Shutdown: the cursors were not advanced, so the same entries are pending at the next start.
        }
        catch (Exception ex)
        {
            _log.Log_Error(state.OrchId, $"Turn for '{state.MemberId}' failed outside the process", ex);
            tracker.LastFailureAt = DateTime.Now;
        }
        finally
        {
            lock (_lock)
                _inFlight.Remove(key);
        }
    }

    async Task Run_Turn_Async(string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources, SessionTracker tracker, IRunnerConfigs configs, CancellationToken cancellationToken)
    {
        if (state.FailedAttempts >= MAX_ATTEMPTS)
        {
            // New traffic after a stall: the counter starts over for this turn.
            state = PrintSessionState_Factory.CreateFrom_Existing_AttemptsReset(state);
            tracker.StalledOnSignature = null;
        }

        var turnNumber = state.NextTurnNumber;
        var requestId = PrintTurn_RequestId.Build(state.OrchId, state.MemberId, turnNumber);

        if (state.ExecutedTurns.Any(turn => turn.RequestId == requestId))
        {
            _log.Log_Warning(state.OrchId, $"Turn {requestId} was already executed — skipped, not re-run");
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

        tracker.FirstTurnSinceStart = false;

        var executor = _executors[roleConfig.Runner];

        Dictionary<string, string> environment = new()
        {
            ["AIORCH_ROLE"] = SessionRole_Names.Get_EnvWord(state.Role),
            ["AIORCH_ID"] = state.OrchId,
            ["AIORCH_MEMBER"] = state.MemberId,
            [RUNNER_ENV_VAR] = SessionRunner_Names.Get_Word(executor.Kind),

            // THE ROOT TRAVELS WITH THE SESSION. The stage-2 live round lost its first two turns to
            // exactly this: under --root the member composed its channel path from the default home,
            // did not find the file, CREATED one in the real home and sat there until the timeout.
            // The hosts export it into their own environment and Process.Start copies the parent's,
            // so this is belt AND braces — but a dispatcher wired directly (a test, a future host)
            // has no parent that did, and the failure it produces is silent.
            [Composition.HostOptions.HostOptions_Factory.SUPERVISION_ROOT_ENV] = _paths.Root,
        };

        var attempt = state.FailedAttempts + 1;
        _log.Log_Info(state.OrchId, $"{SessionRunner_Names.Get_Word(executor.Kind)} turn {requestId} started — attempt {attempt}, {Describe_Traffic(pending)}, {(resumeTranscript ? "resume" : fresh ? "fresh session" : "first turn")} {sessionId}");

        var result = await executor.Execute_Async(state, roleConfig, sessionId, resumeTranscript, requestId, pending, sources, alreadyExecuted, environment, configs.TurnTimeout, cancellationToken);

        // The runner rethrows on shutdown rather than reporting a timeout, so nothing below runs
        // for a turn the app cancelled: no failure counted, no turn_ended entry, no attempt spent.
        cancellationToken.ThrowIfCancellationRequested();

        var outcome = TurnOutcomes.Describe(result);

        if (TurnOutcomes.Is_Success(result))
        {
            if (!Write_Reply(state, sources, result.ResultText))
            {
                _log.Log_Error(state.OrchId, $"Turn {requestId} completed but its entry could not be appended — the channel stayed locked; the turn will be retried", null);
                Record_Failure(stateFile, state, pending, tracker, result, requestId, attempt, executor, "entry not appended (channel locked)");
                return;
            }

            Append_TurnEnded(state, requestId, attempt, pending, result, outcome, null);

            var executed = ExecutedTurn_Factory.Create(turnNumber, requestId, pending[0].Entry.Index, pending[^1].Entry.Index, DateTime.UtcNow, outcome, result.TotalCostUsd);

            PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(state, executed, result.SessionId ?? sessionId, Advance_Cursors(state, sources, pending)));
            tracker.LastFailureAt = null;

            _log.Log_Info(state.OrchId, $"Turn {requestId} ended — {outcome}, {Describe_Cost(result)}, {result.Elapsed.TotalSeconds:F1} s wall");
            return;
        }

        Record_Failure(stateFile, state, pending, tracker, result, requestId, attempt, executor, null);
    }

    /// <summary>
    /// EVERY DELIVERED ENTRY IS RECORDED, whichever channel it came from and whether or not the session
    /// answered that channel. Delivered means handed over; a supervisor that reads a spoke and says
    /// nothing has still read it, and re-handing it the same entry next tick would be a loop.
    ///
    /// <para>
    /// The live file is re-read here rather than reused from the tick's read, because the turn has since
    /// appended to it: pruning against a stale copy would keep identities compaction has moved out and,
    /// worse, would be a second opinion about what the file contains.
    /// </para>
    /// </summary>
    IReadOnlyList<ITurnCursor> Advance_Cursors(IPrintSessionState state, IReadOnlyList<ITurnSource> sources, IReadOnlyList<PendingEntry> pending)
    {
        var byKey = state.Cursors.ToDictionary(cursor => cursor.SourceKey, StringComparer.Ordinal);

        List<ITurnCursor> advanced = [];

        foreach (var source in sources)
        {
            if (!byKey.TryGetValue(source.Key, out var cursor))
                continue;

            var delivered = pending.Where(item => item.Source.Key == source.Key).Select(item => item.Entry).ToList();
            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(source.ChannelFilePath));

            advanced.Add(TurnCursor_Factory.CreateFrom_Delivered(cursor, state.Role, entries, delivered));
        }

        return advanced;
    }

    // ----- the reply -----

    /// <summary>
    /// The session's final message, filed into the channels it addressed. Returns whether every part
    /// landed — a false is a locked channel, which the caller treats as a failed turn so the same
    /// entries are still pending on the retry.
    ///
    /// <para>
    /// A SESSION WITH ONE CHANNEL IS NOT SPLIT. It was never told the format, and a report that happened
    /// to contain a line beginning "to:" would be cut in half by a rule written for somebody else.
    /// </para>
    /// <para>
    /// A PARTIAL WRITE COSTS A DUPLICATE, AND THAT IS THE CHOICE. If one of several blocks cannot be
    /// appended — its channel held by another writer for the whole budget — the turn is reported failed
    /// and retried, so the blocks that DID land are written a second time. The alternative is to accept
    /// the partial write, which loses the block that failed with nobody told. A duplicated entry is
    /// visible in a file a human reads; a missing verdict is not, and the session that was waiting for it
    /// waits for ever. Same rule as everywhere else here: fail towards the visible side.
    /// </para>
    /// </summary>
    bool Write_Reply(IPrintSessionState state, IReadOnlyList<ITurnSource> sources, string resultText)
    {
        var author = SessionRole_Names.Get_Author(state.Role);
        var ownChannel = state.ChannelFilePath;

        if (sources.Count <= 1)
        {
            var (soleSubject, soleBody) = PrintTurnEntry_Splitter.Split(resultText);
            return Append_SessionEntry_WithRetry(ownChannel, author, soleSubject, soleBody);
        }

        var byKey = sources.ToDictionary(source => source.Key, StringComparer.OrdinalIgnoreCase);
        var blocks = TurnReply_Splitter.Split(resultText);

        if (blocks.Count == 0)
        {
            var (emptySubject, emptyBody) = PrintTurnEntry_Splitter.Split(resultText);
            return Append_SessionEntry_WithRetry(ownChannel, author, emptySubject, emptyBody);
        }

        List<string> misaddressed = [];
        var allLanded = true;

        foreach (var block in blocks)
        {
            var target = ownChannel;

            if (block.SourceKey != null)
            {
                if (byKey.TryGetValue(block.SourceKey, out var source))
                    target = source.ChannelFilePath;
                else
                    misaddressed.Add(block.SourceKey);
            }

            var (subject, body) = PrintTurnEntry_Splitter.Split(block.Text);

            if (!Append_SessionEntry_WithRetry(target, author, subject, body))
                allLanded = false;
        }

        // NEVER SILENTLY REROUTED. The block is in the owner channel either way; this says it was meant
        // for somewhere else, so a supervisor addressing a member that has since closed — or mistyping
        // its id — reads about it instead of wondering why the member never answered.
        if (misaddressed.Count > 0)
        {
            ChannelAppender.Append_AppEntry(
                ownChannel,
                AppEntryAudiences.Agent,
                $"{MISADDRESSED_SUBJECT} {state.MemberId} — {string.Join(", ", misaddressed.Distinct())}",
                $"Part(s) of the last turn were addressed to {string.Join(", ", misaddressed.Distinct().Select(key => $"'{key}'"))}, which {(misaddressed.Distinct().Count() == 1 ? "is not a channel" : "are not channels")} this session is woken by. They were written HERE instead, in order.\n\nAddressable this turn: {string.Join(", ", sources.Select(source => source.Key))}",
                DateTime.Now);
        }

        return allLanded;
    }

    void Record_Failure(string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, SessionTracker tracker, ITurnResult result, string requestId, int attempt, ITurnExecutor executor, string? note)
    {
        var outcome = note == null ? TurnOutcomes.Describe(result) : TurnOutcomes.ERROR;

        Append_TurnEnded(state, requestId, attempt, pending, result, outcome, note);

        // THE ATTEMPTS BELONGED TO THE TRANSPORT THAT BROKE. A session that has just walked down the
        // fallback ladder starts its counter again, or the two counters coincide — three deaths and
        // three attempts — and it stalls on the rung it stepped off having never tried the one
        // below. Reported as attempt 1 of the new runner, because that is what it is.
        if (executor.Consume_RunnerChange(state.OrchId, state.MemberId))
        {
            _log.Log_Warning(state.OrchId, $"'{state.MemberId}' changed runner after {attempt} failed attempt(s) — the attempt counter starts again on the new one");
            state = PrintSessionState_Factory.CreateFrom_Existing_AttemptsReset(state);
            tracker.StalledOnSignature = null;
        }

        var failed = PrintSessionState_Factory.CreateFrom_Existing_AttemptFailed(state);
        PrintSessionState_Store.Write(stateFile, failed);
        tracker.LastFailureAt = DateTime.Now;

        if (failed.FailedAttempts >= MAX_ATTEMPTS)
        {
            tracker.StalledOnSignature = Describe_PendingSignature(pending);

            var alert = $"Turn {requestId} failed {failed.FailedAttempts} times ({outcome}{(note == null ? string.Empty : $": {note}")}) — not retried until new traffic arrives in the channels";
            _log.Log_Error(state.OrchId, alert, null);

            ChannelAppender.Append_AppEntry(
                state.ChannelFilePath,
                AppEntryAudiences.Agent,
                $"{TURN_STALLED_SUBJECT} {state.MemberId} turn {state.NextTurnNumber} — {outcome} × {failed.FailedAttempts}",
                $"{alert}\n\nrequest_id: {requestId}\n{Describe_Traffic(pending)}\nlast exit_code: {result.ExitCode}\napi_error_status: {Describe_ApiErrorStatus(result)}\nstderr (tail): {Tail(result.RawStderr, 600)}",
                DateTime.Now);

            return;
        }

        _log.Log_Warning(state.OrchId, $"Turn {requestId} attempt {attempt} {outcome}{(note == null ? string.Empty : $" ({note})")} — retry after {_retryBackoff.TotalSeconds:F0} s; {Tail(result.RawStderr, 200)}");
    }

    bool Append_SessionEntry_WithRetry(string channelFilePath, ChannelAuthors author, string subject, string body)
    {
        for (var attempt = 0; attempt < ENTRY_APPEND_ATTEMPTS; attempt++)
        {
            if (ChannelAppender.Append_SessionEntry(channelFilePath, author, subject, body, DateTime.Now))
                return true;

            Thread.Sleep(ENTRY_APPEND_RETRY_MILLISECONDS);
        }

        return false;
    }

    /// <summary>
    /// The record the supervisor reads (agent audience — the owner cannot act on it, decision 15):
    /// outcome, cost, duration, api_error_status. A member that "forgets" to report does not
    /// vanish; a turn that failed is visible where the brief was written. It goes in the session's OWN
    /// channel whichever channels the turn read, so one turn leaves exactly one record.
    /// </summary>
    void Append_TurnEnded(IPrintSessionState state, string requestId, int attempt, IReadOnlyList<PendingEntry> pending, ITurnResult result, string outcome, string? note)
    {
        var body =
            $"request_id: {requestId}\n" +
            $"outcome: {outcome}{(note == null ? string.Empty : $" — {note}")}\n" +
            $"attempt: {attempt}\n" +
            $"{Describe_Traffic(pending)}\n" +
            $"cost_usd: {(result.TotalCostUsd?.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}\n" +
            $"duration_ms: {(result.DurationMs?.ToString() ?? "unknown")} (api {(result.DurationApiMs?.ToString() ?? "unknown")}), wall {result.Elapsed.TotalSeconds:F1} s\n" +
            $"api_error_status: {Describe_ApiErrorStatus(result)}\n" +
            $"exit_code: {result.ExitCode}{(result.TimedOut ? " (killed on timeout)" : string.Empty)}\n" +
            $"session_id: {result.SessionId ?? state.SessionId}";

        if (!ChannelAppender.Append_AppEntry(state.ChannelFilePath, AppEntryAudiences.Agent, $"{TURN_ENDED_SUBJECT} {state.MemberId} turn {requestId[(requestId.LastIndexOf('/') + 1)..]} — {outcome}", body, DateTime.Now))
            _log.Log_Warning(state.OrchId, $"turn_ended for {requestId} could not be appended (channel locked) — the turn itself is recorded in the state file");
    }

    /// <summary>
    /// What the turn was handed, per channel: <c>entries: owner [4]–[5], imp-1 [12]</c>. The indices are
    /// there to be read by a human next to the file, not to be compared with anything — the cursor stopped
    /// depending on them for the reasons <see cref="ChannelEntry_Digest"/> gives.
    /// </summary>
    static string Describe_Traffic(IReadOnlyList<PendingEntry> pending)
    {
        List<string> parts = [];

        foreach (var group in pending.GroupBy(item => item.Source.Key, StringComparer.Ordinal))
        {
            var indices = group.Select(item => item.Entry.Index).ToList();

            parts.Add(indices.Count == 1 ? $"{group.Key} [{indices[0]}]" : $"{group.Key} [{indices[0]}]–[{indices[^1]}]");
        }

        return $"entries: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// The pending set as one comparable string. Identities, in the order the turn would read them, so
    /// any change — a new entry anywhere, one channel gaining traffic while another is quiet — produces a
    /// different signature and restarts the coalesce window.
    /// </summary>
    static string Describe_PendingSignature(IReadOnlyList<PendingEntry> pending)
    {
        return string.Join('|', pending.Select(item => $"{item.Source.Key}:{ChannelEntry_Digest.Compute(item.Entry)}"));
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
