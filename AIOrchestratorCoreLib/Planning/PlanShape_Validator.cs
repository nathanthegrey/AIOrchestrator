using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Catches ledger lines that CANNOT represent progress, at the moment they appear. A line like
/// "- [>] Remaining tasks 3-9 (gear, settings window, KB, PARTNERS #92)" collapses seven tasks
/// into one entry: from then on six real commits render as zero movement, however diligently the
/// ledger is updated. No amount of care fixes an unrepresentable line — only detection does.
/// </summary>
public static partial class PlanShape_Validator
{
    /// <summary>"tasks 3-9", "task 3 – 9", "items 4-11" — a range is many tasks wearing one checkbox.</summary>
    [GeneratedRegex(@"\b(tasks?|items?|steps?|points?)\s*\d+\s*[-–—]\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TaskRange_Regex();

    /// <summary>
    /// EVERY MARKER EXCEPT `- [-]`, which is left out on purpose and is not the drift it looks like.
    /// (`- [?]` was added 2026-08-19 and belongs here: a line blocked on the owner is still owed, so
    /// one that lumps several deliverables together still hides progress.) A not-doing line is excluded from the denominator and can never represent progress, so
    /// complaining that it lumps several tasks together would be pure noise about a line that costs
    /// nothing. Alerts the owner cannot act on are exactly what decision 15 removed from this feature.
    ///
    /// Said here because the identical four-marker regex in two OTHER files was genuine drift, found
    /// the same evening — `- [-]` was added to the parser additively and every copy of its list went
    /// stale. A reader sweeping for that pattern will arrive here next, and the answer is "correct as
    /// written".
    /// </summary>
    [GeneratedRegex(@"^\s*-\s*\[(x|X| |>|!|\?)\]\s*(.*)$", RegexOptions.Compiled)]
    private static partial Regex TaskLine_Regex();

    /// <summary>Beyond this many separators one line is plainly carrying a list of deliverables.</summary>
    const int MAX_SEPARATORS_PER_TASK = 3;

    /// <summary>Returns one complaint per offending line, empty when the ledger's shape is sound.</summary>
    public static IReadOnlyList<string> Find_UnrepresentableLines(string planText)
    {
        List<string> complaints = [];

        if (string.IsNullOrWhiteSpace(planText))
            return complaints;

        // WHICH SECTION WE ARE IN, tracked exactly as PlanLedger_Parser tracks it so the two agree
        // about where the ledger stops. This validator used to read the file flat, which is why the
        // failure below was invisible from here.
        var inNonLedgerSection = false;

        foreach (var rawLine in planText.Split('\n'))
        {
            var match = TaskLine_Regex().Match(rawLine.TrimEnd('\r'));

            if (PlanLedger_Sections.Is_Heading(rawLine))
                inNonLedgerSection = PlanLedger_Sections.Opens_NonLedgerSection(rawLine);

            if (!match.Success)
                continue;

            // REAL WORK STRANDED IN A PARKED SECTION — invisible to the bar, and silently so.
            //
            // THE PARSER IS RIGHT AND THIS IS NOT A BUG IN IT. A non-ledger section runs to the NEXT
            // heading, deliberately: truncating from the first PARKED heading would make a ledger's
            // meaning depend on where somebody pasted a section. What nothing ever said is that a
            // supervisor can append real ledger lines BELOW a parked heading without opening a new
            // one, and every one of them then vanishes from the owner's progress bar.
            //
            // ai-orchestrator-3 is the case that found it: `## Parked questions` is that file's LAST
            // heading, at line 253 of 550, and 82 task lines sit under it — 41 of them marked done
            // and carrying commit SHAs and suite counts ("guard close-implementer on the request
            // path — 623780d, 659 tests (+11)"). None reached the bar. strategy-lab-6 hides 26 the
            // same way, da-vinci-fintech-suite-17 hides 8.
            //
            // A DONE OR IN-PROGRESS MARKER IS THE TELL, and it is the whole rule. A genuinely parked
            // item is one nobody is working on — the seed text asks for plain bullets there, and
            // `- [ ]` is at worst harmless. `- [x]` inside a section whose entire purpose is to hold
            // what is NOT being worked on is a contradiction, and it is always the stranded case.
            if (inNonLedgerSection)
            {
                var parkedMarker = match.Groups[1].Value;

                if (parkedMarker is "x" or "X" or ">")
                {
                    var parkedText = match.Groups[2].Value.Trim();

                    if (parkedText.Length > 0)
                        complaints.Add($"'{Shorten(parkedText)}' is marked [{parkedMarker}] inside a PARKED / OWNER REQUESTS section, so the progress bar cannot see it — if it is real work, open a heading above it; if it is genuinely parked, it is not done.");
                }

                continue;
            }

            var taskText = match.Groups[2].Value.Trim();

            if (taskText.Length == 0)
                continue;

            if (TaskRange_Regex().IsMatch(taskText))
            {
                complaints.Add($"'{Shorten(taskText)}' covers a RANGE of tasks in one line — split it, one line per task, or its progress can never be shown.");
                continue;
            }

            var separators = taskText.Count(character => character == ',' || character == ';');

            if (separators >= MAX_SEPARATORS_PER_TASK)
                complaints.Add($"'{Shorten(taskText)}' lists {separators + 1} deliverables in one line — split it, one line per task.");
        }

        return complaints;
    }

    static string Shorten(string text)
    {
        const int MAX_QUOTED_CHARS = 60;

        return text.Length <= MAX_QUOTED_CHARS ? text : $"{text[..MAX_QUOTED_CHARS]}…";
    }
}
