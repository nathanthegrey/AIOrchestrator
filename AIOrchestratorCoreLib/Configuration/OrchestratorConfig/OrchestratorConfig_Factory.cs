using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Configuration.TelegramProseSettings;
using AIOrchestratorCoreLib.Running.RunnerConfigs;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

public static class OrchestratorConfig_Factory
{
    /// <summary>Owner's model ladder: routing = cheap, supervision and implementation = opus.</summary>
    public const string DEFAULT_GENERAL_SUPERVISOR_MODEL = "sonnet";
    public const string DEFAULT_SUPERVISOR_MODEL = "opus";
    public const string DEFAULT_IMPLEMENTER_MODEL = "opus";
    public const string DEFAULT_COMMUNICATOR_MODEL = "sonnet";

    /// <summary>
    /// Reviewing stays opus even after the implementer moves to sonnet (owner, 2026-09-09): a bad
    /// implementation gets found and fixed, a bad APPROVAL does not announce itself.
    /// </summary>
    public const string DEFAULT_REVIEWER_MODEL = "opus";

    /// <summary>
    /// A solo is supervisor, implementer and reviewer in one session with nobody above it, so it
    /// takes the supervision price rather than the implementation one.
    /// </summary>
    public const string DEFAULT_SOLO_MODEL = "opus";
    /// <summary>Opt-in: a screenshot raises a real window, so an absent key must read as OFF.</summary>
    public const bool DEFAULT_TELEGRAM_STATUS_SCREENSHOTS = false;

    public static IOrchestratorConfig Create(
        IReadOnlyList<IRepoEntry> repos,
        string? supervisorModel,
        string? implementerModel,

        // AFTER THE IMPLEMENTER AND BEFORE THE GENERAL, which is SessionRole_Names.ALL's own order —
        // and inserted positionally rather than appended, deliberately: every existing call site
        // fails to compile instead of silently binding a string to the wrong role. Measured while
        // writing this: all six of them do.
        string? reviewerModel,
        string? soloModel,
        string? generalSupervisorModel,
        string? communicatorModel,
        long? telegramSupergroupChatId,
        long? telegramOwnerUserId,
        string? telegramBotToken,
        bool? telegramStatusScreenshots,
        string? voiceTranscribeCommand,
        long? orchestrationTokenBudget,

        // OPTIONAL, AND ONLY THESE FOUR. Every other parameter is required because every caller
        // knows its value; these keys are hand-edited in config.json and no window has a field for
        // any of them, so the Settings window builds a config without them — and the loader, which is
        // the only reader that can have them, passes them explicitly. Save() never serialises any of
        // the four, so a config built without them cannot erase them from disk.
        PlanBackendSettings? planBackend = null,
        IGuardrailSettings? guardrails = null,
        IDefaultsSettings? defaults = null,
        ITelegramProseSettings? telegramProse = null)
    {
        return Create(
            repos, supervisorModel, implementerModel, reviewerModel, soloModel, generalSupervisorModel, communicatorModel,
            telegramSupergroupChatId, telegramOwnerUserId, telegramBotToken,
            telegramStatusScreenshots, voiceTranscribeCommand, orchestrationTokenBudget,
            RunnerConfigs_Factory.Create_Default(), planBackend, guardrails, defaults, telegramProse);
    }

