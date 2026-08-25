using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// The exact line that broke a live orchestration: seven tasks behind one checkbox, so six real
/// commits rendered as zero movement. Detection is the only fix — no amount of diligence can make
/// an unrepresentable line representable.
/// </summary>
public class PlanShapeValidatorTests
{
    [Fact]
    public void Find_TheLiveFailure_ARangeOfTasksInOneLine()
    {
        var complaints = PlanShape_Validator.Find_UnrepresentableLines(
            "- [>] Remaining tasks 3-9 (gear, settings window, KB, PARTNERS #92)");

        Assert.Single(complaints);
        Assert.Contains("RANGE", Assert.Single(complaints));
    }

    [Theory]
    [InlineData("- [ ] tasks 4-11 of the rebuild")]
    [InlineData("- [x] items 2 – 5")]
    [InlineData("- [>] steps 1—3 done together")]
    public void Find_RangeVariants(string line)
    {
        Assert.Single(PlanShape_Validator.Find_UnrepresentableLines(line));
    }

    [Fact]
    public void Find_ALineListingManyDeliverables()
    {
        var complaints = PlanShape_Validator.Find_UnrepresentableLines(
            "- [ ] wire the gear icon, the settings window, the KB page, and the partners note");

        Assert.Contains("deliverables", Assert.Single(complaints));
    }

    [Fact]
    public void Find_HealthyLedger_ComplainsAboutNothing()
    {
        var complaints = PlanShape_Validator.Find_UnrepresentableLines("""
            # PLAN — rebuild

            - [x] 1. map the legacy behaviour
            - [>] 2. implement the token exchange
            - [ ] 3. wire the settings window
            - [!] 4. deploy (waiting on owner)
            Notes: prose lines are ignored, tasks 1-4 mentioned here must not trip the check.
            """);

        Assert.Empty(complaints);
    }

    [Fact]
    public void Find_ShortTaskWithACoupleOfCommas_IsAccepted()
    {
        // Two clauses is prose, not a hidden list — the check must not cry wolf.
        Assert.Empty(PlanShape_Validator.Find_UnrepresentableLines("- [ ] fix the parser, then re-run the suite"));
    }

    /// <summary>
    /// REAL WORK STRANDED UNDER A PARKED HEADING. The parser stops the ledger at a PARKED heading and
    /// resumes it at the next heading — correct, and deliberately not a truncation. But a supervisor
    /// can append finished work below that heading without opening a new one, and every line then
    /// vanishes from the owner's bar with nothing said.
    ///
    /// Found in ai-orchestrator-3: `## Parked questions` is the file's LAST heading, 82 task lines
    /// sit under it, 41 marked done with commit SHAs and suite counts. The owner asked for it to be
    /// made visible rather than silently under-counted.
    /// </summary>
    [Fact]
    public void DoneWorkStrandedInAParkedSection_IsComplainedAbout()
    {
        var plan = string.Join("\n",
        [
            "- [x] the real ledger line",
            "",
            "## PARKED — found, not asked for",
            "",
            "- [x] guard close-implementer on the request path - 623780d, 659 tests",
        ]);

        var complaint = Assert.Single(PlanShape_Validator.Find_UnrepresentableLines(plan));

        Assert.Contains("progress bar cannot see it", complaint, StringComparison.Ordinal);
    }

    /// <summary>
    /// AN IN-PROGRESS LINE IS THE SAME CONTRADICTION — something nobody is working on cannot be
    /// underway. It is the shape a half-migrated ledger leaves behind.
    /// </summary>
    [Fact]
    public void InProgressWorkStrandedInAParkedSection_IsComplainedAbout()
    {
        var plan = string.Join("\n",
        [
            "## PARKED — found, not asked for",
            "",
            "- [>] rewriting the tailer",
        ]);

        Assert.Single(PlanShape_Validator.Find_UnrepresentableLines(plan));
    }

    /// <summary>
    /// A GENUINELY PARKED ITEM IS LEFT ALONE — that is the section working as designed, and
    /// complaining about it would make the advisory noise the moment anybody used PARKED correctly.
    /// The seed text asks for plain bullets there; `- [ ]` is the worst a careful session writes.
    /// </summary>
    [Fact]
    public void AGenuinelyParkedItem_IsNotComplainedAbout()
    {
        var plan = string.Join("\n",
        [
            "- [x] the real ledger line",
            "",
            "## PARKED — found, not asked for",
            "",
            "- [ ] the tailer double-reads a channel on restart",
            "- the log guard drops entries below 1 GB free",
        ]);

        Assert.Empty(PlanShape_Validator.Find_UnrepresentableLines(plan));
    }

    /// <summary>
    /// AND THE SECTION ENDS AT THE NEXT HEADING, so work below a parked block is ordinary ledger work
    /// and must not be flagged. This is the property that makes the rule safe to apply at all — the
    /// parser behaves the same way, and the two must not disagree about where the ledger resumes.
    /// </summary>
    [Fact]
    public void WorkAfterTheParkedSectionEnds_IsNotComplainedAbout()
    {
        var plan = string.Join("\n",
        [
            "## PARKED — found, not asked for",
            "",
            "- [ ] something noticed",
            "",
            "## Ledger",
            "",
            "- [x] back in the ledger, and done",
        ]);

        Assert.Empty(PlanShape_Validator.Find_UnrepresentableLines(plan));
    }

    /// <summary>The OWNER REQUESTS table is the other non-ledger section and follows the same rule.</summary>
    [Fact]
    public void DoneWorkStrandedUnderOwnerRequests_IsComplainedAbout()
    {
        var plan = string.Join("\n",
        [
            "## OWNER REQUESTS — written the moment they arrive",
            "",
            "- [x] shipped the thing they asked for",
        ]);

        Assert.Single(PlanShape_Validator.Find_UnrepresentableLines(plan));
    }
}
