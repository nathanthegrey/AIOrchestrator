using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Planning.PlanBackend;

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

        // OPTIONAL, AND ONLY THIS ONE. Every other parameter is required because every caller knows
        // its value; this key is hand-edited in config.json and no window has a field for it, so the
        // Settings window builds a config without one — and the loader, which is the only reader that
        // can have one, passes it explicitly. Save() never serialises the key, so a config built
        // without it cannot erase it from disk.
        PlanBackendSettings? planBackend = null)
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
            planBackend);
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
            source.PlanBackend);
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
            source.PlanBackend);
    }
}
