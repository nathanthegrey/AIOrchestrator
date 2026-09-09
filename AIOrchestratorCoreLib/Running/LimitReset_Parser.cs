using System.Globalization;
using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// WHEN the usage window reopens, read out of the CLI's own refusal. Nothing more: no policy, no
/// clock of its own, no side effect — the dispatcher decides what to do with the instant.
///
/// <para>
/// MEASURED on the VPS 2026-09-08/09. A turn refused for quota comes back as a result row with
/// <c>is_error: true</c>, <c>api_error_status: 429</c> and a <c>result</c> of
/// <c>"You've hit your weekly limit · resets 5am (Europe/Berlin)"</c> (the session-limit variant
/// says <c>"resets 12am (Europe/Berlin)"</c>). The dispatcher used to treat that like any other
/// error — three attempts a minute apart, then a stall until new traffic arrived — and the
/// orchestrations sat dead for 167 to 509 minutes, two of them for a whole night. The sentence
/// already carries the answer; this reads it.
/// </para>
/// <para>
/// TOLERANT LIKE <see cref="Limits.LimitData_Parser"/>, and for the same reason: this text is a
/// human sentence from a CLI that changes, so a shape it has never emitted must read as NOTHING
/// rather than as a wrong instant. Every unparseable input answers null, and null means the
/// dispatcher keeps its old behaviour exactly — which is decision 21's rule that a guard which
/// cannot evaluate its predicate says so and allows.
/// </para>
/// <para>
/// TWO SIGNALS, EITHER ONE. The sentence must carry a <c>resets &lt;clock&gt;</c> clause, and then
/// either the status is 429 or the sentence itself names a limit. Both halves are needed because
/// both are droppable: <c>api_error_status</c> is nullable everywhere in this repo (a newer CLI may
/// stop sending it), and a 429 could arrive with wording nobody has seen. What the pair rules out is
/// a session's OWN answer being mistaken for a refusal — a failed turn whose report happens to say
/// "the cache resets 5am" must not be able to park itself until morning.
/// </para>
/// </summary>
public static class LimitReset_Parser
{
    /// <summary>The status the CLI reports for a quota refusal (measured 2026-09-08 on the VPS).</summary>
    public const int USAGE_LIMIT_STATUS = 429;