    /// <summary>
    /// The full shape. Callers that rebuild a config from parts (the Settings window) MUST pass the
    /// runners through from the config they started from — the overload above defaults them, and a
    /// save through it would silently reset a hand-edited <c>runners</c> block to terminal.
    /// </summary>
    public static IOrchestratorConfig Create(
        IReadOnlyList<IRepoEntry> repos,
        string? supervisorModel,
        string? implementerModel,

        // AFTER THE IMPLEMENTER AND BEFORE THE GENERAL, which is SessionRole_Names.ALL's own order —
        // and inserted positionally rather than appended, deliberately: every existing call site
        // fails to compile instead of silently binding a string to the wrong role. Measured while
        // writing this: all six of them do.
        string? reviewerModel,
        string? soloModel,
        string? generalSupervisorModel,
        string? communicatorModel,
        long? telegramSupergroupChatId,
        long? telegramOwnerUserId,
        string? telegramBotToken,
        bool? telegramStatusScreenshots,
        string? voiceTranscribeCommand,
        long? orchestrationTokenBudget,
        IRunnerConfigs runners,
        PlanBackendSettings? planBackend = null,
        IGuardrailSettings? guardrails = null,
        IDefaultsSettings? defaults = null,
        ITelegramProseSettings? telegramProse = null)
    {
        return new OrchestratorConfigModel(
            repos,
            supervisorModel ?? DEFAULT_SUPERVISOR_MODEL,
            implementerModel ?? DEFAULT_IMPLEMENTER_MODEL,

            // THE LADDER IS THE COMPATIBILITY PROMISE, and it is resolved HERE so that
            // IOrchestratorConfig.ReviewerModel is the EFFECTIVE answer and no reader has to
            // remember the fallback. An absent reviewerModel means "what the reviewer got until
            // now", which was implementerModel — including the case that matters, an owner who had
            // set implementerModel by hand and never heard of this key. Only when neither is set
            // does the shipped default apply, and it is opus, which is what the reviewer already ran.
            reviewerModel ?? implementerModel ?? DEFAULT_REVIEWER_MODEL,
            soloModel ?? implementerModel ?? DEFAULT_SOLO_MODEL,
            generalSupervisorModel ?? DEFAULT_GENERAL_SUPERVISOR_MODEL,
            communicatorModel ?? DEFAULT_COMMUNICATOR_MODEL,
            telegramSupergroupChatId,
            telegramOwnerUserId,
            telegramBotToken,
            telegramStatusScreenshots ?? DEFAULT_TELEGRAM_STATUS_SCREENSHOTS,
            voiceTranscribeCommand,
            orchestrationTokenBudget,
            runners,
            planBackend,

            // DEFAULTED, NEVER NULL — and this is where it differs from planBackend above, whose null
            // IS its meaning. Every caller that predates this parameter, including the app's own
            // settings window, keeps compiling and keeps getting the guarded behaviour, which is the
            // only direction an optional guard is allowed to default in.
            guardrails ?? GuardrailSettings_Factory.Create_Default(),

            // SAME RULE AS THE GUARDRAILS ABOVE, and for the same reason: the block decides what an
            // orchestration started without an explicit shape becomes, so a caller that predates it
            // must get the owner's shipped answer rather than a null nobody downstream can read.
            defaults ?? DefaultsSettings_Factory.Create_Default(),

            // AND AGAIN THE SAME RULE. The block decides only the SHAPE of a long entry on the phone,
            // never whether it is delivered, so every caller that predates it gets the shipped
            // shaping rather than a null the mirror would have to test for at the send site.
            telegramProse ?? TelegramProseSettings_Factory.Create_Default());
    }

    public static IOrchestratorConfig Create_Empty()
    {
        return Create([], null, null, null, null, null, null, null, null, null, null, null, null);
    }

    /// <summary>
    /// The same config with only the status-screenshot flag changed — the /screenshots command
    /// flips it live from the phone, and it must not have to restate every other field just to move
    /// one bool.
    /// </summary>
    public static IOrchestratorConfig Create_WithStatusScreenshots(IOrchestratorConfig source, bool telegramStatusScreenshots)
    {
        return Create(
            source.Repos,
            source.SupervisorModel,
            source.ImplementerModel,
            source.ReviewerModel,
            source.SoloModel,
            source.GeneralSupervisorModel,
            source.CommunicatorModel,
            source.TelegramSupergroupChatId,
            source.TelegramOwnerUserId,
            source.TelegramBotToken,
            telegramStatusScreenshots,
            source.VoiceTranscribeCommand,
            source.OrchestrationTokenBudget,
            source.Runners,
            source.PlanBackend,
            source.Guardrails,
            source.Defaults,
            source.TelegramProse);
    }
}
