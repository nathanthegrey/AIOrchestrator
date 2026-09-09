using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The usage-limit refusal, read for the one thing the dispatcher needs from it: WHEN the window
/// reopens. The two texts pinned here are the ones MEASURED on the VPS on 2026-09-08/09 —
/// "You've hit your weekly limit · resets 5am (Europe/Berlin)" and the session-limit variant with
/// "12am" — and the rest are the shapes the same sentence can obviously take (a half hour, a
/// 24-hour clock, no zone, an unknown zone) plus the garbage that must read as nothing at all.
/// </summary>
public class LimitResetParserTests
{
    const string WEEKLY = "You've hit your weekly limit · resets 5am (Europe/Berlin)";
    const string SESSION = "You've hit your session limit · resets 12am (Europe/Berlin)";

    static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void TheMeasuredWeeklyRefusal_ResolvesFiveAmBerlinToThreeAmUtc_WhenTheHourIsStillAhead()
    {
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
        Assert.Equal("Europe/Berlin", reading.ZoneId);
        Assert.False(reading.ZoneAssumedUtc);
        Assert.Equal("resets 5am (Europe/Berlin)", reading.ResetsPhrase);
    }

    [Fact]
    public void AnHourAlreadyPastInThatZone_IsTomorrows()
    {
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 10, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 10, 3, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void TheZoneIsReadFromTheText_NotAssumedFromSummer_SoTheSameFiveAmIsFourAmUtcInWinter()
    {
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 1, 15, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 1, 15, 4, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void TheMeasuredSessionRefusal_ReadsTwelveAmAsMidnight_NotAsNoon()
    {
        // Midnight BERLIN on the 10th is 22:00 UTC on the 9th — the date rolls in the zone, not in UTC.
        var reading = LimitReset_Parser.Read_OrNull(SESSION, 429, Utc(2026, 9, 9, 10, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 22, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void TwelvePm_IsNoon()
    {
        var reading = LimitReset_Parser.Read_OrNull("You've hit your weekly limit · resets 12pm (Europe/Berlin)", 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 10, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void AHalfHour_IsKept()
    {
        var reading = LimitReset_Parser.Read_OrNull("You've hit your weekly limit · resets 3:30pm (Europe/Berlin)", 429, Utc(2026, 9, 9, 10, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 13, 30), reading.ResetsAtUtc);
    }

    [Fact]
    public void FivePm_WithNoZone_IsReadInUtcAndSaysSo()
    {
        var reading = LimitReset_Parser.Read_OrNull("You've hit your weekly limit · resets 5pm", 429, Utc(2026, 9, 9, 10, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 17, 0), reading.ResetsAtUtc);
        Assert.True(reading.ZoneAssumedUtc);
        Assert.Equal("UTC", reading.ZoneId);
        Assert.Equal("resets 5pm", reading.ResetsPhrase);
    }

    [Fact]
    public void AZoneThisMachineDoesNotKnow_FallsBackToUtcAndSaysWhichZoneWasAskedFor()
    {
        var reading = LimitReset_Parser.Read_OrNull("You've hit your weekly limit · resets 5am (Mars/Olympus)", 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 5, 0), reading.ResetsAtUtc);
        Assert.True(reading.ZoneAssumedUtc);
        Assert.Equal("Mars/Olympus", reading.ZoneId);
    }

    [Fact]
    public void ATwentyFourHourClock_IsToleratedWithoutAnAmPm()
    {
        var reading = LimitReset_Parser.Read_OrNull("You've hit your weekly limit · resets 17:00 (Europe/Berlin)", 429, Utc(2026, 9, 9, 10, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 15, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void TheCaseOfTheSentence_DoesNotMatter()
    {
        var reading = LimitReset_Parser.Read_OrNull("You've hit your WEEKLY LIMIT · Resets 5AM (Europe/Berlin)", 429, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void AnExactHit_OnTheResetInstant_IsTodaysAndNotTomorrows()
    {
        // 03:00 UTC IS 5am Berlin: the window has just reopened, so the answer is now — pushing it a
        // day out here would cost 24 hours for being one tick early.
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 3, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DONE\n\nThe parser is written and the suite is green.")]
    [InlineData("Error: connection reset by peer")]
    [InlineData("You've hit your weekly limit")]
    [InlineData("You've hit your weekly limit · resets soon")]
    [InlineData("You've hit your weekly limit · resets 13pm (Europe/Berlin)")]
    [InlineData("You've hit your weekly limit · resets 25 (Europe/Berlin)")]
    [InlineData("You've hit your weekly limit · resets 5:70am (Europe/Berlin)")]
    [InlineData("{\"is_error\":true,")]
    public void GarbageAndSilence_ReadAsNothing(string? resultText)
    {
        Assert.Null(LimitReset_Parser.Read_OrNull(resultText, 429, Utc(2026, 9, 9, 10, 0)));
    }

    [Fact]
    public void AResetsClauseInAnAnswerThatNamesNoLimit_IsNotAUsageLimit_UnlessTheStatusSaysSo()
    {
        const string prose = "REPORT\n\nThe cache resets 5am (Europe/Berlin), which is why the test flaked.";

        Assert.Null(LimitReset_Parser.Read_OrNull(prose, null, Utc(2026, 9, 9, 1, 0)));
        Assert.Null(LimitReset_Parser.Read_OrNull(prose, 529, Utc(2026, 9, 9, 1, 0)));
        Assert.NotNull(LimitReset_Parser.Read_OrNull(prose, 429, Utc(2026, 9, 9, 1, 0)));
    }

    [Fact]
    public void TheLimitWordAlone_IsEnough_WhenTheStatusIsMissing()
    {
        // MEASURED SHAPE, MISSING FIELD. api_error_status is nullable everywhere in this repo because a
        // newer CLI may stop sending it; the sentence is the stronger signal and must stand on its own.
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, null, Utc(2026, 9, 9, 1, 0));

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
    }

    [Fact]
    public void ANonUtcNow_IsAccepted_AndAnsweredInUtc()
    {
        // The dispatcher hands it DateTime.UtcNow, but the tracker's clock is local — a reading that
        // silently treated an unspecified stamp as UTC would be wrong by the machine's offset.
        var reading = LimitReset_Parser.Read_OrNull(WEEKLY, 429, Utc(2026, 9, 9, 1, 0).ToLocalTime());

        Assert.NotNull(reading);
        Assert.Equal(Utc(2026, 9, 9, 3, 0), reading.ResetsAtUtc);
        Assert.Equal(DateTimeKind.Utc, reading.ResetsAtUtc.Kind);
    }
}
