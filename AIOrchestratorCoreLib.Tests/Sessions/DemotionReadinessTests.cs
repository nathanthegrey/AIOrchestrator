using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// The demotion's decision table, and the direction the owner's one command reads off the shape.
///
/// The mirror of the promotion's, and written as a separate file for the same reason that one is:
/// this is the rule asked at the MOMENT OF EFFECT, and a parked request can be twelve hours old.
///
/// The owner, 2026-08-25: *"This command must be bidirectional — with the same command I transform
/// an orchestra into solo, and a solo into an orchestra."* Nothing could clear SupervisorSpawnedUtc
/// before that, so half of this table was unreachable.
/// </summary>
public class DemotionReadinessTests
{
    static readonly DateTime SPAWNED = new(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A crew with no solo left: the ordinary case, and the only one that spawns a solo.</summary>
    [Fact]
    public void ACrewWithNoSolo_IsReady()
    {
        Assert.Equal(DemotionReadiness.Ready, OrchestrationShape.Decide_DemotionReadiness(SPAWNED, hasLiveSolo: false));
    }

    /// <summary>
    /// Already basic — the refusal that stops a second tap, the mirror of AlreadyACrew. Two parked
    /// requests can both pass the park check; the second must not put a second solo beside the first.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AlreadyBasic_IsRefused_WhicheverSessionsAreRunning(bool hasLiveSolo)
    {
        Assert.Equal(DemotionReadiness.AlreadyBasic, OrchestrationShape.Decide_DemotionReadiness(null, hasLiveSolo));
        Assert.False(OrchestrationShape.Can_StillDemote(DemotionReadiness.AlreadyBasic));
    }

    /// <summary>
    /// Stamped as a crew with a solo ALREADY running — a demotion that stopped halfway. Finishing it
    /// is right, and it must not spawn a second solo; that is what Incomplete buys.
    /// </summary>
    [Fact]
    public void ACrewWithALiveSolo_IsIncomplete_AndStillActionable()
    {
        Assert.Equal(DemotionReadiness.Incomplete, OrchestrationShape.Decide_DemotionReadiness(SPAWNED, hasLiveSolo: true));
        Assert.True(OrchestrationShape.Can_StillDemote(DemotionReadiness.Incomplete));
    }

    /// <summary>
    /// THE ASYMMETRY IS DELIBERATE, so it is pinned rather than left to look like an oversight.
    /// Promotion has NothingToPromote — basic with no live solo, no session to replace. Demotion has
    /// no such case: a crew always has something to take down, alive or not.
    /// </summary>
    [Fact]
    public void ACrewWithNothingRunning_IsStillDemotable_UnlikeTheMirrorCase()
    {
        Assert.Equal(PromotionReadiness.NothingToPromote, OrchestrationShape.Decide_PromotionReadiness(null, hasLiveSolo: false));
        Assert.False(OrchestrationShape.Can_StillPromote(PromotionReadiness.NothingToPromote));

        Assert.True(OrchestrationShape.Can_StillDemote(OrchestrationShape.Decide_DemotionReadiness(SPAWNED, hasLiveSolo: false)));
    }

    /// <summary>
    /// THE DIRECTION OF THE OWNER'S ONE COMMAND, read off the shape rather than typed — the whole
    /// point of /switch being one verb: they never have to remember which of two commands this topic
    /// needs.
    /// </summary>
    [Fact]
    public void TheDirection_IsReadFromTheShape()
    {
        Assert.True(OrchestrationShape.Would_Promote(null));
        Assert.False(OrchestrationShape.Would_Promote(SPAWNED));
    }

    /// <summary>
    /// THE HALF-FINISHED STATE BELONGS TO BOTH DIRECTIONS, and that is not a defect — it is one
    /// state with two honest readings. "Stamped as a crew with its solo still running" is exactly
    /// what a promotion that threw halfway leaves behind, and it is exactly what a demotion that
    /// threw halfway leaves behind too. Promotion calls it Incomplete and finishes it; demotion calls
    /// it Incomplete and finishes it the other way. Both are legitimate.
    ///
    /// This case exists because an earlier version of it asserted the OPPOSITE — that only one
    /// direction is ever available — and failed here. The law it was reaching for is the real one
    /// below: the direction is never ambiguous, because /switch does not consult availability to
    /// choose. It reads the shape.
    /// </summary>
    [Fact]
    public void TheHalfFinishedState_CanGoEitherWay()
    {
        Assert.True(OrchestrationShape.Can_StillPromote(OrchestrationShape.Decide_PromotionReadiness(SPAWNED, hasLiveSolo: true)));
        Assert.True(OrchestrationShape.Can_StillDemote(OrchestrationShape.Decide_DemotionReadiness(SPAWNED, hasLiveSolo: true)));
    }

    /// <summary>
    /// AND THE DIRECTION IS STILL NEVER AMBIGUOUS. Whatever the state, `Would_Promote` picks exactly
    /// one way and that way is actionable — so one command can never be argued into either, and the
    /// owner is never shown a choice they did not ask to make.
    ///
    /// In the shared half-finished state it demotes, which is the reading that matches what the owner
    /// can SEE: the stamp is set, so their topic has been behaving as a crew, and /switch means "make
    /// it the other thing".
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhicheverDirectionIsChosen_IsTheOneThatCanAct(bool hasLiveSolo)
    {
        foreach (var stamp in new DateTime?[] { null, SPAWNED })
        {
            var promoting = OrchestrationShape.Would_Promote(stamp);

            var actionable = promoting
                ? OrchestrationShape.Can_StillPromote(OrchestrationShape.Decide_PromotionReadiness(stamp, hasLiveSolo))
                : OrchestrationShape.Can_StillDemote(OrchestrationShape.Decide_DemotionReadiness(stamp, hasLiveSolo));

            // The one genuinely dead state: basic with nothing running. /switch refuses it out loud
            // rather than spawning a crew around an empty orchestration.
            var deadEnd = promoting && !hasLiveSolo;

            Assert.Equal(!deadEnd, actionable);
        }
    }
}
