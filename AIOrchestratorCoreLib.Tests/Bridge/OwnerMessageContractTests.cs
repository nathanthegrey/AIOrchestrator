using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE RULES THE PROTOCOL ASKS FOR, CHECKED INSTEAD OF HOPED FOR.
///
/// <para>
/// A mechanical audit of the supervisor protocol on 2026-09-07 counted 229 imperative rules and 14
/// pairs that cannot both be obeyed — "lead with the decision" against a mandatory receipt 396 lines
/// below it, "one open question at a time" against "ask in passing" 783 lines below it. The lower one
/// wins because it is what the model read last. These pin the handful of rules a machine can settle,
/// so they stop depending on which of 229 instructions got weighed.
/// </para>
/// <para>
/// The conforming examples are real entries from the owner's own topic; so are most of the faults.
/// </para>
/// </summary>
public class OwnerMessageContractTests
{
    static void Assert_Clean(string body)
    {
        Assert.Empty(OwnerMessage_Contract.Check(body));
    }

    static void Assert_Faults(string body, OwnerMessageFaults expected)
    {
        Assert.Contains(expected, OwnerMessage_Contract.Check(body));
    }

    // ── What conforms ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAnswerWithAQuestionAndOptions_Conforms()
    {
        Assert_Clean(
            "No card, like the old app. A card wall at cutover would be a regression for every migrating customer.\n"
            + "\n"
            + "QUESTION: Does the trial ask for a card at signup?\n"
            + "OPTION: No card\n"
            + "OPTION: Card required");
    }

    [Fact]
    public void APlainStatementWithNoQuestion_Conforms()
    {
        Assert_Clean("Both tickets are merged. The pricing page now names the Live cap per tier.");
    }

    [Fact]
    public void AnEmptyBody_Conforms()
    {
        Assert_Clean("");
    }

    /// <summary>A screenshot carries a path by design — it is how a picture reaches the phone.</summary>
    [Fact]
    public void AnImageLine_IsNotCode()
    {
        Assert_Clean("The comparison table now shows the cap.\nIMAGE: /Users/nvene/Desktop/pricing/comparison-table.png");
    }

    /// <summary>Member ids are how the owner refers to the crew. They must never be flagged.</summary>
    [Fact]
    public void MemberIdsAndTicketCodes_AreNotCode()
    {
        Assert_Clean("imp-1 finished FIN-D-272 and rev-2 cleared it. Nothing is waiting on you.");
    }

    // ── The faults ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoQuestionsInOneEntry_IsAFault()
    {
        Assert_Faults(
            "QUESTION: Merge 272 now?\nOPTION: Merge\nOPTION: Hold\nQUESTION: And 267?\nOPTION: Yes\nOPTION: No",
            OwnerMessageFaults.TwoQuestions);
    }

    /// <summary>
    /// The topic's own shape: a question, then more prose under it. The owner answers from a lock
    /// screen and never sees what came after.
    /// </summary>
    [Fact]
    public void ProseBelowTheQuestion_IsAFault()
    {
        Assert_Faults(
            "QUESTION: Merge 272 into dev now?\nOPTION: Merge\nOPTION: Hold\nimp-1 is on 274 meanwhile.",
            OwnerMessageFaults.ProseAfterTheQuestion);
    }

    [Theory]
    [InlineData("OPTION: Merge\nOPTION: Hold")]
    [InlineData("Ready when you are.\nOPTION: Go\nOPTION: Wait")]
    public void ButtonsWithNoQuestion_IsAFault(string body)
    {
        Assert_Faults(body, OwnerMessageFaults.OptionsWithoutAQuestion);
    }

    /// <summary>Markers that belong to the question are not prose and must not trip the check.</summary>
    [Fact]
    public void DeadlineAndDefaultBelowTheQuestion_AreNotProse()
    {
        Assert_Clean("QUESTION: Merge 272?\nOPTION: Merge\nOPTION: Hold\nDEADLINE: 30m\nDEFAULT: 2");
    }

