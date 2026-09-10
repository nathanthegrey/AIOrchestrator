using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The three gates that were unreachable, now where the suite can ask about them.
///
/// A reviewer deleted the trusted-stamp reader, the per-topic delivery gate and the backoff gate ALL
/// AT ONCE and the suite stayed green — then proved the build was genuine by injecting a syntax error
/// into the same engine file and watching it fail. The green was necessary, not observed:
/// BridgeEngineModel is internal sealed, there is no InternalsVisibleTo, and the test project
/// references CoreLib alone.
///
/// InternalsVisibleTo was the cheaper seam and was refused: it would make the engine testable without
/// making it tested. Moving the two error predicates out is what made them pinned; this applies the
/// same move to the gates and to the wiring that activates them.
/// </summary>
public class TopicStatusLinePlannerTests
{
    static readonly DateTime NOW = new(2026, 8, 12, 15, 0, 0);
    const int BACKOFF = 30;
    const long STATUS_ID = 4242;

    /// <summary>The ordinary case: something to say, nothing posted, delivery normal, no failures.</summary>
    [Fact]
    public void AFirstLineIsPosted()
    {
        Assert.Equal(TopicStatusActions.Post, Plan().Action);
    }

    /// <summary>
    /// THE DELIVERY GATE, on the POST — and SILENCED ONLY since 2026-09-10. 🔕 means the owner is
    /// reading the same content live in a terminal and asked not to have it twice; dropping is the
    /// mode's whole contract, so a first PULSE is not posted into it.
    /// </summary>
    [Fact]
    public void ASilencedTopicIsNotPostedInto()
    {
        Assert.Equal(TopicStatusActions.None, Plan(mode: TelegramDeliveryModes.Silenced).Action);
    }

    /// <summary>
    /// AND DEFERRED IS NOT SILENCED — the other half of the owner's ruling of 2026-09-09: "🌙 holds
    /// only what rings; PULSE and the dashboard keep updating silently". This case asserted None
    /// until 2026-09-10, sharing a Theory with the Silenced one, and the shared assertion was the
    /// mistake: the two modes mean OPPOSITE things about content. Deferred keeps everything and
    /// replays it because the owner is coming back to it; Silenced throws it away.
    ///
    /// The gate could treat them alike while a post NOTIFIED. Every write here is silent now, so
    /// under 🌙 there is nothing left to protect the owner from — and refusing the first post cost
    /// them a topic with NO status surface at all for the length of the mute, which is what their
    /// check-in ritual reads when they come back.
    /// </summary>
    [Fact]
    public void ADeferredTopicIsStillPostedInto_Silently()
    {
        Assert.Equal(TopicStatusActions.Post, Plan(mode: TelegramDeliveryModes.Deferred).Action);
    }

    /// <summary>
    /// And NOT on the edit, under either mode. An edit notifies nobody, so gating it buys nothing and
    /// costs a line frozen at pre-DND content for the whole period — Deferred's contract is that
    /// nothing is lost. Asserted separately from the POST cases so none can pass for another's reason.
    /// </summary>
    [Theory]
    [InlineData(TelegramDeliveryModes.Silenced)]
    [InlineData(TelegramDeliveryModes.Deferred)]
    public void ASilencedTopicIsStillEdited(TelegramDeliveryModes mode)
    {
        Assert.Equal(TopicStatusActions.Edit, Plan(mode: mode, existingMessageId: 4242, lastWrittenText: "something older").Action);
    }

    /// <summary>
    /// THE BACKOFF. A 429 answered at the tick rate inverts the cadence from once a minute to thirty
    /// times a minute per topic and sustains the throttling that caused it.
    /// </summary>
    [Fact]
    public void ARecentFailureHoldsTheNextAttempt()
    {
        Assert.Equal(TopicStatusActions.None, Plan(lastFailedAttemptAt: NOW.AddSeconds(-5)).Action);
        Assert.Equal(TopicStatusActions.Post, Plan(lastFailedAttemptAt: NOW.AddSeconds(-BACKOFF)).Action);
    }

    [Fact]
    public void NoRecordedFailureIsAlwaysDue()
    {
        Assert.True(TopicStatusLine_Planner.Is_AttemptDue(null, NOW, BACKOFF));
        Assert.False(TopicStatusLine_Planner.Is_AttemptDue(NOW.AddSeconds(-1), NOW, BACKOFF));
    }

