using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// What to do after an OWNER-FACING edit was refused — retry after Telegram's own wait, or give up.
///
/// <para>
/// WHY THIS EXISTS. Measured on the VPS, 2026-09-10 21:11:40: the owner tapped an option, the
/// rewrite of the tapped message ("✅ …", keyboard gone) got <c>429 retry after 24</c>, the
/// fallback that removes the keyboard got the same 429, and NEITHER was tried again — the phone kept
/// the old text and a live keyboard on a question already answered. The client's inline retry covers
/// only a <c>retry_after</c> of two seconds or less (a burst), and every 429 this system sees on an
/// edit carries 20–34 s. So the decision of whether to wait that long is the caller's, and it
/// depends on what the edit IS: a status line can wait for the next tick; a tap's rewrite is the one
/// thing the owner is looking at, and it deserves the wait (owner decision, 2026-09-10: up to about a
/// minute, in the background, then give up).
/// </para>
/// <para>
/// PURE, so the schedule can be tested without a clock or a network: the caller carries the attempt
/// number and sleeps for what this returns.
/// </para>
/// </summary>
public static class RateLimitedRetry_Policy
{
    /// <summary>Attempts in total, the first one included. Three covers a 429 that is a wave, not a wall.</summary>
    public const int MAX_ATTEMPTS = 3;

    /// <summary>
    /// The most one wait may last. Telegram's <c>retry_after</c> on an edit was measured at 20–34 s;
    /// a value above this is a wall, and a tap's rewrite is not worth parking a task on for minutes.
    /// Two waits at the cap plus the attempts themselves stay inside the owner's "about a minute".
    /// </summary>
    public const int MAX_WAIT_SECONDS = 30;

    /// <summary>A 429 with no <c>retry_after</c> still has to cost something, or the retry is immediate.</summary>
    public const int DEFAULT_WAIT_SECONDS = 5;

    /// <summary>
    /// How long to wait before attempt <paramref name="attemptsMade"/> + 1, or null to stop.
    /// Null for anything that is not a rate limit: a 400 will refuse the identical request again, a
    /// 5xx is not a wait but an outage, and neither is this policy's to retry.
    /// </summary>
    public static TimeSpan? Wait_BeforeNextAttempt_OrNull(Exception failure, int attemptsMade)
    {
        if (attemptsMade >= MAX_ATTEMPTS)
            return null;

        if (failure is not TelegramApiException { StatusCode: 429 } rateLimited)
            return null;

        var seconds = rateLimited.RetryAfterSeconds ?? DEFAULT_WAIT_SECONDS;

        if (seconds < 1)
            seconds = DEFAULT_WAIT_SECONDS;

        return TimeSpan.FromSeconds(Math.Min(seconds, MAX_WAIT_SECONDS));
    }
}
