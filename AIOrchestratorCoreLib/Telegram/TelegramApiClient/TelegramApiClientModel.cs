using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

internal sealed class TelegramApiClientModel : ITelegramApiClient
{
    /// <summary>
    /// How many times a 429 is honoured before the failure is handed to the caller. Telegram answers
    /// a rate limit with a wait, not a refusal, so retrying is the correct reading — but it has to
    /// TERMINATE: an endpoint that answers 429 for ever would otherwise hold a mirror tick open
    /// indefinitely, which is the shape of every unbounded-retry defect this file has already paid
    /// for. Two attempts after the first cover a burst; a third means the limit is not a burst.
    /// </summary>
    const int RATE_LIMIT_RETRIES = 2;

    readonly HttpClient _httpClient;
    readonly string _botToken;
    readonly long _supergroupChatId;

    /// <summary>
    /// THE ONE PLACE THE OUTBOUND RATE LIMIT LIVES. Guarded by its own lock because sends come from
    /// both loops — the mirror tick and the inbound batch — and a bucket read-modify-written from
    /// two threads hands the same token out twice, which is the burst it exists to prevent.
    /// See <see cref="TokenBucket_Gate"/> for why a bucket and not a delay.
    /// </summary>
    readonly Lock _bucketLock = new();
    double _bucketTokens = TokenBucket_Gate.DEFAULT_CAPACITY;
    DateTime _bucketRefilledUtc = DateTime.UtcNow;

    public TelegramApiClientModel(string botToken, long supergroupChatId)
    {
        _botToken = botToken;
        _supergroupChatId = supergroupChatId;
        _httpClient = new HttpClient
        {
            // Must exceed the getUpdates long-poll timeout with margin.
            Timeout = TimeSpan.FromSeconds(90),
        };
    }

    public async Task<long> Create_ForumTopic_Async(string topicName, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["name"] = topicName,
        };

        var resultJson = await Post_Async("createForumTopic", payload, cancellationToken);

        var root = JsonNode.Parse(resultJson) as JsonObject
            ?? throw new Exception($"createForumTopic returned non-object JSON: {resultJson}");

        var threadIdNode = root["result"]?["message_thread_id"]
            ?? throw new Exception($"createForumTopic response has no result.message_thread_id: {resultJson}");

