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
    public const bool DEFAULT_TELEGRAM_ITALIAN_LAYER = true;

    /// <summary>Opt-in: a screenshot raises a real window, so an absent key must read as OFF.</summary>
    public const bool DEFAULT_TELEGRAM_STATUS_SCREENSHOTS = false;

    public static IOrchestratorConfig Create(
        IReadOnlyList<IRepoEntry> repos,
        string? supervisorModel,
        string? implementerModel,
        string? generalSupervisorModel,
        string? communicatorModel,
        long? telegramSupergroupChatId,
        long? telegramOwnerUserId,
        string? telegramBotToken,
        bool? telegramItalianLayer,
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
            repos, supervisorModel, implementerModel, generalSupervisorModel, communicatorModel,
            telegramSupergroupChatId, telegramOwnerUserId, telegramBotToken, telegramItalianLayer,
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
        string? generalSupervisorModel,
        string? communicatorModel,
        long? telegramSupergroupChatId,
        long? telegramOwnerUserId,
        string? telegramBotToken,
        bool? telegramItalianLayer,
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
            generalSupervisorModel ?? DEFAULT_GENERAL_SUPERVISOR_MODEL,
            communicatorModel ?? DEFAULT_COMMUNICATOR_MODEL,
            telegramSupergroupChatId,
            telegramOwnerUserId,
            telegramBotToken,
            telegramItalianLayer ?? DEFAULT_TELEGRAM_ITALIAN_LAYER,
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
        return Create([], null, null, null, null, null, null, null, null, null, null, null);
    }

    /// <summary>
    /// The same config with only the Italian layer changed — the app's status-bar toggle and the
    /// /italian command both flip it live, and neither should have to restate every other field.
    /// </summary>
    public static IOrchestratorConfig Create_WithItalianLayer(IOrchestratorConfig source, bool telegramItalianLayer)
    {
        return Create(
            source.Repos,
            source.SupervisorModel,
            source.ImplementerModel,
            source.GeneralSupervisorModel,
            source.CommunicatorModel,
            source.TelegramSupergroupChatId,
            source.TelegramOwnerUserId,
            source.TelegramBotToken,
            telegramItalianLayer,
            source.TelegramStatusScreenshots,
            source.VoiceTranscribeCommand,
            source.OrchestrationTokenBudget,
            source.Runners,
            source.PlanBackend,
            source.Guardrails,
            source.Defaults,
            source.TelegramProse);
    }

    /// <summary>
    /// The same config with only the status-screenshot flag changed — the /screenshots command
    /// flips it live from the phone, and like the Italian toggle it must not have to restate every
    /// other field just to move one bool.
    /// </summary>
    public static IOrchestratorConfig Create_WithStatusScreenshots(IOrchestratorConfig source, bool telegramStatusScreenshots)
    {
        return Create(
            source.Repos,
            source.SupervisorModel,
            source.ImplementerModel,
            source.GeneralSupervisorModel,
            source.CommunicatorModel,
            source.TelegramSupergroupChatId,
            source.TelegramOwnerUserId,
            source.TelegramBotToken,
            source.TelegramItalianLayer,
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