    /// <summary>
    /// THE CLOCK MISMATCH, written down as a test because no assertion can catch it structurally —
    /// the tests build both sides from one constant, so two clocks can never disagree in here.
    ///
    /// This is what it LOOKED like in production: the failure stamp came from UtcNow while `now` came
    /// from DateTime.Now, so on a UTC+2 machine one second after a failure computed as two hours
    /// elapsed and a 30-second backoff cleared instantly — at every value it could be given. The 429
    /// protection was absent while every test passed.
    ///
    /// The fix is that the caller now has only ONE clock to give. This case pins the arithmetic that
    /// made it invisible, so the next reader recognises the shape rather than rediscovering it.
    /// </summary>
    [Fact]
    public void AStampFromTheWrongClockReadsAsImmediatelyDue()
    {
        var twoHoursBehind = NOW.AddHours(-2);

        Assert.True(TopicStatusLine_Planner.Is_AttemptDue(twoHoursBehind, NOW, BACKOFF));
        Assert.False(TopicStatusLine_Planner.Is_AttemptDue(NOW.AddSeconds(-1), NOW, BACKOFF));
    }

    /// <summary>
    /// THE WIRING, which is what M-G3 caught: the engine could pass `false` where it meant "a message
    /// exists" and nothing reddened, so the whole spin fix rested on an argument no test could see.
    /// The planner takes the ID and derives the flag itself, so there is no boolean to get wrong —
    /// and this asserts the derivation from both sides.
    /// </summary>
    [Fact]
    public void TheMessageIdDecidesWhatNothingToSayMeans()
    {
        // Nothing to report at all: silence with no message, the bare LEAD WORD with one. It
        // used to be the bare topic title; the topic name left the line on 2026-08-24 and the
        // opening field is now the literal `PULSE`, which is what tells this constantly-edited
        // message apart from the half-hourly digest that opens with STATUS.
        Assert.Equal("", Plan(members: []).Text);
        Assert.Equal("PULSE", Plan(members: [], existingMessageId: 4242, lastWrittenText: "old row").Text);
    }

    /// <summary>
    /// GATE C — the trusted reading of an agent-written stamp, which never left the engine and so
    /// could be reverted to a raw parse with 630 tests staying green. A FUTURE-dated entry must not
    /// win `last` and hold it until real time catches up.
    /// </summary>
    [Fact]
    public void AFutureDatedEntryDoesNotWinTheLastLine()
    {
        var members = new[]
        {
            Member("imp-1", "the real latest", "2026-08-12 14:50"),
            Member("imp-2", "stamped in the future", "2026-08-13 23:00"),
        };

        Assert.Equal("the real latest", TopicStatusLine_Planner.Pick_LastSubject_OrNull(members, NOW));
    }

    /// <summary>An unparseable stamp loses rather than winning by accident.</summary>
    [Fact]
    public void AnUnparseableStampDoesNotWinTheLastLine()
    {
        var members = new[]
        {
            Member("imp-1", "the real latest", "2026-08-12 14:50"),
            Member("imp-2", "no date at all", "not a date"),
        };

        Assert.Equal("the real latest", TopicStatusLine_Planner.Pick_LastSubject_OrNull(members, NOW));
    }

    /// <summary>And the ordinary case still picks the genuinely most recent.</summary>
    [Fact]
    public void TheLatestTrustworthyStampWins()
    {
        var members = new[]
        {
            Member("imp-1", "older", "2026-08-12 10:00"),
            Member("imp-2", "newer", "2026-08-12 14:55"),
        };

        Assert.Equal("newer", TopicStatusLine_Planner.Pick_LastSubject_OrNull(members, NOW));
    }