        return threadIdNode.GetValue<long>();
    }

    public async Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_thread_id"] = messageThreadId,
            ["name"] = newName,
        };

        await Post_Async("editForumTopic", payload, cancellationToken);
    }

    public async Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken)
    {
        // A DIFFERENT METHOD, not editForumTopic with a special id. Telegram's General topic has no
        // message_thread_id at all — that absence is how the whole app recognises it — so the only
        // way to rename it is the dedicated call.
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["name"] = newName,
        };

        await Post_Async("editGeneralForumTopic", payload, cancellationToken);
    }

    public async Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_thread_id"] = messageThreadId,
        };

        await Post_Async("deleteForumTopic", payload, cancellationToken);
    }

    public async Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken)
    {
        var unpinPayload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_thread_id"] = messageThreadId,
        };

        await Post_Async("unpinAllForumTopicMessages", unpinPayload, cancellationToken);

        try
        {
            // The service message's id equals the thread id; deleting it removes the header
            // entirely. Cosmetic — the unpin above is the actual fix, so failures are ignored.
            var deletePayload = new JsonObject
            {
                ["chat_id"] = _supergroupChatId,
                ["message_id"] = messageThreadId,
            };

            await Post_Async("deleteMessage", deletePayload, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Requires can_delete_messages; without it the unpinned service message just stays.
        }
    }

    public async Task<long?> Send_Message_Async(long? messageThreadId, string text, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = text,
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        var responseJson = await Post_Async("sendMessage", payload, cancellationToken, rateLimited: true);

        return Read_MessageId_OrNull(responseJson);
    }

    public async Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        return Read_MessageId_OrNull(await Post_Async("sendMessage", payload, cancellationToken, rateLimited: true));
    }

    public async Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
            ["text"] = text,
        };

        await Post_Async("editMessageText", payload, cancellationToken);
    }

    /// <summary>
    /// Rewrites a message AND the buttons under it in one call.
    ///
    /// The plain edit above is not a subset of this one — it sends no <c>reply_markup</c>, and
    /// Telegram reads that as "remove the keyboard". That is exactly what the question flow wants
    /// (recording a choice strips the buttons), and exactly what the hold receipt does NOT: its
    /// button has to CHANGE from ⏸ Wait to ▶ GO as the state changes. Two methods rather than an
    /// optional argument, so neither behaviour can be reached by accident.
    /// </summary>
    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        return Edit_MessageTextWithButtonRows_Async(messageId, text, Wrap_OneButtonPerRow(buttons), cancellationToken);
    }

    public async Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
            ["text"] = text,
            ["reply_markup"] = new JsonObject { ["inline_keyboard"] = Build_InlineKeyboard(buttonRows) },
        };

        await Post_Async("editMessageText", payload, cancellationToken);
    }

    /// <summary>
    /// One button per row — the shape every DECISION keyboard wants, because an option's label is a
    /// sentence and two of them side by side are each half-readable.
    /// </summary>
    static IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Wrap_OneButtonPerRow(IReadOnlyList<(string Data, string Label)> buttons)
    {
        return [.. buttons.Select(button => (IReadOnlyList<(string Data, string Label)>)[button])];
    }

    /// <summary>
    /// THE ONE PLACE the inline-keyboard markup is built. It was written out twice — once in the
    /// edit and once in the send — and a third copy was about to be added for the status line's
    /// command bar; two copies of a formatter is how they drift.
    /// </summary>
    static JsonArray Build_InlineKeyboard(IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows)
    {
        var keyboardRows = new JsonArray();

        foreach (var row in buttonRows)
        {
            var buttons = new JsonArray();

            foreach (var button in row)
            {
                buttons.Add(new JsonObject
                {
                    ["text"] = button.Label,
                    ["callback_data"] = button.Data,
                });
            }

            keyboardRows.Add(buttons);
        }

        return keyboardRows;
    }

    static long? Read_MessageId_OrNull(string responseJson)
    {
        try
        {
            var node = (JsonNode.Parse(responseJson) as JsonObject)?["result"]?["message_id"];

            if (node == null)
                return null;

            return node.GetValue<long>();
        }
        catch
        {
            return null;
        }
    }

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        return Send_MessageWithButtonRows_Async(messageThreadId, text, Wrap_OneButtonPerRow(buttons), cancellationToken);
    }

    public async Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = text,
            ["reply_markup"] = new JsonObject { ["inline_keyboard"] = Build_InlineKeyboard(buttonRows) },
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        // The id is needed later: on a tap this message is rewritten to show the chosen option.
        return Read_MessageId_OrNull(await Post_Async("sendMessage", payload, cancellationToken, rateLimited: true));
    }

    public async Task<long?> Send_MessageWithReplyKeyboard_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<string>> keyboardRows, CancellationToken cancellationToken)
    {
        var rows = new JsonArray();

        foreach (var row in keyboardRows)
        {
            var buttons = new JsonArray();

            foreach (var label in row)
                buttons.Add(new JsonObject { ["text"] = label });

            rows.Add(buttons);
        }

        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = text,
            // SILENT, because this message exists only to be deleted. Telegram has no way to install a
            // reply keyboard without sending something, so the caller sends a carrier and removes it
            // immediately — but the push notification fires when Telegram ACCEPTS the message, not when
            // the owner could have read it. The owner therefore got a notification, once per app
            // launch, for a message that was already gone by the time they opened the app: measured on
            // the owner's phone, 2026-09-06, during the Linux VPS install, where ten restarts in an
            // hour made a rare annoyance into a constant one. The bar itself is unaffected — it is
            // chat-level state, and silence changes only whether the carrier buzzes on its way past.
            ["disable_notification"] = true,
            ["reply_markup"] = new JsonObject
            {
                ["keyboard"] = rows,
                // is_persistent keeps the bar up instead of collapsing it behind the little keyboard
                // icon after one use, which is the whole point of asking for a PERMANENT bar.
                ["is_persistent"] = true,
                // Without this the bar renders at full standard-keyboard height — four buttons in a
                // half-screen slab, sitting on top of the conversation the owner is reading.
                ["resize_keyboard"] = true,
                ["selective"] = false,
            },
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        // The id comes back so the PREVIOUS installer message can be deleted on the next startup:
        // the keyboard is chat-level state that outlives its carrier message, but the carrier itself
        // is a message like any other and would otherwise pile up one per app launch.
        return Read_MessageId_OrNull(await Post_Async("sendMessage", payload, cancellationToken, rateLimited: true));
    }

    public async Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["callback_query_id"] = callbackQueryId,
            ["text"] = text,
        };

        await Post_Async("answerCallbackQuery", payload, cancellationToken);
    }

    public async Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
        };

        await Post_Async("editMessageReplyMarkup", payload, cancellationToken);
    }

    public async Task Delete_Message_Async(long messageId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
        };

        await Post_Async("deleteMessage", payload, cancellationToken);
    }

    public async Task Send_Photo_Async(long? messageThreadId, string filePath, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_supergroupChatId.ToString()), "chat_id");

        if (messageThreadId != null)
            form.Add(new StringContent(messageThreadId.Value.ToString()), "message_thread_id");

        var photoBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        form.Add(new ByteArrayContent(photoBytes), "photo", Path.GetFileName(filePath));

        // METERED LIKE ANY OTHER MESSAGE. sendPhoto builds its own multipart request and therefore
        // does not pass through Post_Async — which is exactly how a message-creating call ends up
        // outside the one place the rate limit lives. It creates a message in the group, so it
        // spends the group's allowance whether or not it shares that code path.
        await Wait_ForBucket_Async(cancellationToken);

        var response = await _httpClient.PostAsync(Build_MethodUrl("sendPhoto"), form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new TelegramApiException((int)response.StatusCode, $"Telegram 'sendPhoto' failed with HTTP {(int)response.StatusCode} for '{filePath}': {body}");
    }

    public async Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken)
    {
        // REGISTERED UNDER EVERY SCOPE, and the admin one is the whole reason (owner, 2026-08-24:
        // "add the native command menu / in every chat").
        //
        // Telegram resolves a user's command menu down a fallback chain, most specific first:
        // chat_member → chat_administrators → chat → all_chat_administrators → all_group_chats →
        // default. The owner is an ADMINISTRATOR of their own supergroup, so in the topics they
        // actually live in, `all_chat_administrators` is consulted BEFORE `all_group_chats` — and
        // the two scopes we used to set were the two that an admin reaches last, or not at all.
        //
        // The same list goes to all four on purpose: the chain stops at the first scope that carries
        // commands, so a scope set to a DIFFERENT (or empty) list does not merge with the ones below
        // it, it replaces them.
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, null), cancellationToken);
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, "all_private_chats"), cancellationToken);
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, "all_group_chats"), cancellationToken);
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, "all_chat_administrators"), cancellationToken);
    }

    /// <summary>
    /// THE `/` BUTTON IS A SEPARATE FACT FROM THE COMMANDS, and that is what made it intermittent.
    ///
    /// setMyCommands says WHAT the menu contains; the chat's menu button says WHETHER the client
    /// offers one. Left unset it is `default`, and each client then decides for itself — so the
    /// owner saw the `/` in some topics and not others, with the command list correct in all of
    /// them. Their report, 2026-08-25, of a screenshot pointing straight at it: *"This is not always
    /// present. Only sometimes. It's very convenient, can't you make it ALWAYS present?"*
    ///
    /// Set WITHOUT a chat_id on purpose: that writes the account-wide default for this bot, so it
    /// covers the supergroup, every topic inside it, and any private chat, and it does not have to
    /// be re-applied per topic as new orchestrations create them. A per-chat call would fix the
    /// topics that exist today and leave tomorrow's back on the client default.
    /// </summary>
    public async Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["menu_button"] = new JsonObject { ["type"] = "commands" },
        };

        await Post_Async("setChatMenuButton", payload, cancellationToken);
    }

    static JsonObject Build_CommandsPayload(IReadOnlyList<(string Command, string Description)> commands, string? scopeType)
    {
        var commandsArray = new JsonArray();

        foreach (var command in commands)
        {
            commandsArray.Add(new JsonObject
            {
                ["command"] = command.Command,
                ["description"] = command.Description,
            });
        }

        var payload = new JsonObject
        {
            ["commands"] = commandsArray,
        };

        if (scopeType != null)
            payload["scope"] = new JsonObject { ["type"] = scopeType };

        return payload;
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // allowed_updates = ["message","callback_query"] — without callback_query, inline-button
        // taps would never reach the bridge.
        var url = $"{Build_MethodUrl("getUpdates")}?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%2C%22callback_query%22%5D";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new TelegramApiException((int)response.StatusCode, $"getUpdates failed with HTTP {(int)response.StatusCode}: {body}");

        return body;
    }

    public async Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["file_id"] = fileId,
        };

        var resultJson = await Post_Async("getFile", payload, cancellationToken);

        var root = JsonNode.Parse(resultJson) as JsonObject
            ?? throw new Exception($"getFile returned non-object JSON: {resultJson}");

        var filePath = root["result"]?["file_path"]?.GetValue<string>()
            ?? throw new Exception($"getFile response has no result.file_path: {resultJson}");

        var downloadUrl = $"https://api.telegram.org/file/bot{_botToken}/{filePath}";
        var response = await _httpClient.GetAsync(downloadUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new TelegramApiException((int)response.StatusCode, $"Telegram file download failed with HTTP {(int)response.StatusCode} for file id '{fileId}'");

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Every Telegram call goes through here, which is what makes the rate limit expressible once.
    ///
    /// <para>
    /// <paramref name="rateLimited"/> is OPT-IN, and what opts in is exactly the set of calls that
    /// CREATE A MESSAGE IN THE GROUP — the thing Telegram's 20-a-minute ceiling actually counts.
    /// Metering everything was the first shape of this and it was wrong in both directions: it spent
    /// the message allowance on edits and deletes, which do not consume it, and it queued
    /// <c>answerCallbackQuery</c> behind the mirror's backlog — a call Telegram gives ten seconds
    /// before the button spinner hangs on the owner's phone. Edits and deletes have their own, far
    /// higher limits, and the 429 handling below is what covers them.
    /// </para>
    /// <para>
    /// A 429 IS HONOURED WITH TELEGRAM'S OWN NUMBER, not ours. The bucket is a guess at a limit we
    /// cannot observe; <c>retry_after</c> is the limit answering. Retries are bounded — see
    /// <see cref="RATE_LIMIT_RETRIES"/> — and a request that still fails is thrown exactly as
    /// before, so every caller's existing classification of the failure is unchanged.
    /// </para>
    /// </summary>
    async Task<string> Post_Async(string method, JsonObject payload, CancellationToken cancellationToken, bool rateLimited = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (rateLimited)
                await Wait_ForBucket_Async(cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(Build_MethodUrl(method), payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return body;

            var statusCode = (int)response.StatusCode;
            var retryAfterSeconds = Read_RetryAfterSeconds_OrNull(body);

            // RETRIED ONLY FOR THE CALLS THAT CREATE A MESSAGE, and only for a wait short enough to
            // spend inside a 2 s tick.
            //
            // Retrying everything cost far more than it bought. `editMessageText` and
            // `answerCallbackQuery` come through here unmetered, so a 429 carrying
            // `retry_after: 300` slept 300 s, retried, slept 300 s and then threw — ten minutes
            // inside a single mirror tick, holding its channel-write allowance, with nothing
            // mirrored and no deadline swept for the duration. And for a callback query it was
            // pointless as well as expensive: Telegram invalidates one after about ten seconds, so
            // the wait guaranteed the retry would fail while the owner's spinner hung.
            //
            // Past the inline cap the failure is handed to the caller, which is where the per-channel
            // backoff and the outcome classification already live.
            if (statusCode == 429 && rateLimited && attempt < RATE_LIMIT_RETRIES)
            {
                var wait = TokenBucket_Gate.Read_RetryAfter(retryAfterSeconds);

                // A 429 with no retry_after still has to cost something, or the retry is immediate
                // and lands on the same wall. One token's worth of time is the smallest honest wait.
                if (wait <= TimeSpan.Zero)
                    wait = TimeSpan.FromSeconds(TokenBucket_Gate.DEFAULT_REFILL_SECONDS / TokenBucket_Gate.DEFAULT_CAPACITY);

                if (wait <= TokenBucket_Gate.MAXIMUM_INLINE_RETRY_WAIT)
                {
                    await Task.Delay(wait, cancellationToken);
                    continue;
                }
            }

            throw new TelegramApiException(statusCode, $"Telegram '{method}' failed with HTTP {statusCode}: {body}", retryAfterSeconds);
        }
    }

    /// <summary>
    /// Blocks until the bucket has a token. Recomputed after each sleep rather than sleeping the
    /// whole predicted wait in one go: several senders race here, and the one that wakes first
    /// should take the token that actually became available.
    /// </summary>
    async Task Wait_ForBucket_Async(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;

            lock (_bucketLock)
            {
                var (tokens, refilledUtc, computedWait) = TokenBucket_Gate.Take(_bucketTokens, _bucketRefilledUtc, DateTime.UtcNow);
                _bucketTokens = tokens;
                _bucketRefilledUtc = refilledUtc;
                wait = computedWait;
            }

            if (wait <= TimeSpan.Zero)
                return;

            await Task.Delay(wait, cancellationToken);
        }
    }

    /// <summary>
    /// Telegram's <c>parameters.retry_after</c>, in seconds. Null for any body that does not carry
    /// one — including one that is not JSON at all, which a proxy or a gateway can produce.
    /// </summary>
    static int? Read_RetryAfterSeconds_OrNull(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject root)
                return null;

            return root["parameters"]?["retry_after"]?.GetValue<int>();
        }
        catch
        {
            // Broad by intent: a body that will not parse simply carries no advice.
            return null;
        }
    }

    string Build_MethodUrl(string method)
    {
        return $"https://api.telegram.org/bot{_botToken}/{method}";
    }
}