    /// <summary>
    /// THE BURIED ANSWER, in its smallest form. The protocol mandates a receipt before the work and
    /// also says line one is the answer; the receipt wins, and this is what the owner reads.
    /// </summary>
    [Theory]
    [InlineData("Noted — I'll look at the trial policy and report back.")]
    [InlineData("Got it. Checking the Stripe config now.")]
    [InlineData("I'll verify the merged code and come back to you.")]
    [InlineData("On it — imp-2 is measuring the pricing page.")]
    public void OpeningWithAReceipt_IsAFault(string body)
    {
        Assert_Faults(body, OwnerMessageFaults.OpensWithAReceipt);
    }

    /// <summary>The same words further down are where the contract actually puts them.</summary>
    [Fact]
    public void AReceiptOnLineTwo_IsFine()
    {
        Assert_Clean("No card, like the old app.\nI'll write it up as ruled and brief the code.");
    }

    /// <summary>"Noted" inside a sentence is a word, not a receipt.</summary>
    [Fact]
    public void TheWordNotedMidSentence_IsNotAReceipt()
    {
        Assert_Clean("The risk you noted on 03/09 is now recorded on the ticket instead of living in a chat.");
    }

    [Theory]
    [InlineData("The fix is in /Users/nvene/Visual Studio/AIOrchestrator/src/engine.cs")]
    [InlineData("at Orchestrator.Run(Program.cs:42)")]
    [InlineData("PlanLedger_Parser was returning null for that row.")]
    public void CodeInAnOwnerMessage_IsAFault(string body)
    {
        Assert_Faults(body, OwnerMessageFaults.CarriesCode);
    }

    /// <summary>A date is not a path, and neither is a fraction. These broke a naive separator count.</summary>
    [Theory]
    [InlineData("Done on 07/09 — the trial now grants the plan being bought.")]
    [InlineData("Two of the five launch tickets are done, so 2/5 overall.")]
    public void DatesAndFractions_AreNotPaths(string body)
    {
        Assert_Clean(body);
    }

    [Fact]
    public void OverTheCharacterCeiling_IsAFault()
    {
        Assert_Faults(new string('a', OwnerMessage_Contract.MAXIMUM_CHARACTERS + 1), OwnerMessageFaults.TooLong);
    }

    [Fact]
    public void OverTheLineCeiling_IsAFault()
    {
        var body = string.Join("\n", Enumerable.Range(1, OwnerMessage_Contract.MAXIMUM_LINES + 1).Select(n => $"line {n}"));

        Assert_Faults(body, OwnerMessageFaults.TooLong);
    }

    [Fact]
    public void BlankLines_DoNotCountTowardTheCeiling()
    {
        Assert_Clean("one\n\ntwo\n\nthree\n\nfour");
    }

    // ── The coaching ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every fault must say what is wrong AND why it costs the owner something. Coaching that only
    /// names a rule teaches the session to satisfy the checker.
    /// </summary>
    [Theory]
    [InlineData(OwnerMessageFaults.TwoQuestions)]
    [InlineData(OwnerMessageFaults.ProseAfterTheQuestion)]
    [InlineData(OwnerMessageFaults.OptionsWithoutAQuestion)]
    [InlineData(OwnerMessageFaults.OpensWithAReceipt)]
    [InlineData(OwnerMessageFaults.CarriesCode)]
    [InlineData(OwnerMessageFaults.TooLong)]
    public void EveryFault_IsExplainedInFull(OwnerMessageFaults fault)
    {
        var described = OwnerMessage_Contract.Describe(fault);

        Assert.False(string.IsNullOrWhiteSpace(described));
        Assert.True(described.Length > 60, $"too terse to act on: {described}");
    }

    /// <summary>One entry can break the contract more than one way, and must be told about all of it.</summary>
    [Fact]
    public void SeveralFaultsInOneEntry_AreAllReported()
    {
        var faults = OwnerMessage_Contract.Check(
            "Noted — checking now.\nQUESTION: Merge?\nQUESTION: And 267?\nstill working on 274.");

        Assert.Contains(OwnerMessageFaults.OpensWithAReceipt, faults);
        Assert.Contains(OwnerMessageFaults.TwoQuestions, faults);
        Assert.Contains(OwnerMessageFaults.ProseAfterTheQuestion, faults);
    }
}