    /// <summary>
    /// PINS THE CALL, not the callee. Replacing Pick_LastSubject_OrNull(...) with a plain null at the
    /// planner's own call site left 634 green, because the only two assertions on Plan(...).Text used
    /// an EMPTY roster — where the picker returns null anyway — and every other Plan assertion looks
    /// at .Action.
    ///
    /// Third instance of one shape: the derived bool, then the clock, now the picker call. Each time
    /// a decision moved somewhere the tests could reach and the WIRING that activates it stayed
    /// behind, unobserved. The rule is to pin the call as well as the thing it calls.
    ///
    /// In production that mutation removes the `last` row from every topic message, and where the
    /// subject is the only substance it reduces the message to the bare title or to nothing.
    ///
    /// ADAPTED 2026-09-10: `[^1]` stopped being the `last` line once PULSE grew a trailing
    /// `updated HH:MM` heartbeat (Brief C, field 6) — the array's last element is now always that
    /// line when there is anything to say at all. Picking the line by its own `last ` prefix instead
    /// of by position keeps the claim ("the picker's answer lands in the text") true regardless of
    /// which other fields are present.
    /// </summary>
    [Fact]
    public void ThePlanActuallyCallsThePickerAndPutsTheWinnerInTheText()
    {
        var plan = Plan(members:
        [
            Member("imp-1", "older thing", "2026-08-12 10:00"),
            Member("imp-2", "the winning subject", "2026-08-12 14:55"),
        ]);

        // Asserted on the `last` LINE, not on the whole text: every member's brief also appears as
        // its own row, so "contains the subject" is satisfied by the row and says nothing about the
        // picker. The `last` line is the only place the picker's answer shows up.
        var lastFieldLine = plan.Text.Split('\n').Single(line => line.StartsWith("last "));

        Assert.Contains("the winning subject", lastFieldLine);
        Assert.DoesNotContain("older thing", lastFieldLine);
    }

    /// <summary>
    /// CONTENT PROBE (d) from Brief C's "Done when": the `last` field is the latest SUPERVISOR
    /// subject, never the first `[>]` ledger line — the old STATUS message's defect ("now: FIN-D-293a
    /// step 6 and step 7" repeated identically for hours after the work merged). PULSE's `last` field
    /// never reads the ledger at all: it is Pick_LastSubject_OrNull's answer and nothing else, so this
    /// pins that a ledger with a same-named in-progress line cannot leak into it.
    /// </summary>
    [Fact]
    public void TheLastFieldIsTheLatestSupervisorSubjectNeverTheFirstInProgressLedgerLine()
    {
        var progress = PlanProgress_Factory.Create(
            0, 1, 0, 0, 1, "the first in-progress ledger line",
            ["the first in-progress ledger line"], [], [], null,
            [new PlanLedgerLine(">", "the first in-progress ledger line")]);

        var plan = Plan(
            progress: progress,
            members: [Member("imp-1", "the real latest supervisor subject", "2026-08-12 14:55")]);

        var lastFieldLine = plan.Text.Split('\n').Single(line => line.StartsWith("last "));

        Assert.Contains("the real latest supervisor subject", lastFieldLine);
        Assert.DoesNotContain("first in-progress ledger line", lastFieldLine);
    }

    // ── THE REPOST, owner directive 2026-08-13 ────────────────────────────────────────────────────
    //
    // Posted once and edited forever meant the line SCROLLED AWAY: entering the topic showed whatever
    // was last said, and the current state was somewhere above. The owner wants the status to be the
    // thing they see without typing a command, so when it is no longer the last message AND the topic
    // has gone quiet, it is rewritten at the bottom. While it IS the last message it keeps being
    // edited exactly as before, because an edit notifies nobody.
    //
    // THE WINDOW IS TEN SECONDS since 2026-08-24 — two minutes made the move correct and invisible,
    // and the owner asked for it to feel immediate. See REPOST_AFTER_QUIET_SECONDS for why a short
    // window is not a waterfall: the status line's own message is never recorded as topic traffic, so
    // the fresh post reads as un-buried and nothing reposts again until real traffic arrives.

