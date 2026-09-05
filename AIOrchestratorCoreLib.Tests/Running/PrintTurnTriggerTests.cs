using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

public class PrintTurnTriggerTests
{
    const string CHANNEL =
        "## [1] FROM supervisor — 2026-09-05 10:00 — brief\n\ndo X\n" +
        "## [2] FROM implementer — 2026-09-05 10:01 — imp-1 online\n\nhi\n" +
        "## [3] FROM app — 2026-09-05 10:02 — [agent] turn_ended imp-1 turn 1 — success\n\nrequest_id: o/imp-1/1\n" +
        "## [4] FROM supervisor — 2026-09-05 10:03 — GO AHEAD\n\ncontinue\n" +
        "## [5] FROM owner — 2026-09-05 10:04 — via Telegram\n\ntyped straight in\n";

    [Fact]
    public void AMember_IsTriggeredBySupervisorAndOwner_NeverByItselfOrTheApp()
    {
        var entries = ChannelEntry_Parser.Parse_All(CHANNEL);

        var pending = PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, lastHandledEntryIndex: 0);

        Assert.Equal([1, 4, 5], pending.Select(entry => entry.Index));
    }

    [Fact]
    public void OnlyEntriesAboveTheHandledIndex_ArePending()
    {
        var entries = ChannelEntry_Parser.Parse_All(CHANNEL);

        Assert.Equal([4, 5], PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, 1).Select(entry => entry.Index));
        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, 5));
    }

    [Theory]
    [InlineData(SessionRoles.Solo, ChannelAuthors.Owner, true)]
    [InlineData(SessionRoles.Solo, ChannelAuthors.Solo, false)]
    [InlineData(SessionRoles.Solo, ChannelAuthors.App, false)]
    [InlineData(SessionRoles.General, ChannelAuthors.Owner, true)]
    [InlineData(SessionRoles.General, ChannelAuthors.Supervisor, false)]
    [InlineData(SessionRoles.Reviewer, ChannelAuthors.Supervisor, true)]
    [InlineData(SessionRoles.Reviewer, ChannelAuthors.Reviewer, false)]
    [InlineData(SessionRoles.Supervisor, ChannelAuthors.Implementer, true)]
    [InlineData(SessionRoles.Supervisor, ChannelAuthors.Supervisor, false)]
    [InlineData(SessionRoles.Implementer, ChannelAuthors.Unknown, false)]
    public void InboundRule_PerRole(SessionRoles role, ChannelAuthors author, bool inbound)
    {
        Assert.Equal(inbound, PrintTurn_Trigger.Is_Inbound(role, author));
    }
}
