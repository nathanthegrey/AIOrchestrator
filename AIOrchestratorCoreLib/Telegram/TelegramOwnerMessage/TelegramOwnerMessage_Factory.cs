namespace AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

public static class TelegramOwnerMessage_Factory
{
    public static ITelegramOwnerMessage Create(
        long updateId,
        long? messageId,
        long chatId,
        long fromUserId,
        long? messageThreadId,
        string text,
        string? photoFileId,
        string? voiceFileId,

        // Trailing and optional: every existing caller predates replies and means "not a reply".
        string? replyToText = null,

        // Trailing and optional for the same reason, and defaulting the SAFE way round: the parser
        // builds genuinely typed messages, and a new call site that forgets this argument gets the
        // binding behaviour that has always applied to typed text. Only the three app-composed
        // sites pass true, and they are named in ITelegramOwnerMessage.IsAppComposed.
        bool isAppComposed = false)
    {
        return new TelegramOwnerMessageModel(
            updateId, messageId, chatId, fromUserId, messageThreadId, text, photoFileId, voiceFileId, replyToText, isAppComposed);
    }
}