    /// <summary>
    /// <c>resets 5am</c>, <c>resets 12am</c>, <c>resets 3:30pm</c>, <c>resets 17:00</c>, each with an
    /// optional IANA zone in parentheses. The clock is validated after the match, not in the pattern:
    /// a regex that rejected "13pm" by shape would also have to know that "13:00" is fine, and the
    /// two rules read far more clearly as code.
    /// </summary>
    static readonly Regex RESETS_CLAUSE = new(
        @"resets\s+(?<hour>\d{1,2})(?::(?<minute>\d{1,2}))?\s*(?<meridiem>am|pm)?(?:\s*\((?<zone>[^)]{1,64})\))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    static readonly string[] LIMIT_WORDS = ["limit"];

    /// <summary>
    /// Whether this failure is a quota refusal AT ALL — the question the dispatcher asks before it
    /// complains that it could not read one. Deliberately looser than
    /// <see cref="Read_OrNull"/>: a refusal that names no clock still answers true here, because a
    /// limit the app cannot schedule around is exactly the thing that has to reach the log.
    ///
    /// <para>
    /// ONE IMPLEMENTATION, TWO CALLERS. <see cref="Read_OrNull"/> gates on this too, so "is this a
    /// limit" is answered in one place — the alternative was the dispatcher keeping its own copy of
    /// the same question, which CLAUDE.md decision 12 names as the way two answers start to differ.
    /// </para>
    /// </summary>
    public static bool Looks_LikeUsageLimit(string? resultText, int? apiErrorStatus)
    {
        if (apiErrorStatus == USAGE_LIMIT_STATUS)
            return true;

        return resultText != null && LIMIT_WORDS.Any(word => resultText.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The reset instant this text names, or null when it names none this parser is sure of.
    /// <paramref name="now"/> may be of any <see cref="DateTimeKind"/> — it is converted to UTC
    /// before use, because the dispatcher's clock is local and its state file is not.
    /// </summary>
    public static LimitResetReading? Read_OrNull(string? resultText, int? apiErrorStatus, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(resultText))
            return null;

        if (!Looks_LikeUsageLimit(resultText, apiErrorStatus))
            return null;

        Match match;

        try
        {
            match = RESETS_CLAUSE.Match(resultText);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological result text is not worth a turn: no reading, so the caller keeps its
            // old behaviour and logs which predicate it could not evaluate.
            return null;
        }

        if (!match.Success)
            return null;

        var clock = Read_Clock_OrNull(match);

        if (clock == null)
            return null;

        var zoneText = match.Groups["zone"].Success ? match.Groups["zone"].Value.Trim() : null;
        var zone = Find_Zone_OrNull(zoneText);

        return new LimitResetReading(
            Resolve_NextOccurrence(clock.Value.Hour, clock.Value.Minute, zone ?? TimeZoneInfo.Utc, now.ToUniversalTime()),
            match.Value.Trim(),
            zone == null ? zoneText ?? "UTC" : zoneText!,
            zone == null);
    }

    /// <summary>
    /// The hour and minute the clause names, or null when they are not a time of day. With a
    /// meridiem the hour is 1-12 (<c>12am</c> is midnight, <c>12pm</c> is noon); without one it is a
    /// 24-hour clock. "13pm" and "25" are the two shapes this rejects, and both are why the
    /// validation is here rather than in the pattern.
    /// </summary>
    static (int Hour, int Minute)? Read_Clock_OrNull(Match match)
    {
        if (!int.TryParse(match.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour))
            return null;

        var minute = 0;

        if (match.Groups["minute"].Success && !int.TryParse(match.Groups["minute"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minute))
            return null;

        if (minute is < 0 or > 59)
            return null;

        if (!match.Groups["meridiem"].Success)
            return hour is >= 0 and <= 23 ? (hour, minute) : null;

        if (hour is < 1 or > 12)
            return null;

        var pm = match.Groups["meridiem"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);

        return (pm ? hour == 12 ? 12 : hour + 12 : hour == 12 ? 0 : hour, minute);
    }

    /// <summary>
    /// The zone the text named, or null when it named none or named one this machine does not have.
    /// IANA ids resolve on every platform this app runs on (.NET carries ICU), but a zone database
    /// that has never heard of the id is a fact to report and fall back from, not to throw over —
    /// the caller then says in its log line that the instant was read in UTC instead.
    /// </summary>
    static TimeZoneInfo? Find_Zone_OrNull(string? zoneText)
    {
        if (string.IsNullOrWhiteSpace(zoneText))
            return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneText);
        }
        catch (Exception)
        {
            // TimeZoneNotFoundException, InvalidTimeZoneException and (on a locked-down host) a
            // security or IO failure reading the database — all of them mean the same thing here.
            return null;
        }
    }

    /// <summary>
    /// The next time that clock strikes in that zone, as a UTC instant. Today's if it is still to
    /// come (equal counts as still to come — a window that has just reopened is not a reason to wait
    /// another day), tomorrow's otherwise.
    ///
    /// <para>
    /// DST IS ANSWERED, NOT ASSUMED AWAY. A clock that the spring-forward skips does not exist, and
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> throws on it; the answer
    /// is the first instant after the gap, which is when the window is certainly open. An AMBIGUOUS
    /// clock (autumn, struck twice) resolves to the standard-time reading, the LATER of the two in
    /// UTC — late is a wait, early is a second refusal.
    /// </para>
    /// </summary>
    static DateTime Resolve_NextOccurrence(int hour, int minute, TimeZoneInfo zone, DateTime nowUtc)
    {
        var nowInZone = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var candidate = new DateTime(nowInZone.Year, nowInZone.Month, nowInZone.Day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(hour).AddMinutes(minute);

        if (candidate < nowInZone)
            candidate = candidate.AddDays(1);

        while (zone.IsInvalidTime(candidate))
            candidate = candidate.AddMinutes(1);

        return TimeZoneInfo.ConvertTimeToUtc(candidate, zone);
    }
}

/// <summary>
/// One reading of a usage-limit refusal: the instant, the words it was read from, and whether the
/// zone was the one the text asked for. All three are carried because the dispatcher's log line and
/// its channel entry must both quote the SOURCE — an instant with no provenance is the kind of
/// confident number this repo has been burnt by (CLAUDE.md decisions 12 and 18).
/// </summary>
/// <param name="ResetsAtUtc">When the window reopens. Always <see cref="DateTimeKind.Utc"/>.</param>
/// <param name="ResetsPhrase">The matched words, verbatim — e.g. <c>resets 5am (Europe/Berlin)</c>.</param>
/// <param name="ZoneId">The zone the text asked for, or <c>UTC</c> when it named none.</param>
/// <param name="ZoneAssumedUtc">True when <see cref="ResetsAtUtc"/> was computed in UTC because the named zone was absent or unknown here.</param>
public sealed record LimitResetReading(DateTime ResetsAtUtc, string ResetsPhrase, string ZoneId, bool ZoneAssumedUtc)
{
    /// <summary>
    /// Where the instant came from, in words — the words themselves, plus the zone caveat when there
    /// is one. ONE implementation, because the log line and the channel entry must say the same
    /// thing (CLAUDE.md decision 12: never add a second copy of a formatter), and the caveat is the
    /// half a reader needs most: an instant read in UTC because the named zone was unknown here can
    /// be hours off, and saying so is what makes that recoverable instead of mysterious.
    /// </summary>
    public string Describe_Source()
    {
        if (!ZoneAssumedUtc)
            return $"'{ResetsPhrase}'";

        return ZoneId == "UTC"
            ? $"'{ResetsPhrase}' — it named no zone, so the clock was read in UTC"
            : $"'{ResetsPhrase}' — the zone '{ZoneId}' is unknown on this machine, so the clock was read in UTC";
    }
}
