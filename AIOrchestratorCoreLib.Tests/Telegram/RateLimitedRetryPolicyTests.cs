using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// AN OWNER-FACING EDIT WAITS FOR TELEGRAM'S OWN NUMBER, THEN TRIES AGAIN — bounded.
///
/// <para>
/// Measured 2026-09-10 21:11:40 on the VPS: the rewrite of a tapped question got
/// <c>429 retry after 24</c> and was never retried, so the phone kept a live keyboard on a question
/// already answered. The policy below is the schedule that rewrite now follows.
/// </para>
/// </summary>
public class RateLimitedRetryPolicyTests
{
    static TelegramApiException RateLimited(int? retryAfter) => new(429, "Too Many Requests", retryAfter);

    [Fact]
    public void ARateLimit_IsWaitedOut_ForTelegramsOwnNumber()
    {
        Assert.Equal(TimeSpan.FromSeconds(24), RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(24), attemptsMade: 1));
    }

    [Fact]
    public void AWaitAboveTheCap_IsClampedToIt()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(RateLimitedRetry_Policy.MAX_WAIT_SECONDS),
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(600), attemptsMade: 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void ARateLimitWithNoUsableNumber_StillCostsAWait(int? retryAfter)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(RateLimitedRetry_Policy.DEFAULT_WAIT_SECONDS),
            RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(retryAfter), attemptsMade: 1));
    }

    /// <summary>The last allowed attempt is the cap itself: after it, stop.</summary>
    [Fact]
    public void AfterTheLastAttempt_ItGivesUp()
    {
        Assert.NotNull(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(5), attemptsMade: RateLimitedRetry_Policy.MAX_ATTEMPTS - 1));
        Assert.Null(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(RateLimited(5), attemptsMade: RateLimitedRetry_Policy.MAX_ATTEMPTS));
    }

    /// <summary>A 400 refuses the identical request again; a 5xx is an outage; neither is a wait.</summary>
    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    [InlineData(502)]
    public void AnythingButARateLimit_IsNotRetried(int status)
    {
        Assert.Null(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(new TelegramApiException(status, "no", null), attemptsMade: 1));
        Assert.Null(RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(new InvalidOperationException("no"), attemptsMade: 1));
    }

    /// <summary>The owner's bound: "up to about a minute". Two waits at the cap plus the attempts stay inside it.</summary>
    [Fact]
    public void TheWholeSchedule_StaysInsideAboutAMinute()
    {
        var worstCase = (RateLimitedRetry_Policy.MAX_ATTEMPTS - 1) * RateLimitedRetry_Policy.MAX_WAIT_SECONDS;

        Assert.True(worstCase <= 60, $"worst case waits {worstCase} s");
    }
}
