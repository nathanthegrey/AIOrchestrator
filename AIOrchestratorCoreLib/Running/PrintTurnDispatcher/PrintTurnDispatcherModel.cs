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
    /// <summary>
    /// How long cancelled turns get to observe their cancellation and unwind, AFTER the drain. Not
    /// the drain itself: that is <see cref="Get_DrainGrace"/>, the configured turn timeout plus a
    /// minute, because a turn that is still legitimately running is worth exactly what it was worth
    /// before somebody asked the app to stop.
    /// </summary>
    static readonly TimeSpan CANCEL_GRACE = TimeSpan.FromSeconds(15);
    static readonly TimeSpan DRAIN_MARGIN = TimeSpan.FromMinutes(1);
    static readonly TimeSpan DRAIN_GRACE_FALLBACK = TimeSpan.FromMinutes(31);

    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IReadOnlyDictionary<SessionRunners, ITurnExecutor> _executors;
    readonly IOrchestrationLog _log;
    readonly TimeSpan _retryBackoff;

    readonly Lock _lock = new();
    readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// "No new turns" — the first half of stopping. Measured 2026-09-06→08 on the VPS: 21 daemon
    /// starts in 44 hours (deploys), and 17 in-flight turns, 112 M tokens, ended within four minutes
    /// of a `Daemon stopping` line — killed mid-work, their entries re-run from scratch at the next
    /// start. The turns ran under <see cref="_shutdown"/> alone, so Stop_Async cancelled them in the
    /// same instant it stopped admitting new ones. The two are now two signals: this one closes the
    /// door, <see cref="_shutdown"/> is pulled only when the drain grace has run out.
    /// </summary>
    readonly CancellationTokenSource _draining = new();
    readonly Dictionary<string, Task> _inFlight = [];
    readonly Dictionary<string, SessionTracker> _trackers = [];
    readonly Dictionary<string, SemaphoreSlim> _orchestrationSlots = [];
    readonly HashSet<string> _warnedStaleRegistrations = [];
    readonly HashSet<string> _warnedArchiveGaps = [];
    readonly HashSet<string> _warnedBrokenSessions = [];

    /// <summary>
    /// ONE comparer for source keys, everywhere. A session addresses a channel by a word it typed, so the
    /// lookup has to forgive case; the cursor set, the roster match and the factory's uniqueness check
    /// must then forgive it too, or two ids differing only in case pass validation and throw inside the
    /// turn — where the failure is caught, not counted, and retried for ever.
    /// </summary>
    static readonly StringComparer SOURCE_KEYS = StringComparer.OrdinalIgnoreCase;
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

    /// <summary>
    /// One source and what of it is waiting, as of this tick. It carries the PENDING ENTRIES AND NOTHING
    /// ELSE on purpose: an earlier shape also held the file's whole contents and the cursor it was read
    /// against, and neither had a reader — a snapshot nobody consumes is how the next person reasons
    /// from a stale copy of a file that has since been appended to. <see cref="Advance_Cursors"/> re-reads
    /// deliberately, and this leaves it nothing to re-read from.
    /// </summary>
    readonly record struct SourceRead(ITurnSource Source, IReadOnlyList<IChannelEntry> Pending);

    public int InFlightCount
    {
        get
        {
            lock (_lock)
                return _inFlight.Count;
        }
    }

    public bool Is_TurnInFlight(string orchId, string memberId)
    {
        // THE SAME KEY Consider_Session builds, and deliberately not a second way of spelling it: two
        // constructions of one key are two that can drift, and this one decides whether a working
        // member is described as idle.
        var key = $"{orchId}/{memberId}";

        lock (_lock)
            return _inFlight.ContainsKey(key);
    }

    public void Tick(DateTime nowLocal)
    {
        if (_draining.IsCancellationRequested || _shutdown.IsCancellationRequested)
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
                // SAID ONCE, NOT EVERY TICK. The commonest way to land here is a state file that cannot
                // be parsed — PrintSessionState_Store.Read_OrNull throws on one by design, because a
                // session whose identity is gone is not a default — and the tick runs every two seconds.
                // Unbounded, that is an error line every two seconds for as long as the app runs, which
                // buries the log it is written into. The stale-registration warning two blocks up already
                // dedupes for the same reason; this one did not.
                if (_warnedBrokenSessions.Add($"{registered.OrchId}/{registered.MemberId}"))
                    _log.Log_Error(registered.OrchId, $"Print dispatcher: '{registered.MemberId}' could not be considered — it is skipped from now on and this is NOT repeated; fix or delete its {PrintSessionState_Store.STATE_FILE_NAME} and restart the app", ex);
            }
        }
    }

    public async Task Stop_Async()
    {
        // DRAIN FIRST, CANCEL SECOND. Closing the door stops Tick and aborts turns still queued for
        // a slot; the ones already running keep their token and finish on their own — entry appended,
        // cursors advanced, nothing to redo at the next start. Only when the grace is spent are they
        // cancelled, and then they get CANCEL_GRACE to unwind like before.
        _draining.Cancel();

        var running = Snapshot_InFlight();

        if (running.Length > 0)
        {
            var grace = Get_DrainGrace();
            _log.Log_Info(string.Empty, $"Stopping — draining {running.Length} in-flight turn(s) before the sessions are killed (up to {grace.TotalMinutes:0} min; the turn timeout plus a minute)");

            try
            {
                await Task.WhenAll(running).WaitAsync(grace);
                _log.Log_Info(string.Empty, "Drain complete — every in-flight turn ended on its own; nothing is left to redo at the next start");
            }
            catch (TimeoutException)
            {
                _log.Log_Warning(string.Empty, $"Drain grace of {grace.TotalMinutes:0} min elapsed with {InFlightCount} turn(s) still running — cancelling them; their entries stay pending for the next start");
            }
            catch
            {
                // A turn's own failure is recorded by Execute_Turn_Async; the drain only waits.
            }
        }

        _shutdown.Cancel();

        var cancelled = Snapshot_InFlight();

        try
        {
            if (cancelled.Length > 0)
                await Task.WhenAll(cancelled).WaitAsync(CANCEL_GRACE);
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

    Task[] Snapshot_InFlight()
    {
        lock (_lock)
            return [.. _inFlight.Values];
    }

    /// <summary>The configured turn timeout plus a minute — read at stop time, so a config edit made while the app ran counts.</summary>
    TimeSpan Get_DrainGrace()
    {
        try
        {
            return _configProvider.Get_Current().Runners.TurnTimeout + DRAIN_MARGIN;
        }
        catch
        {
            // An unreadable config at shutdown is not a reason to kill work faster than the default would.
            return DRAIN_GRACE_FALLBACK;
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

        // THE BOOT TURN — the one turn that runs with NOTHING pending, and the only exception to
        // "an entry starts a turn".
        //
        // A bridge-driven session has no process until a turn runs, and no turn runs until an entry
        // arrives. For a member that is right: nobody is waiting on it. For the session that owns an
        // orchestration's OWNER channel it is a deadlock, because that session's greeting is what
        // creates the orchestration's Telegram topic (Bridge.OwnerPush_Policy.Is_OnlineGreeting) — so
        // the owner has nowhere to type, so no entry ever arrives, so the session never boots.
        // Measured on the VPS on 2026-09-06: a full crew started from the phone at 22:01:08 had no
        // topic until 22:07:30, and only because the task was appended to owner-channel.md by hand.
        // Under the terminal runner the supervisor was spawned WITH its role command and greeted at
        // once; the deadlock arrived with the bridge-driven runners, so it is closed where they made
        // it rather than papered over in the kit.
        if (ordered.Count == 0 && !Needs_BootTurn(state))
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
    /// Whether this session still owes the owner the greeting nothing else can produce.
    ///
    /// <para>
    /// TWO ROLES, because exactly two write an orchestration's owner channel: the SUPERVISOR of a crew
    /// and the SOLO of a basic orchestration (<see cref="TurnSource.TurnSources_Resolver.Resolve_Own"/>
    /// maps both onto <c>owner-channel.md</c>). A member's greeting reaches nobody but its supervisor,
    /// and booting every member on registration would buy two model turns each — one to greet, one for
    /// the supervisor woken by the greeting — for something no owner is waiting on. The GENERAL
    /// supervisor is excluded for a different reason: its topic is the supergroup's General, pinned by
    /// the owner, so it is reachable before it has ever run.
    /// </para>
    /// <para>
    /// ONCE, and "never completed a turn" is the whole test. An app restart re-registers every session
    /// and finds <see cref="IPrintSessionState.ExecutedTurns"/> non-empty, so nothing is dispatched and
    /// no second greeting is filed. A boot turn that FAILED leaves the list empty and is retried — under
    /// <see cref="MAX_ATTEMPTS"/> and the retry backoff like any other turn, because a supervisor whose
    /// one and only turn died is exactly the case where giving up is silent.
    /// </para>
    /// </summary>
    static bool Needs_BootTurn(IPrintSessionState state)
    {
        return state.ExecutedTurns.Count == 0 && state.Role is SessionRoles.Supervisor or SessionRoles.Solo;
    }

    /// <summary>
    /// Reads every source, baselining the ones this session has never seen and dropping the cursors of
    /// sources it no longer has. A change to the cursor set is PERSISTED HERE, before any turn: a
    /// baseline taken on a tick that starts no turn must survive a restart, or the same history is
    /// absorbed and announced again on every tick for ever.
    /// </summary>
    IReadOnlyList<SourceRead> Read_Sources(string stateFile, ref IPrintSessionState state, SessionRoles role, IReadOnlyList<ITurnSource> sources)
    {
        var known = state.Cursors.ToDictionary(cursor => cursor.SourceKey, SOURCE_KEYS);

        // NO CURSORS AT ALL means this session has never been through here — a state file written before
        // sources existed, or one whose `sources` array could not be read. Its channel holds a whole life
        // of traffic and none of it has been handed over by anything that recorded the fact, so it is
        // HISTORY, absorbed exactly as registration absorbs it. Treating it as "nothing delivered" would
        // replay up to a full live channel into one turn, which is the very thing the store's own note
        // says must not happen. A session that HAS cursors and meets a NEW key is the opposite case, and
        // is handled below.
        var firstSightOfThisSession = state.Cursors.Count == 0;

        List<SourceRead> reads = [];
        List<ITurnCursor> cursors = [];
        var changed = false;

        foreach (var source in sources)
        {
            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(source.ChannelFilePath));

            if (!known.TryGetValue(source.Key, out var cursor))
            {
                // A SOURCE THAT APPEARS WHILE THE SESSION IS RUNNING STARTS EMPTY, so everything in it
                // is traffic and none of it is absorbed. A channel that turns up now belongs to a member
                // that was created now, and its very first entry — the member's boot greeting, landing
                // between its spawn and the next tick — is exactly what a supervisor is here to answer.
                // Absorbing it would swallow the one entry this rule can ever see.
                cursor = firstSightOfThisSession
                    ? TurnCursor_Factory.Create_Baseline(source, role, entries)
                    : TurnCursor_Factory.Create_Empty(source);

                changed = true;

                if (cursor.Delivered.Count > 0)
                    _log.Log_Warning(state.OrchId, $"'{state.MemberId}' had no cursors at all, so channel '{source.Key}' was baselined on sight: the {cursor.Delivered.Count} inbound entr{(cursor.Delivered.Count == 1 ? "y" : "ies")} already in it are HISTORY and will not start a turn");
                else
                    _log.Log_Info(state.OrchId, $"'{state.MemberId}' is now also woken by channel '{source.Key}'");
            }

            cursors.Add(cursor);
            Warn_IfEntriesWereArchivedUndelivered(state, source, cursor, entries);

            reads.Add(new SourceRead(source, PrintTurn_Trigger.Select_Pending(role, entries, cursor)));
        }

        // A CURSOR IS NEVER DROPPED FOR A SOURCE THAT MERELY DID NOT RESOLVE THIS TICK. It used to be,
        // to keep the file tidy — and the roster read that decides is best-effort: an absent session.json
        // resolves to the owner channel alone, and rewriting the durable record from that transient
        // answer loses every spoke cursor irreversibly. The next successful read then meets them all as
        // unknown keys and re-delivers whole channels. Keeping a stale key costs one line in a JSON file
        // nobody counts; dropping it costs a supervisor re-answering every member it has.
        foreach (var cursor in state.Cursors)
        {
            if (!sources.Any(source => SOURCE_KEYS.Equals(source.Key, cursor.SourceKey)))
                cursors.Add(cursor);
        }

        if (changed)
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
        // TWO TOKENS. A turn WAITING for a slot is admitted under both signals — a drain aborts it,
        // its entries stay pending. A turn RUNNING holds only the shutdown token, so a drain lets it
        // finish and only the exhausted grace cancels it.
        var cancellationToken = _shutdown.Token;
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, _draining.Token);
        var orchestrationSlots = Get_OrchestrationSlots(state.OrchId);

        try
        {
            await _globalSlots.WaitAsync(admission.Token);

            try
            {
                await orchestrationSlots.WaitAsync(admission.Token);

                try
                {
                    // A slot won in the same instant the door closed is not a mandate to start.
                    admission.Token.ThrowIfCancellationRequested();
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
            // Shutdown, or a drain that closed the door before this turn got a slot: the cursors were
            // not advanced, so the same entries are pending at the next start.
        }
        catch (Exception ex)
        {
            _log.Log_Error(state.OrchId, $"Turn for '{state.MemberId}' failed outside the process", ex);
            tracker.LastFailureAt = DateTime.Now;
            Record_OutOfProcessFailure(stateFile, state, pending, tracker, ex);
        }
        finally
        {
            lock (_lock)
                _inFlight.Remove(key);
        }
    }

    /// <summary>
    /// A FAILURE OUTSIDE THE PROCESS IS STILL A FAILED ATTEMPT. It used to be logged and nothing else,
    /// so <see cref="IPrintSessionState.FailedAttempts"/> never moved and <see cref="MAX_ATTEMPTS"/>
    /// could never be reached: a <c>claude</c> missing from PATH, or an IO error thrown while appending
    /// the entry, produced one error line per tick for ever — no stall, no alert in the channel, and
    /// nothing at all where a human looks. The stall path exists precisely for "this keeps failing", and
    /// a failure that cannot reach it is the silence this repo keeps paying for.
    ///
    /// <para>
    /// THE STATE IS RE-READ, not reused. The turn may have written it after the caller captured it —
    /// claiming the session id does exactly that — and incrementing a stale copy would undo that write.
    /// </para>
    /// <para>
    /// And it never throws. It runs inside a catch handler; an exception escaping here leaves the task
    /// faulted with nobody awaiting it, which is a worse silence than the one it is fixing.
    /// </para>
    /// </summary>
    void Record_OutOfProcessFailure(string stateFile, IPrintSessionState captured, IReadOnlyList<PendingEntry> pending, SessionTracker tracker, Exception cause)
    {
        try
        {
            var state = PrintSessionState_Store.Read_OrNull(stateFile) ?? captured;
            var failed = PrintSessionState_Factory.CreateFrom_Existing_AttemptFailed(state);

            PrintSessionState_Store.Write(stateFile, failed);

            if (failed.FailedAttempts < MAX_ATTEMPTS)
                return;

            tracker.StalledOnSignature = Describe_PendingSignature(pending);

            var requestId = PrintTurn_RequestId.Build(state.OrchId, state.MemberId, state.NextTurnNumber);
            var alert = $"Turn {requestId} failed {failed.FailedAttempts} times before the process could report anything — not retried until new traffic arrives in the channels";

            _log.Log_Error(state.OrchId, alert, null);

            ChannelAppender.Append_AppEntry(
                state.ChannelFilePath,
                Stall_Audience(state),
                $"{TURN_STALLED_SUBJECT} {state.MemberId} turn {state.NextTurnNumber} — failed outside the process × {failed.FailedAttempts}",
                $"{alert}\n\nrequest_id: {requestId}\n{Describe_Traffic(pending)}\nlast error: {cause.GetType().Name}: {cause.Message}",
                DateTime.Now);
        }
        catch (Exception ex)
        {
            _log.Log_Error(captured.OrchId, $"'{captured.MemberId}': the out-of-process failure could not be recorded either — the attempt counter did not move, so this turn keeps retrying", ex);
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
        //
        // SPENT IS STILL NOT THE SAME AS EXISTS, and the gap is recovered from rather than predicted —
        // see TRANSCRIPT_GONE_SIGNAL and Record_Failure. Guessing here was tried and was wrong: the id
        // is claimed before the process starts, but an attempt that ran and merely answered badly DOES
        // have a transcript, and refusing to resume it would throw away work every time a model errored.
        // ONE SOURCE FOR "IS THIS ID SPENT": the flag. It used to be `SessionStarted || hasHistory`, and
        // the second half quietly outvoted the first — when the CLI told us the transcript was gone and
        // the id was given back, a session with turns behind it went on resuming the FRESH id it had just
        // been handed, and was refused again, every attempt, for ever. History is not evidence about the
        // id the session is holding NOW. It was only ever a fallback for a state file written before the
        // flag existed, and the store already applies exactly that fallback when the field is absent.
        var sessionUsed = state.SessionStarted;
        var resumeTranscript = !fresh && sessionUsed;
        var sessionId = fresh && sessionUsed ? Guid.NewGuid().ToString() : state.SessionId;

        // Claimed BEFORE the process starts, so a bridge that dies mid-turn still knows the id is
        // spent and resumes instead of colliding with itself.
        if (!resumeTranscript)
        {
            state = PrintSessionState_Factory.CreateFrom_Existing_SessionClaimed(state, sessionId);
            PrintSessionState_Store.Write(stateFile, state);
        }

        // NOT CLEARED HERE. It used to be, one line below this, before the turn's outcome was known —
        // so a first turn after a bridge restart that timed out or errored had attempts 2 and 3 resume
        // the very transcript that may remember answering, with the preamble that says "those turns are
        // done, do not repeat them" switched off. The decision-8 guard was off on exactly the retry it
        // exists for. It is cleared where a turn actually COMPLETES instead.
        var alreadyExecuted = tracker.FirstTurnSinceStart && hasHistory
            ? state.ExecutedTurns.Select(turn => turn.TurnNumber).ToList()
            : [];

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
            if (!await Write_Reply_Async(state, sources, result.ResultText))
            {
                _log.Log_Error(state.OrchId, $"Turn {requestId} completed but its entry could not be appended — the channel stayed locked; the turn will be retried", null);
                Record_Failure(stateFile, state, pending, tracker, result, requestId, attempt, executor, "entry not appended (channel locked)");
                return;
            }

            Append_TurnEnded(state, requestId, attempt, pending, result, outcome, null);

            // A BOOT TURN ANSWERED NO ENTRY. Zero is not an index any channel has — they are numbered
            // from 1 — so the record reads as "none" rather than borrowing the first entry of a turn
            // it never saw, and the range in the turn_ended entry stays honest.
            var firstIndex = pending.Count == 0 ? 0 : pending[0].Entry.Index;
            var lastIndex = pending.Count == 0 ? 0 : pending[^1].Entry.Index;

            var executed = ExecutedTurn_Factory.Create(turnNumber, requestId, firstIndex, lastIndex, DateTime.UtcNow, outcome, result.TotalCostUsd);

            PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(state, executed, result.SessionId ?? sessionId, Advance_Cursors(state, sources, pending)));
            tracker.LastFailureAt = null;
            tracker.FirstTurnSinceStart = false;

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
        var byKey = state.Cursors.ToDictionary(cursor => cursor.SourceKey, SOURCE_KEYS);

        List<ITurnCursor> advanced = [];

        foreach (var source in sources)
        {
            if (!byKey.TryGetValue(source.Key, out var cursor))
                continue;

            var delivered = pending.Where(item => SOURCE_KEYS.Equals(item.Source.Key, source.Key)).Select(item => item.Entry).ToList();
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
    /// A PARTIAL WRITE COSTS A SECOND, DIFFERENT ANSWER — not a duplicate, and the difference matters.
    /// If one of several blocks cannot be appended — its channel held by another writer for the whole
    /// budget — the turn is reported failed and RE-RUN, so the model answers again: a member that already
    /// received verdict A can then receive verdict B and may have acted on the superseded one. The
    /// alternative is to accept the partial write, which loses the block that failed with nobody told and
    /// leaves the session it was for waiting for ever. Both are bad; this one is bad WHERE SOMEONE CAN SEE
    /// IT, which is the rule everywhere else here. (An earlier version of this comment claimed the retry
    /// merely duplicates what landed. It does not, and a comment that understates its own trade is how the
    /// next reader accepts it without noticing.)
    /// </para>
    /// </summary>
    /// <param name="resultText">
    /// NULLABLE, because <see cref="ITurnResult.ResultText"/> is: a turn can succeed and say nothing.
    /// Both splitters below already accept null and answer with the "(no message)" entry, so the only
    /// thing a non-null signature bought was a compiler warning at the one call site — and a signature
    /// that promises what its caller cannot give is a lie that the next reader resolves with a `!`.
    /// </param>
    async Task<bool> Write_Reply_Async(IPrintSessionState state, IReadOnlyList<ITurnSource> sources, string? resultText)
    {
        var author = SessionRole_Names.Get_Author(state.Role);
        var ownChannel = state.ChannelFilePath;

        if (sources.Count <= 1)
        {
            var (soleSubject, soleBody) = PrintTurnEntry_Splitter.Split(resultText);
            return await Append_SessionEntry_WithRetry_Async(ownChannel, author, soleSubject, soleBody);
        }

        var byKey = sources.ToDictionary(source => source.Key, SOURCE_KEYS);
        var blocks = TurnReply_Splitter.Split(resultText);

        if (blocks.Count == 0)
        {
            var (emptySubject, emptyBody) = PrintTurnEntry_Splitter.Split(resultText);
            return await Append_SessionEntry_WithRetry_Async(ownChannel, author, emptySubject, emptyBody);
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

            if (!await Append_SessionEntry_WithRetry_Async(target, author, subject, body))
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

        // THE TRANSCRIPT IS NOT THERE, AND THE CLI SAID SO. The id is claimed BEFORE the process starts,
        // so a first turn that dies before the CLI creates the transcript — an invalid --model, an auth
        // failure, the binary not on PATH — leaves every later attempt resuming something that was never
        // made. MEASURED 2026-09-06 against 2.1.263: `claude -p --resume <uuid never created>` exits 1
        // with "No conversation found with session ID: <uuid>". All three attempts died that way, the
        // session stalled, and new traffic only reset the counter and retried the same doomed resume:
        // recovery meant deleting print-session.json by hand.
        //
        // REACTED TO, NOT PREDICTED. The obvious guess — "never completed a turn, so do not resume" — is
        // wrong in the other direction: an attempt that ran and merely answered badly has a transcript
        // worth keeping, which is the case the previous stage measured and pinned. The CLI distinguishes
        // the two for us; this listens to it and unspends the id so the next attempt claims a fresh one.
        if (Transcript_IsGone(result))
        {
            _log.Log_Warning(state.OrchId, $"'{state.MemberId}': the CLI has no transcript for session {state.SessionId} — the id was claimed but never created, so the next attempt starts a fresh one instead of resuming a conversation that does not exist");
            state = PrintSessionState_Factory.CreateFrom_Existing_SessionUnclaimed(state, Guid.NewGuid().ToString());
        }

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
                Stall_Audience(state),
                $"{TURN_STALLED_SUBJECT} {state.MemberId} turn {state.NextTurnNumber} — {outcome} × {failed.FailedAttempts}",
                $"{alert}\n\nrequest_id: {requestId}\n{Describe_Traffic(pending)}\nlast exit_code: {result.ExitCode}\napi_error_status: {Describe_ApiErrorStatus(result)}\nstderr (tail): {Tail(result.RawStderr, 600)}",
                DateTime.Now);

            return;
        }

        _log.Log_Warning(state.OrchId, $"Turn {requestId} attempt {attempt} {outcome}{(note == null ? string.Empty : $" ({note})")} — retry after {_retryBackoff.TotalSeconds:F0} s; {Tail(result.RawStderr, 200)}");
    }

    /// <summary>
    /// AWAITED, NOT SLEPT. This parks up to <see cref="ENTRY_APPEND_ATTEMPTS"/> × 
    /// <see cref="ENTRY_APPEND_RETRY_MILLISECONDS"/> of waiting, and since a multi-source session writes
    /// one entry PER ADDRESSED BLOCK it can now run several times in one turn — on a threadpool thread,
    /// with up to <c>MaxConcurrentTurns</c> turns doing the same. Blocking it held threads the pool
    /// could have used; awaiting releases them.
    ///
    /// <para>
    /// The delay deliberately takes NO cancellation token. It is reached only after the model has
    /// already answered, so cancelling here would throw away a reply that was paid for — and the whole
    /// wait is under a second, which no shutdown notices.
    /// </para>
    /// </summary>
    static async Task<bool> Append_SessionEntry_WithRetry_Async(string channelFilePath, ChannelAuthors author, string subject, string body)
    {
        for (var attempt = 0; attempt < ENTRY_APPEND_ATTEMPTS; attempt++)
        {
            if (ChannelAppender.Append_SessionEntry(channelFilePath, author, subject, body, DateTime.Now))
                return true;

            await Task.Delay(ENTRY_APPEND_RETRY_MILLISECONDS);
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
        // SAID, not left as a bare "entries:". The empty set has one cause and it is worth reading in
        // the log and in the turn_ended entry: this was the boot turn, run so the session could greet.
        if (pending.Count == 0)
            return "entries: none (boot turn)";

        List<string> parts = [];

        foreach (var group in pending.GroupBy(item => item.Source.Key, StringComparer.Ordinal))
        {
            // MIN AND MAX, not first and last: the entries were globally ordered by stamp before they
            // got here, so within one channel the first listed is not necessarily the lowest — and
            // "imp-1 [12]–[3]" is a range no reader can make sense of.
            var indices = group.Select(item => item.Entry.Index).ToList();
            var lowest = indices.Min();
            var highest = indices.Max();

            parts.Add(lowest == highest ? $"{group.Key} [{lowest}]" : $"{group.Key} [{lowest}]–[{highest}]");
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

    /// <summary>
    /// The CLI's own words for "that session id names nothing", measured on 2.1.263 (2026-09-06):
    /// <c>No conversation found with session ID: &lt;uuid&gt;</c>, exit 1. Matched on the stable half of the
    /// sentence, so a reworded suffix does not stop it being recognised.
    ///
    /// <para>
    /// A STRING IS A FRAGILE CONTRACT AND THAT IS ACCEPTED HERE, because of which way it fails: if a
    /// future CLI renames this, the recovery stops firing and the behaviour falls back to the stall it
    /// replaced — visible, and no worse than before. The fake emits the same sentence for an unknown
    /// <c>--resume</c>, so a rename turns a contract test red rather than wedging a live session.
    /// </para>
    /// </summary>
    const string TRANSCRIPT_GONE_SIGNAL = "No conversation found with session ID";

    static bool Transcript_IsGone(ITurnResult result)
    {
        return (result.RawStderr ?? string.Empty).Contains(TRANSCRIPT_GONE_SIGNAL, StringComparison.OrdinalIgnoreCase)
            || (result.RawStdout ?? string.Empty).Contains(TRANSCRIPT_GONE_SIGNAL, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// WHO CAN ACT ON A STALL. For a member the alert lands in its spoke, where the supervisor reads it —
    /// agent audience, and the owner is rightly not told (decision 15).
    ///
    /// <para>
    /// For a SOLO, a GENERAL supervisor or an orchestration SUPERVISOR the session's own channel IS the
    /// owner's channel, and the only agent that reads it is the stalled session itself. Filed as an agent
    /// entry it was suppressed from the mirror as well, so a solo orchestration that gave up after three
    /// failures went quiet with nothing on the owner's phone and nothing in their topic — the exact
    /// silence this alert exists to break. A session that has stopped answering is something the owner can
    /// act on, so for those roles it is addressed to them.
    /// </para>
    /// </summary>
    static AppEntryAudiences Stall_Audience(IPrintSessionState state)
    {
        return state.Role is SessionRoles.Solo or SessionRoles.General or SessionRoles.Supervisor
            ? AppEntryAudiences.Owner
            : AppEntryAudiences.Agent;
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
