using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The guard that did not exist when it was needed. Adding <see cref="MemberStates.StandingBy"/> left
/// three consumers throwing on the happy path — two card switches and the bridge's own — and the
/// suite stayed green at 484, because `dotnet test` never compiles the WPF project where two of them
/// lived. The blast radius was total: the main window assigns ItemsSource as its last statement, so
/// ONE standing-by member froze EVERY card in EVERY orchestration, re-throwing every 5 seconds.
///
/// So these walk the enum itself rather than a list someone has to remember to extend. A new member
/// added without a case fails here, in a project the suite does compile, before it can reach a card.
/// </summary>
public class MemberStateDescriptorTests
{
    /// <summary>
    /// THE CONTRADICTION THE OWNER SENT BACK, 2026-08-24, as one line about one session at one
    /// instant: "solo-1: working now (channel says: idle — writing window left open)".
    ///
    /// It came from a SECOND copy of this class's job in BridgeEngineModel, which printed
    /// "working now (channel says: {declared})" and interpolated the not-working wording of the very
    /// same state. That copy is deleted; this asserts the surviving one cannot say both words at
    /// once, so a future re-introduction fails here rather than on the owner's phone.
    /// </summary>
    [Fact]
    public void AWorkingSession_IsNeverAlsoDescribedAsIdle()
    {
        foreach (var state in Enum.GetValues<MemberStates>())
        {
            var described = MemberState_Descriptor.Describe_ForOwner(state, isWorkingNow: true, ownerOwesReply: false);

            Assert.StartsWith("working now", described);
            Assert.DoesNotContain("idle", described);
        }
    }

    [Fact]
    public void EveryStateHasAWording()
    {
        foreach (var state in Enum.GetValues<MemberStates>())
            Assert.False(string.IsNullOrWhiteSpace(MemberState_Descriptor.Describe(state)), $"no wording for {state}");
    }

    [Fact]
    public void EveryStateHasABrushKey()
    {
        foreach (var state in Enum.GetValues<MemberStates>())
            Assert.False(string.IsNullOrWhiteSpace(MemberState_Descriptor.Brush_Key(state)), $"no brush key for {state}");
    }

    /// <summary>
    /// The wordings are what the owner reads on the card, so they must be distinct — two states that
    /// render identically are one state as far as anybody looking at the app is concerned.
    /// </summary>
    [Fact]
    public void TheWordingsAreDistinct()
    {
        List<string> seen = [];

        foreach (var state in Enum.GetValues<MemberStates>())
        {
            var wording = MemberState_Descriptor.Describe(state);

            Assert.DoesNotContain(wording, seen);
            seen.Add(wording);
        }
    }

    /// <summary>
    /// Brush keys are deliberately NOT required to be distinct — standing-by shares the awaiting
    /// colour, because both mean quiet-and-fine and a new colour would imply something is wrong. This
    /// pins that as a decision rather than an oversight.
    /// </summary>
    [Fact]
    public void StandingBySharesTheQuietColourWithAwaitingReview()
    {
        Assert.Equal(
            MemberState_Descriptor.Brush_Key(MemberStates.AwaitingSupervisorReview),
            MemberState_Descriptor.Brush_Key(MemberStates.StandingBy));
    }

    [Fact]
    public void TheNewStateReadsAsNothingIsWrong()
    {
        Assert.Equal("standing by — nothing owed", MemberState_Descriptor.Describe(MemberStates.StandingBy));
    }
}