    /// <summary>
    /// The rule as the owner stated it: buried by later traffic, and the topic has gone quiet.
    /// </summary>
    [Fact]
    public void AStatusLineBuriedByLaterTrafficIsRepostedOnceTheTopicGoesQuiet()
    {
        var plan = Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)));

        Assert.Equal(TopicStatusActions.Repost, plan.Action);
    }

    /// <summary>
    /// AND THE OTHER SIDE, which is the one that keeps this feature from becoming a waterfall: while
    /// the status line IS the last message it is EDITED, silently, exactly as before. A repost
    /// notifies — Telegram cannot move a message — so one that fires while the line is already at the
    /// bottom would ping the owner for a duration ticking from 4 to 5 minutes.
    /// </summary>
    [Fact]
    public void AStatusLineThatIsStillTheLastMessageIsEditedInPlace()
    {
        var plan = Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID - 20, NOW.AddHours(-1)));

        Assert.Equal(TopicStatusActions.Edit, plan.Action);
    }

    /// <summary>
    /// THE QUIET WINDOW, asserted THROUGH Plan and at its boundary — so it pins the wiring of the
    /// constant as well as the arithmetic. Item: the derived bool, the clock and the picker call were
    /// each moved somewhere reachable while the wiring that activates them stayed behind, unobserved.
    ///
    /// One second short holds; the window itself fires. Without the window a repost would land on the
    /// owner's phone in the middle of their own conversation, which is the opposite of the ask.
    /// </summary>
    [Fact]
    public void TheRepostWaitsForTheTopicToGoQuiet()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddSeconds(-(TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS - 1)))).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddSeconds(-TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS))).Action);
    }

    /// <summary>
    /// AND THE WINDOW IS TEN SECONDS — the one number the owner actually specified (2026-08-24:
    /// *"the topic status message should arrive immediately, not after 2 minutes, but more like after
    /// 10 seconds"*), asserted with LITERAL seconds because the case above cannot see it.
    ///
    /// F2, rev-1: every other test passes `REPOST_AFTER_QUIET_SECONDS` symbolically, so they pin the
    /// ARITHMETIC around the constant and never its VALUE. Set the constant to 0 and all of them stay
    /// green — `AddSeconds(-(0 - 1))` is a stamp one second in the FUTURE, which holds, and
    /// `AddSeconds(0)` is due. A repost would then fire the instant any message buried the line: a
    /// notification in the middle of the owner's own sentence, which is the waterfall item 14 exists
    /// to prevent, arriving with a green suite.
    ///
    /// 9 and 10 pin it EXACTLY rather than approximately: asserting only that 11 reposts would allow
    /// any window from 0 to 11, and asserting only that 9 holds would allow the old 120 to survive.
    /// The 120 case is what makes this red against the previous value, and the 11 case is the owner's
    /// sentence read literally — "just over ten seconds" must already be moving.
    /// </summary>
    [Fact]
    public void TheQuietWindowIsTenSeconds()
    {
        Assert.Equal(TopicStatusActions.Edit, Plan_AfterQuietSeconds(5).Action);
        Assert.Equal(TopicStatusActions.Edit, Plan_AfterQuietSeconds(9).Action);
        Assert.Equal(TopicStatusActions.Repost, Plan_AfterQuietSeconds(10).Action);
        Assert.Equal(TopicStatusActions.Repost, Plan_AfterQuietSeconds(11).Action);
    }

    /// <summary>
    /// THE OWNER'S COMPLAINT, as a case rather than as a boundary: a line buried while the topic then
    /// went quiet for a quarter of a minute must ALREADY have moved. This is the assertion that reads
    /// red against the old two-minute window — 15 seconds of quiet planned an Edit there, which is
    /// the "it arrives after 2 minutes" the owner reported.
    /// </summary>
    [Fact]
    public void AShortPauseIsAlreadyLongEnoughToMoveTheLine()
    {
        Assert.Equal(TopicStatusActions.Repost, Plan_AfterQuietSeconds(15).Action);
        Assert.Equal(TopicStatusActions.Repost, Plan_AfterQuietSeconds(60).Action);
    }

    /// <summary>
    /// AND THE SHORT WINDOW IS STILL A PAUSE-DETECTOR, which is the half that keeps it from becoming
    /// the waterfall item 14 exists to prevent. A message that landed a moment ago holds the line
    /// exactly as it did at 120 — the owner typing a second sentence is not a quiet topic, and the
    /// repost must not interrupt them mid-thought.
    /// </summary>
    [Fact]
    public void TrafficThatHasJustLandedStillHoldsTheRepost()
    {
        Assert.Equal(TopicStatusActions.Edit, Plan_AfterQuietSeconds(0).Action);
        Assert.Equal(TopicStatusActions.Edit, Plan_AfterQuietSeconds(2).Action);
    }

    /// <summary>
    /// WHAT ACTUALLY BOUNDS THE REPOST, and it is not the window. After a repost the app's stored id
    /// is the FRESH message, whose Telegram id is higher than every message the topic has seen —
    /// and the status line's own post is deliberately never recorded as topic traffic
    /// (`BridgeEngineModel._newestTopicMessageByThread` is written only by `Remember_TopicMessage`,
    /// which the status-line refresh does not call). So the very next tick reads the line as
    /// UN-BURIED and plans an edit, at any window value.
    ///
    /// This is the case that says a ten-second window cannot delete-and-send every ten seconds in a
    /// quiet topic: without it, "10 is safe" rests on an argument in a comment in another file.
    /// The state is the one that exists two seconds after a repost — quiet far longer than the
    /// window, and the stored id now above the newest traffic id.
    /// </summary>
    [Fact]
    public void AFreshlyRepostedLineIsNoLongerBuriedAndDoesNotRepostAgain()
    {
        const long BURYING_TRAFFIC_ID = STATUS_ID + 20;
        const long REPOSTED_ID = BURYING_TRAFFIC_ID + 1;

        // The traffic that buried the old line is still the newest thing the app knows of, and it is
        // now an hour old — far past any window this constant could hold.
        var newest = Newest(BURYING_TRAFFIC_ID, NOW.AddHours(-1));

        Assert.False(TopicStatusLine_Planner.Is_RepostDue(
            REPOSTED_ID, newest, NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));

        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: REPOSTED_ID, lastWrittenText: "an older line", newestTopicMessage: newest).Action);
    }

    /// <summary>
    /// THE RULE REVERSED, ON THE OWNER'S OWN WORDS (2026-09-09, brief C): PULSE is re-posted "only
    /// when it is buried by later traffic AND its content changed". This test ASSERTED THE OPPOSITE
    /// until 2026-09-10 and it was not wrong then — burial alone was the rule, and the summary above
    /// it argued the case for it: a quiet topic never changes its text, so a content-gated repost
    /// would never move a quiet topic's line.
    ///
    /// The owner read that trade and took the other side. Their complaint is the one this whole brief
    /// answers — half of what reaches the phone is not for them — and a repost that carries no news
    /// is the surface breaking its own promise: PULSE exists so that status costs no notifications,
    /// and every pause in a talkative topic was buying a delete plus a post that said the same thing.
    /// A buried unchanged line is a cosmetic loss (it is above some traffic); a repost of it is
    /// traffic. Cosmetics lose.
    ///
    /// KEPT UNDER ITS OLD NAME INVERTED RATHER THAN DELETED, so the reversal is visible in the diff
    /// of the file that carried the old claim, and nobody re-derives the old rule from the argument
    /// still written above it.
    ///
    /// Both sides, from the SAME text: unchanged is silence whether or not it is buried.
    /// </summary>
    [Fact]
    public void TheRepostDoesNotFireWhenTheTextHasNotChanged()
    {
        var current = Plan(existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID - 20, NOW.AddHours(-1))).Action);

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// THE BRIEF'S OWN PROBE, both halves in one place: "PULSE buried under 3 later messages with
    /// unchanged content → not re-posted; content changes → one silent re-post at the bottom".
    ///
    /// The three messages are modelled the way the planner sees burial — by the newest id the app
    /// knows of, which is the third of them — because that is the only thing `Is_RepostDue` reads.
    /// Asserting on a count of intermediate messages would test a counter this feature does not have.
    ///
    /// ONE repost, not two: the second call re-runs the same tick with the line now carrying the new
    /// text, which is the state the engine is in immediately after a successful repost. It must go
    /// quiet — otherwise a changed line reposts on every tick for as long as it stays buried, which
    /// is the waterfall by another door.
    /// </summary>
    [Fact]
    public void BuriedAndUnchangedStaysPut_BuriedAndChangedMovesOnce()
    {
        var buriedUnderThree = Newest(STATUS_ID + 3, NOW.AddMinutes(-2));

        var current = Plan(existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: buriedUnderThree).Action);

        var afterAChange = Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "what PULSE said before anything moved",
            newestTopicMessage: buriedUnderThree);

        Assert.Equal(TopicStatusActions.Repost, afterAChange.Action);
        Assert.False(string.IsNullOrWhiteSpace(afterAChange.Text));

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: afterAChange.Text,
                 newestTopicMessage: buriedUnderThree).Action);
    }

    /// <summary>
    /// THE RESTART, which is where a content-gated repost could have gone wrong and does not. The
    /// remembered text lives in memory, so after a restart every topic has an id and no last text —
    /// `Decide` reads that as Edit, which this planner counts as news. On its own that would repost
    /// every buried line at every startup, all at once: a notification storm on the one event the
    /// owner did not ask for.
    ///
    /// It cannot happen, because the NEWEST-MESSAGE map is in memory as well: until the app observes
    /// real traffic in a topic it knows of no message that could have buried the line, and
    /// `Is_RepostDue` refuses a topic it knows nothing about. This test pins the PAIR — the two blind
    /// spots cover each other, and either one made durable alone would open the storm.
    /// </summary>
    [Fact]
    public void AfterARestartNothingIsRepostedUntilRealTrafficIsSeen()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: null, newestTopicMessage: null).Action);
    }

    /// <summary>
    /// With no status message up there is nothing to move: the first line is still a POST, not a
    /// repost, and it must not delete an id it does not have.
    /// </summary>
    [Fact]
    public void WithNoStatusMessageUpThereIsNothingToRepost()
    {
        Assert.Equal(
            TopicStatusActions.Post,
            Plan(newestTopicMessage: Newest(9999, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// AN UNKNOWN TOPIC IS NOT A BURIED ONE. The newest id is remembered in memory, so after an app
    /// restart it is absent for every topic until traffic repopulates it — and "I do not know" must
    /// not be answered with a notification. It edits, as it always did, and the first message through
    /// the mirror restores the knowledge.
    /// </summary>
    [Fact]
    public void ATopicWithNoKnownTrafficIsNotReposted()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line", newestTopicMessage: null).Action);
    }

    /// <summary>
    /// THE DELIVERY GATE APPLIES TO SILENCED, and it falls back to the EDIT rather than to silence:
    /// the edit notifies nobody, so the line stays current instead of freezing, and only the MOVE to
    /// the bottom waits.
    /// </summary>
    [Fact]
    public void ASilencedTopicIsNotRepostedIntoAndFallsBackToTheEdit()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(mode: TelegramDeliveryModes.Silenced, existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// A DEFERRED TOPIC STILL MOVES ITS LINE, silently — the repost half of the same ruling. It
    /// shared a Theory with the Silenced case until 2026-09-10 on the strength of one sentence, "a
    /// repost NOTIFIES", which stopped being true when every send in this surface became silent.
    ///
    /// Under 🌙 the owner is away and will read this topic when they return; a PULSE stranded above
    /// an hour of later traffic is the one thing they then have to scroll for.
    /// </summary>
    [Fact]
    public void ADeferredTopicStillMovesItsLine_Silently()
    {
        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(mode: TelegramDeliveryModes.Deferred, existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// And the fallback is to what the decider actually said, not to an edit regardless: silenced,
    /// buried, and nothing new to say is NOTHING. Falling back to a blanket Edit would write the same
    /// text every tick for the whole DND period — the wasted-call spin the identical-text rule exists
    /// to stop, reintroduced through the back door of a feature that is supposed to be quiet.
    /// </summary>
    [Fact]
    public void ASilencedTopicWithNothingNewToSayStaysSilent()
    {
        // `current` is captured under the SAME mode the second call uses. The header now carries the
        // mode glyph (the planner fills `fields.Mode` in from `mode` before calling the builder — see
        // TopicStatusLine_Planner.Plan's own comment, "THE MODE IS FILLED IN HERE"), so a "previously
        // written" text for an ALREADY-silenced topic would itself read `🔕 PULSE`, never bare `PULSE`.
        // Building `current` under Normal and comparing it against a Silenced computation was
        // comparing two different topics' text, not the same topic on two ticks.
        var current = Plan(mode: TelegramDeliveryModes.Silenced, existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(mode: TelegramDeliveryModes.Silenced, existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// THE BACKOFF APPLIES TOO. A repost is a delete plus a post — two calls where an edit was one —
    /// so a 429 answered at the tick rate costs double what it did before.
    /// </summary>
    [Fact]
    public void ARecentFailureHoldsTheRepostAsWell()
    {
        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), lastFailedAttemptAt: NOW.AddSeconds(-5)).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), lastFailedAttemptAt: NOW.AddSeconds(-BACKOFF)).Action);
    }

    /// <summary>
    /// The predicate on its own, at the three edges Plan cannot show as clearly. EQUAL ids are the
    /// subtle one: the newest message the app knows of IS the status line itself, which means nothing
    /// came after it.
    /// </summary>
    [Fact]
    public void TheRepostPredicateAtItsEdges()
    {
        var quiet = NOW.AddMinutes(-5);

        Assert.False(TopicStatusLine_Planner.Is_RepostDue(null, Newest(9999, quiet), NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, null, NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, Newest(STATUS_ID, quiet), NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
        Assert.True(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, Newest(STATUS_ID + 1, quiet), NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
    }

    /// <summary>
    /// A message stamped in the FUTURE is not a quiet topic. Both stamps are read off the same local
    /// clock, so this can only come from a clock step — and it must hold the repost rather than
    /// treat a negative elapsed as "long enough".
    /// </summary>
    [Fact]
    public void AMessageStampedInTheFutureDoesNotCountAsQuiet()
    {
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(
            STATUS_ID, Newest(STATUS_ID + 1, NOW.AddMinutes(5)), NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
    }

    /// <summary>
    /// A REPOST STILL HAS TO HAVE SOMETHING TO SEND. The repost overrides the decider, and the
    /// decider is where emptiness is refused — so overriding it without re-checking would hand the
    /// engine a delete followed by a sendMessage with an empty body, which Telegram rejects outright.
    /// The topic would lose the status line it had and get a 400 in exchange.
    ///
    /// THE ROUTE TO AN EMPTY REPOST IS NOW CLOSED BY CONSTRUCTION, which is what this pins instead
    /// of the old blank-title case. It used to be reached through a topic whose display name was
    /// blank with nothing else to report — the bare-title fallback was then a bare NOTHING. Since
    /// 2026-08-24 the opening field is the LITERAL word `PULSE` rather than the topic's name, and a
    /// repost needs an existing message id to be due at all, which is the same id that makes the
    /// builder fall back to that word. So the text handed to a repost can no longer be empty, and
    /// the emptiness guard in the planner is the belt behind these braces rather than the only one.
    ///
    /// Asserted on the TEXT as well as the action: "it reposts" alone would still be true of a
    /// planner that had gone back to sending nothing, and the body is the half Telegram rejects.
    /// </summary>
    [Fact]
    public void ARepostAlwaysHasSomethingToSend()
    {
        var plan = Plan(
            members: [],
            existingMessageId: STATUS_ID,
            newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)));

        Assert.Equal("PULSE", plan.Text);
        Assert.Equal(TopicStatusActions.Repost, plan.Action);
    }

    /// <summary>
    /// THE LATCH, rev-1 F1. A delete that is REFUSED rather than failed loops forever and starves the
    /// edit with it: `Is_MessageGone` matches none of the refusal wordings, so the id is never
    /// cleared, the delete throws before the send every time, and because the repost overrides the
    /// decider unconditionally the Edit never runs either.
    ///
    /// That is a REGRESSION, not a missing improvement, and this is the sentence that decides the
    /// design: before this branch a buried line at least stayed CURRENT. Un-latched it can now be
    /// buried AND stale, which is worse than the behaviour it replaced.
    ///
    /// The fix is NOT to call the message gone — rev-1 was right that "can't be deleted" is unsound
    /// for the identical reason "can't be edited" is excluded: the message still EXISTS, so clearing
    /// the id posts a second line beside an undeletable one, which is the two-lines-in-one-topic
    /// defect through a third door. Instead the topic stops trying to MOVE its line and keeps
    /// updating it in place — degrading to master's behaviour rather than to nothing.
    /// </summary>
    [Fact]
    public void ATopicWhereTheRepostIsImpossibleKeepsEditingInPlace()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), repostIsImpossible: true).Action);
    }

    /// <summary>
    /// And it falls back to what the DECIDER said, not to a blanket edit — the same rule the silenced
    /// topic follows. Nothing new to say is still silence, or a latched topic would rewrite identical
    /// text every tick for the rest of the app's life, which is a worse loop than the one being fixed.
    /// </summary>
    [Fact]
    public void ATopicWhereTheRepostIsImpossibleWithNothingNewToSayStaysSilent()
    {
        var current = Plan(existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), repostIsImpossible: true).Action);
    }

    /// <summary>
    /// The latch is PER TOPIC and nothing else changes: an unlatched topic in the same state still
    /// reposts. Asserted beside the two above so neither can pass because reposting broke generally.
    /// </summary>
    [Fact]
    public void TheLatchStopsOnlyTheTopicItWasSetFor()
    {
        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), repostIsImpossible: false).Action);
    }

    /// <summary>A buried line in a topic that has been quiet for exactly this many seconds.</summary>
    static TopicStatusLine_Planner.TopicStatusPlan Plan_AfterQuietSeconds(int quietSeconds)
    {
        return Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddSeconds(-quietSeconds)));
    }

    static TopicStatusLine_Planner.TopicNewestMessage Newest(long messageId, DateTime arrivedAt)
    {
        return new TopicStatusLine_Planner.TopicNewestMessage(messageId, arrivedAt);
    }

    static TopicStatusLine_Planner.TopicStatusPlan Plan(
        IReadOnlyList<ITopicStatusMember>? members = null,
        IPlanProgress? progress = null,
        long? existingMessageId = null,
        string? lastWrittenText = null,
        TelegramDeliveryModes mode = TelegramDeliveryModes.Normal,
        DateTime? lastFailedAttemptAt = null,
        TopicStatusLine_Planner.TopicNewestMessage? newestTopicMessage = null,
        bool repostIsImpossible = false)
    {
        return TopicStatusLine_Planner.Plan(
            progress,
            members ?? [Member("imp-1", "fix the parser", "2026-08-12 14:50")],
            NOW,
            existingMessageId,
            lastWrittenText,
            mode,
            lastFailedAttemptAt,
            BACKOFF,
            newestTopicMessage,
            repostIsImpossible);
    }

    /// <summary>
    /// THE OWNER'S OWN WORDS ARE NOT THE ORCHESTRATION'S LAST WORD (their call, 2026-08-19).
    ///
    /// A solo's "member channel" IS the owner channel, so this scan reaches the owner's inbound
    /// messages — and the bridge stamps every one of them with the subject "via Telegram". The owner
    /// read that back on their own topic as what the session last said.
    /// </summary>
    [Fact]
    public void TheOwnersOwnEntryIsNeverTheLastLine()
    {
        var solo = TopicStatusMember_Factory.Create(
            "solo-1",
            [
                Entry(1, ChannelAuthors.Solo, "2026-08-12 14:50", "fix landed — 1316 green"),
                Entry(2, ChannelAuthors.Owner, "2026-08-12 14:55", "via Telegram"),
            ],
            isClosed: false);

        Assert.Equal(
            "fix landed — 1316 green",
            TopicStatusLine_Planner.Pick_LastSubject_OrNull([solo], NOW));
    }

    /// <summary>The app was already excluded, and still is — this pins that the new filter kept it out.</summary>
    [Fact]
    public void TheAppsOwnEntryIsNeverTheLastLine()
    {
        var solo = TopicStatusMember_Factory.Create(
            "solo-1",
            [
                Entry(1, ChannelAuthors.Solo, "2026-08-12 14:50", "fix landed"),
                Entry(2, ChannelAuthors.App, "2026-08-12 14:55", "STATUS"),
            ],
            isClosed: false);

        Assert.Equal("fix landed", TopicStatusLine_Planner.Pick_LastSubject_OrNull([solo], NOW));
    }

    /// <summary>
    /// A SUPERVISOR IS NOT A MEMBER BUT IT IS A SESSION, and on a spoke channel its brief is very
    /// often the newest thing said. Filtering to Is_Member instead of Is_Session would have emptied
    /// this field for every orchestration between a brief and the implementer's first report.
    /// </summary>
    [Fact]
    public void ASupervisorsEntryStillCounts()
    {
        var imp = TopicStatusMember_Factory.Create(
            "imp-1",
            [
                Entry(1, ChannelAuthors.Implementer, "2026-08-12 14:50", "TASK 1 landed"),
                Entry(2, ChannelAuthors.Supervisor, "2026-08-12 14:55", "brief — TASK 2"),
            ],
            isClosed: false);

        Assert.Equal("brief — TASK 2", TopicStatusLine_Planner.Pick_LastSubject_OrNull([imp], NOW));
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string stamp, string subject)
    {
        return ChannelEntry_Factory.Create(
            index, author, stamp, subject, "body", $"## [{index}] FROM x — {stamp} — {subject}");
    }

    static ITopicStatusMember Member(string memberId, string briefSubject, string stamp)
    {
        return TopicStatusMember_Factory.Create(
            memberId,
            [ChannelEntry_Factory.Create(1, ChannelAuthors.Supervisor, stamp, briefSubject, "body", $"## [1] FROM supervisor — {stamp} — {briefSubject}\nbody")],
            isClosed: false);
    }
}
