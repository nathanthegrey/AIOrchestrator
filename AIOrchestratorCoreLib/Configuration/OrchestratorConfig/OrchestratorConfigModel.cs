using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Running.RunnerConfigs;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

internal sealed class OrchestratorConfigModel(
    IReadOnlyList<IRepoEntry> repos,
    string? supervisorModel,
    string? implementerModel,
    string? generalSupervisorModel,
    string? communicatorModel,
    long? telegramSupergroupChatId,
    long? telegramOwnerUserId,
    string? telegramBotToken,
    bool telegramItalianLayer,
    bool telegramStatusScreenshots,
    string? voiceTranscribeCommand,
    long? orchestrationTokenBudget,
    IRunnerConfigs runners,
    PlanBackendSettings? planBackend,
    IGuardrailSettings guardrails,
    IDefaultsSettings defaults) : IOrchestratorConfig
{
    public IReadOnlyList<IRepoEntry> Repos { get; } = repos;
    public string? SupervisorModel { get; } = supervisorModel;
    public string? ImplementerModel { get; } = implementerModel;
    public string? GeneralSupervisorModel { get; } = generalSupervisorModel;
    public string? CommunicatorModel { get; } = communicatorModel;
    public long? TelegramSupergroupChatId { get; } = telegramSupergroupChatId;
    public long? TelegramOwnerUserId { get; } = telegramOwnerUserId;
    public string? TelegramBotToken { get; } = telegramBotToken;
    public bool TelegramItalianLayer { get; } = telegramItalianLayer;
    public bool TelegramStatusScreenshots { get; } = telegramStatusScreenshots;
    public string? VoiceTranscribeCommand { get; } = voiceTranscribeCommand;
    public long? OrchestrationTokenBudget { get; } = orchestrationTokenBudget;
    public IRunnerConfigs Runners { get; } = runners;
    public PlanBackendSettings? PlanBackend { get; } = planBackend;
    public IGuardrailSettings Guardrails { get; } = guardrails;
    public IDefaultsSettings Defaults { get; } = defaults;

    public bool Is_TelegramConfigured()
    {
        return TelegramSupergroupChatId != null
            && TelegramOwnerUserId != null
            && !string.IsNullOrWhiteSpace(TelegramBotToken);
    }
}
