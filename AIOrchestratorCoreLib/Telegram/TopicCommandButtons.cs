using System.Globalization;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The owner's frequently-used commands, as permanent tappable buttons on a topic's status line.
///
/// They render as an INLINE keyboard hanging off that message — one tap sends a callback_data
/// payload back to the app, leaving no message in the chat. The set lives HERE, once, and both the
/// rendering and the parser derive from <see cref="BUTTONS"/>, so adding a sixth command is one
/// line in one array and cannot leave a button that renders and does nothing.
///
/// THERE WAS A SECOND RENDERING, AND IT WAS REMOVED ON 2026-09-06: a persistent REPLY keyboard, the
/// bar of literal slash commands above the input box, installed by sending a carrier message and
/// deleting it again. IT NEVER WORKED. Telegram anchors a reply keyboard to the message that
/// delivered it, so deleting that message takes the bar with it — measured on the owner's phone,
/// send then delete, bar appears then vanishes on the same chat. The bar had only ever been SEEN
/// when the process was killed between the send and the delete, leaving the carrier alive by
/// accident; in normal operation the feature spent every launch sending a message, buzzing the
/// owner's phone, and ending with the chat exactly as it started. Keeping it working would have
/// cost a permanent junk line in General, for five commands already one tap away here and listed
/// in the "/" menu besides. Do not reintroduce it without re-measuring that premise first.
///
/// Callback data is prefixed "cmd:" so a tap cannot be confused with the other button families
/// already flying around this bridge: "hold:"/"go:" (<see cref="HoldButton_Data"/>),
/// "close-yes-{guid}"/"close-no-{guid}" (the close confirmation), and "opt-{n}" (the single-use
/// option registry). <see cref="Parse_OrNull"/> returns null for every one of those, because a tap
/// this class cannot read must fall THROUGH to the next handler untouched. Swallowing it as a
/// malformed command would eat the owner's answer to a question — the one tap in this system that
/// cannot be repeated.
///
/// Like the hold toggle, and unlike the question options, these buttons are deliberately NOT
/// registered in the single-use registry. They are permanent furniture: the owner presses /show a
/// dozen times across a session, and expiring the button after the first press would leave dead
/// furniture sitting exactly where they were told to press.
/// </summary>
public static class TopicCommandButtons
{
    /// <summary>
    /// What marks a payload as ours. Distinct from "hold:", "go:", "close-yes-", "close-no-" and
    /// "opt-", and not a prefix of any of them — so the parse ORDER in the tap handler cannot
    /// decide the meaning of a payload.
    /// </summary>
    const string PREFIX = "cmd:";

    /// <summary>Splits the verb from the topic inside the payload body.</summary>
    const char FIELD_SEPARATOR = ':';

    /// <summary>
    /// THE source of truth. Display order is the order the owner reaches for them: look at it,
    /// land it, mark it for testing, photograph it.
    ///
    /// Every label is emoji-then-slash-command: the emoji is what the eye finds on a crowded
    /// screen, the slash command is what makes the button's EFFECT unambiguous — a picture alone
    /// leaves the owner guessing which of two similar glyphs merges and which closes.
    /// 🧪 for /test is not a free choice: it is the same glyph the topic's own status line uses for
    /// the awaiting-testing state, so the button and the state it toggles read as one thing.
    /// </summary>
    static readonly (string Command, string Label)[] BUTTONS =
    [
        // /screen LEADS, on the owner's call: "the /screen command is crucial, it should be among
        // the main commands always available" (2026-08-24). It was already in every surface it
        // could be in — but it sat last, in the second row of a two-by-two, which is the least
        // reachable of the four on a phone.
        ("screen", "📸 /screen"),
        ("show",   "👁 /show"),
        ("merge",  "🔀 /merge"),
        ("test",   "🧪 /test"),
        // /refresh EARNS A STANDING BUTTON on the owner's call, 2026-08-25: *"It happens so often
        // that the question mark gets stuck that this command should be one of the main command
        // buttons always present."* It is the only button here that repairs the topic LIST rather
        // than acting on the work, and it is wanted precisely at the moment the owner cannot trust
        // what the list is telling them.
        ("refresh", "♻ /refresh"),
    ];

