using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Configuration.TelegramProseSettings;
using AIOrchestratorCoreLib.Running.RunnerConfigs;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

/// <summary>
/// Orchestrator configuration, merged from config.json (non-secret) and secrets.json (bot token).
/// Telegram settings are optional: when absent the bridge runs file-only (no mirror, no remote input).
/// </summary>
public interface IOrchestratorConfig
{
    IReadOnlyList<IRepoEntry> Repos { get; }
    string? SupervisorModel { get; }
    string? ImplementerModel { get; }

    /// <summary>The general supervisor only routes "work on X" requests — a cheap model suffices (default: sonnet).</summary>
    string? GeneralSupervisorModel { get; }

    /// <summary>The per-orchestration press-secretary: narrates, never works — sonnet holds the boundary (default: sonnet).</summary>
    string? CommunicatorModel { get; }
    long? TelegramSupergroupChatId { get; }
    long? TelegramOwnerUserId { get; }
    string? TelegramBotToken { get; }

    /// <summary>
    /// When on, the half-hourly periodic status message carries a SCREENSHOT of the
    /// orchestration's supervisor/solo terminal, so the owner can read the session itself and not
    /// just a narration of it. Taking one raises and maximises a real window on the owner's desk,
    /// which is why it is opt-in: an absent key reads as OFF, and it is additionally suppressed
    /// (elsewhere) while the owner is at the PC, where the window would jump in front of them.
    /// </summary>
    bool TelegramStatusScreenshots { get; }

    /// <summary>
    /// External command that transcribes an owner voice note; {input} is replaced by the audio
    /// file path. Null = voice messages get a "not configured" reply. Example:
    /// "whisper {input} --model small --language it --output_format txt --output_dir -" style CLIs.
    /// </summary>
    string? VoiceTranscribeCommand { get; }

    /// <summary>
    /// Runaway guard: text the owner once an orchestration's lifetime tokens pass this ceiling.
    /// Null or 0 = no guard.
    /// </summary>
    long? OrchestrationTokenBudget { get; }

    /// <summary>
    /// How each role's sessions run (terminal window vs transient print turns) and the print
    /// dispatcher's limits — the <c>runners</c> / <c>printRunner</c> blocks. Absent blocks read as
    /// terminal everywhere, so an existing config.json changes nothing.
    /// </summary>
    IRunnerConfigs Runners { get; }

    /// <summary>
    /// Which plan backend this machine runs. Null — the ordinary case — means PLAN.md alone, exactly
    /// as before the seam existed. Hand-edited in config.json; no window writes it, which is why
    /// <see cref="OrchestratorConfig_Loader"/> never serialises the key back out.
    /// </summary>
    PlanBackendSettings? PlanBackend { get; }

    /// <summary>
    /// What makes an irreversible decision hard to take by accident, and what stops the dispatcher
    /// spending an allowance it is about to exhaust. Never null: an absent config.json yields the
    /// guarded defaults, because a guard nobody configured must still be a guard.
    /// </summary>
    IGuardrailSettings Guardrails { get; }

    /// <summary>
    /// The <c>defaults</c> block: what a request that does not say gets. Never null — an absent block
    /// means the shipped defaults, the same shape <see cref="Guardrails"/> has.
    /// </summary>
    IDefaultsSettings Defaults { get; }

    /// <summary>
    /// The <c>telegram</c> block: how a long owner-facing entry is SHAPED on the phone — folded above
    /// a length, attached as a file above a message count. Never null; an absent block means the
    /// shipped defaults, the same rule <see cref="Guardrails"/> and <see cref="Defaults"/> follow.
    /// </summary>
    ITelegramProseSettings TelegramProse { get; }

    bool Is_TelegramConfigured();
}
