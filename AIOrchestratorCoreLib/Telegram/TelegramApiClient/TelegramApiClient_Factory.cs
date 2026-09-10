using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;

namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

public static class TelegramApiClient_Factory
{
    /// <summary>
    /// <paramref name="budget"/> is handed in rather than built here because the SEND allowance
    /// outlives the client: it is restored from <c>.bridge-state.json</c> at start and written back
    /// on every tick, so the caller that owns that file owns the object (brief F5).
    /// </summary>
    public static ITelegramApiClient Create(string botToken, long supergroupChatId, ITelegramSendBudget budget)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            throw new ArgumentException($"Bot token must be non-empty (supergroupChatId was {supergroupChatId})");

        return new TelegramApiClientModel(botToken, supergroupChatId, budget);
    }
}