    /// <summary>The commands offered, in display order: "show", "merge", "test", "screen".</summary>
    public static IReadOnlyList<string> Commands { get; } = BUTTONS.Select(button => button.Command).ToArray();

    /// <summary>
    /// Membership test for the parser. Ordinal and case-SENSITIVE: a payload we did not build is
    /// not ours, and quietly accepting "cmd:SHOW:5" would mean accepting whatever else invented it.
    /// </summary>
    static readonly HashSet<string> KNOWN_COMMANDS = new(BUTTONS.Select(button => button.Command), StringComparer.Ordinal);

    /// <summary>
    /// Inline-keyboard buttons for one topic: (callback_data, label), in <see cref="Commands"/>
    /// order.
    ///
    /// The payload carries the TOPIC because a tap arrives with no text to infer one from, and this
    /// app's entire routing is per topic. 0 is the General topic, which has no thread id — the same
    /// convention the hold button and the receipt registry use, so all three agree on what "no
    /// topic" is keyed as.
    ///
    /// Telegram caps callback_data at 64 BYTES and rejects an over-long one at SEND time, on the
    /// phone, where nothing here can see it. The worst case is "cmd:screen:" plus a 20-character
    /// long — 31 bytes — so the cap is not close, but it is pinned by a test because a future
    /// longer verb is exactly the change that would sail past review.
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForTopic(long messageThreadId)
    {
        var topic = messageThreadId.ToString(CultureInfo.InvariantCulture);

        return BUTTONS
            .Select(button => ($"{PREFIX}{button.Command}{FIELD_SEPARATOR}{topic}", button.Label))
            .ToArray();
    }

    /// <summary>
    /// Null when the data is not ours, so the caller falls through to the next handler.
    ///
    /// The thread id is carried back VERBATIM, including negative values. Telegram's real thread ids
    /// are positive, so a negative one can only come from something that is not this app — but
    /// rejecting it here would be the wrong lie: null means "not ours, try the next handler", and
    /// every other handler will not recognise it either, so the owner taps and NOTHING happens with
    /// nothing logged. Handing the router a payload that is plainly ours, with the id it actually
    /// contains, lets the one component that holds the topic roster say so out loud. Deciding
    /// whether a topic exists was never this parser's job; its job is the shape.
    ///
    /// The parameter is nullable although the contract writes it plain — a tap payload arrives from
    /// the wire, and a parser whose whole purpose is "return null for anything unexpected" must not
    /// be the thing that throws on the most ordinary unexpected value there is.
    /// </summary>
    public static (string Command, long MessageThreadId)? Parse_OrNull(string? callbackData)
    {
        if (string.IsNullOrWhiteSpace(callbackData))
            return null;

        if (!callbackData.StartsWith(PREFIX, StringComparison.Ordinal))
            return null;

        var body = callbackData[PREFIX.Length..];

        var separator = body.IndexOf(FIELD_SEPARATOR);

        // -1 is "cmd:show" — a verb with no topic. 0 is "cmd::5" — a topic with no verb. Neither is
        // something Build_ForTopic can produce, so neither gets a benefit of the doubt.
        if (separator <= 0)
            return null;

        var command = body[..separator];

        if (!KNOWN_COMMANDS.Contains(command))
            return null;

        // AllowLeadingSign and nothing else: the default NumberStyles.Integer would also swallow
        // surrounding whitespace, and "cmd:show: 5" is not a payload this class ever wrote.
        if (!long.TryParse(body[(separator + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var messageThreadId))
            return null;

        return (command, messageThreadId);
    }
}
