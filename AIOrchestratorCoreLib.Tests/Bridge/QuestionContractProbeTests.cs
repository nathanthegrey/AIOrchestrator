using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Translation.MessageTranslator;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A question reaches the owner COMPLETE or not at all, carries its recommendation and its row, and
/// offers a way to talk that does not spend the decision.
///
/// <para>
/// Driven end to end through the real engine, because every one of these is a claim about what
/// arrives on a phone: a unit test of the contract cannot see that the body still went, that the
/// refusal never reached Telegram, or that a keyboard survived a tap.
/// </para>
/// </summary>
public class QuestionContractProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4646;

    const string COMPLETE_QUESTION =
        "Two plan levers changed and the build can start.\n"
        + "QUESTION: Start the FIN-D-277a build now?\n"
        + "OPTION: Start it\n"
        + "OPTION: Wait\n"
        + "RECOMMEND: Start it — the matrix values are numbers, not layout.\n"
        + "RISK: low\n"
        + "ROW: FIN-D-277a";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly CapturingTelegram_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);
    readonly IBridgeEngine _engine;

    public QuestionContractProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-question-contract-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            MessageTranslator_Factory.Create(_log), _engineState, _clock);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnIncompleteQuestion_IsNotForwarded_AndTheAgentIsToldEveryMissingLineAtOnce()
    {
        var orchId = await Start_Async();

        // The grammar this replaces accepted exactly this and derived a question from the prose.
        Append_Supervisor(orchId, "Ready to go.\nQUESTION: Start the build now?\nOPTION: Start it\nOPTION: Wait");

        Assert.True(
            await Run_Until_Async(() => Channel(orchId).Contains("was NOT sent to the owner", StringComparison.Ordinal), 20_000),
            $"the agent was never told.{Environment.NewLine}{_log.Dump()}");

        // The body still went: a formatting fault must never cost the owner a message.
        Assert.True(_telegram.Has_Sent_Containing("Ready to go"), _telegram.Dump_Sent());
        Assert.Null(_telegram.Find_ButtonFor("Start it"));

        // EVERY missing line, not the first — one refusal, one round trip.
        var channel = Channel(orchId);
        Assert.Contains("RECOMMEND:", channel, StringComparison.Ordinal);
        Assert.Contains("RISK:", channel, StringComparison.Ordinal);
        Assert.Contains("ROW:", channel, StringComparison.Ordinal);
        Assert.DoesNotContain("QUESTION: is missing", channel, StringComparison.Ordinal);

        // The refusal is for the agent, never for the phone.
        Assert.False(_telegram.Has_Sent_Containing("was NOT sent to the owner"), _telegram.Dump_Sent());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ACompleteQuestion_CarriesItsRecommendationAndItsRow_AndBothWaysOut()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var question = _telegram.Find_SentContaining("Start the FIN-D-277a build now?")
            ?? throw new Exception($"the question message was never sent.{Environment.NewLine}{_telegram.Dump_Sent()}");

        // The recommendation rides WITH the question, not in the body the owner scrolled past.
        Assert.Contains("💡", question, StringComparison.Ordinal);
        Assert.Contains("the matrix values are numbers", question, StringComparison.Ordinal);
        Assert.Contains("📎 FIN-D-277a", question, StringComparison.Ordinal);
        Assert.DoesNotContain("🔐", question, StringComparison.Ordinal);

        Assert.NotNull(_telegram.Find_ButtonFor(OwnerPush_Policy.TALK_LABEL));
        Assert.NotNull(_telegram.Find_ButtonFor(OwnerPush_Policy.MORE_DETAIL_LABEL));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ADeclaredHighRisk_LocksAQuestionWhoseWordsAreHarmless()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION.Replace("RISK: low", "RISK: high", StringComparison.Ordinal));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing("🔐"), 20_000),
            $"a declared high risk did not lock the question.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

        Assert.Contains("declared by the asker", _log.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task LetsTalk_KeepsTheQuestionOpen_AndATypedReplyDoesNotCloseIt_UntilATapDoes()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var talk = _telegram.Find_ButtonFor(OwnerPush_Policy.TALK_LABEL)
            ?? throw new Exception("the talk button never reached the phone");

        var questionMessageId = _telegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id");

        _telegram.Queue_Updates(Build_CallbackTapJson(talk, questionMessageId, updateId: 3010));

        Assert.True(
            await Run_Until_Async(() => Channel(orchId).Contains("wants to talk this decision through", StringComparison.Ordinal), 20_000),
            $"the supervisor never received the request.{Environment.NewLine}{_log.Dump()}");

        // STILL OPEN, and marked as under discussion — the whole point of the button.
        var afterTalk = Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);
        Assert.Equal(questionMessageId, afterTalk.MessageId);
        Assert.True(afterTalk.InDiscussion, "the question was not marked as under discussion");
        Assert.NotNull(_telegram.Find_ButtonFor("Start it"));

        // Their words while discussing are conversation, not a vote.
        _telegram.Queue_Updates(Build_OwnerMessageJson("what does waiting actually cost me", updateId: 3020, messageId: 88));

        Assert.True(
            await Run_Until_Async(() => Channel(orchId).Contains("what does waiting actually cost me", StringComparison.Ordinal), 20_000),
            $"the owner's message never reached the channel.{Environment.NewLine}{_log.Dump()}");

        Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);

        // And a real option still closes it.
        var startIt = _telegram.Find_ButtonFor("Start it")
            ?? throw new Exception("the option is gone");

        _telegram.Queue_Updates(Build_CallbackTapJson(startIt, questionMessageId, updateId: 3030));

        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().OpenQuestions.Count == 0, 20_000),
            $"the tap did not close the question.{Environment.NewLine}{_log.Dump()}");
    }

    string Channel(string orchId) => File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));

    async Task<string> Start_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        // The tailer baselines a channel it has never seen at its CURRENT length, so the engine has
        // to make one pass before anything is appended — otherwise the entry is absorbed as history.
        _telegram.Queue_Updates(Build_OwnerMessageJson("what is happening", updateId: 3001, messageId: 77));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 15_000),
            $"the engine never made its first pass.{Environment.NewLine}{_log.Dump()}");

        return session.OrchId;
    }

    void Append_Supervisor(string orchId, string body)
    {
        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [3] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a question\n{body}\n");
    }

    static string Build_OwnerMessageJson(string text, long updateId, long messageId)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    static string Build_CallbackTapJson(string callbackData, long questionMessageId, long updateId)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"callback_query\":{{\"id\":\"cbq-{updateId}\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{questionMessageId},\"message_thread_id\":{TOPIC_ID},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}";
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(100);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}
