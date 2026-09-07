using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// THE ONE WAY AGENT PROSE LEAVES FOR TELEGRAM: rendered by
/// <see cref="TelegramHtml_Renderer"/>, sent as HTML, and — if Telegram refuses to parse it —
/// re-sent once as the plain Markdown the owner was reading before this renderer existed.
///
/// <para>
/// THE FALLBACK IS THE POINT, not a nicety. This whole system exists so an owner-facing message is
/// never lost, and a rendering bug is a new way to lose one: a malformed entity is a 400, the send
/// throws, <c>Mirror_Append_Async</c> returns false, and the entry is retried into the same
/// rejection for ever. One escaped bracket in one agent's message would silently wedge that
/// channel. So the refusal costs the formatting, never the message.
/// </para>
/// <para>
/// IT FIRES ONLY ON A 400, and that is the rule this file's neighbours already state: A FALLBACK IS
/// FOR "THAT CALL FAILED", NOT FOR "THE ENDPOINT IS UNREACHABLE". A 429, a 5xx or an
/// <c>HttpClient</c> timeout means the outcome is UNKNOWN — a second live call there can post the
/// message twice and spends another ~90 s inside a 2 s tick. Those propagate untouched to the
/// caller's own retry, exactly as they did before. Cancellation is never a
/// <see cref="TelegramApiException"/>, so it propagates too.
/// </para>
/// </summary>
public static class TelegramProse_Sender
{
    public static async Task<long?> Send_Async(
        ITelegramApiClient client,
        IOrchestrationLog log,
        string orchId,
        long? messageThreadId,
        string markdown,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.Send_HtmlMessage_Async(messageThreadId, TelegramHtml_Renderer.Render(markdown), cancellationToken);
        }
        catch (TelegramApiException ex) when (Is_ParseRefusal(ex))
        {
            log.Log_Warning(orchId, Describe_Refusal("sendMessage", markdown, ex));

            return await client.Send_Message_Async(messageThreadId, markdown, cancellationToken);
        }
    }

    public static async Task<long?> Send_WithButtons_Async(
        ITelegramApiClient client,
        IOrchestrationLog log,
        string orchId,
        long? messageThreadId,
        string markdown,
        IReadOnlyList<(string Data, string Label)> buttons,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.Send_HtmlMessageWithButtons_Async(
                messageThreadId, TelegramHtml_Renderer.Render(markdown), buttons, cancellationToken);
        }
        catch (TelegramApiException ex) when (Is_ParseRefusal(ex))
        {
            log.Log_Warning(orchId, Describe_Refusal("sendMessage", markdown, ex));

            // THE BUTTONS COME BACK TOO. Dropping to the plain send would strip the keyboard, which
            // on a decision message is the entire message: the owner would be shown a question with
            // no way to answer it.
            return await client.Send_MessageWithButtons_Async(messageThreadId, markdown, buttons, cancellationToken);
        }
    }

    public static async Task Edit_Async(
        ITelegramApiClient client,
        IOrchestrationLog log,
        string orchId,
        long messageId,
        string markdown,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Edit_HtmlMessageText_Async(messageId, TelegramHtml_Renderer.Render(markdown), cancellationToken);
        }
        catch (TelegramApiException ex) when (Is_ParseRefusal(ex))
        {
            log.Log_Warning(orchId, Describe_Refusal("editMessageText", markdown, ex));

            await client.Edit_MessageText_Async(messageId, markdown, cancellationToken);
        }
    }

    /// <summary>
    /// A 400 is Telegram REFUSING the request on its merits and it will refuse the identical
    /// request again — which is what makes one retry with different content the right move, and
    /// what makes retrying a 429 or a 5xx the wrong one. Read from
    /// <see cref="TelegramApiException.StatusCode"/>, never from the English message: that type
    /// exists precisely so a caller does not have to parse a sentence to learn a status.
    /// </summary>
    static bool Is_ParseRefusal(TelegramApiException ex)
    {
        return ex.StatusCode == 400;
    }

    /// <summary>
    /// NAMES THE FAILURE, not just its existence. Telegram's own body is carried in the message
    /// ("can't parse entities: ..." with the byte offset), and that offset is the only thing that
    /// makes a renderer bug diagnosable after the fact. The length of the text goes with it because
    /// a refusal at a chunk boundary looks different from one in a short message.
    /// </summary>
    static string Describe_Refusal(string method, string markdown, TelegramApiException ex)
    {
        return $"Telegram refused the HTML '{method}' ({markdown.Length} chars) — resending as plain text, so the message arrives with its Markdown markers literal: {ex.Message}";
    }
}
