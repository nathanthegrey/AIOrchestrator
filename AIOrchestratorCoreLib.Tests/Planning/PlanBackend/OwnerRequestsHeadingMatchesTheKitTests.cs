using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// THE APP NOW WRITES A SECTION THE ROLE COMMANDS TEACH, so there are three copies of its heading and
/// its table shape: `kit/commands/supervisor.md`, `kit/commands/solo.md`, and
/// <see cref="PlanRequest_Writer"/>. That is the drift mechanism `PlanLedger_Markers` documents — a
/// marker list written down five times ended up with four entries in two of them — arriving in a new
/// place, and prose cannot hold it.
///
/// The join is cross-language: the role commands are markdown a session reads, and nothing can import
/// them. Reading the files is the only assertion available, and it FAILS LOUDLY when it cannot find
/// them: a harness that cannot find what it tests must refuse to run rather than certify the absence
/// of the thing it is testing.
/// </summary>
public class OwnerRequestsHeadingMatchesTheKitTests
{
    [Theory]
    [InlineData("supervisor.md")]
    [InlineData("solo.md")]
    public void TheHeadingTheAppWritesIsTheOneTheRoleCommandTeaches(string command)
    {
        var text = Read_Command(command);

        Assert.Contains(PlanRequest_Writer.OWNER_REQUESTS_HEADING, text);
    }

    [Theory]
    [InlineData("supervisor.md")]
    [InlineData("solo.md")]
    public void TheTableShapeTheAppWritesIsTheOneTheRoleCommandTeaches(string command)
    {
        var text = Read_Command(command);

        Assert.Contains(PlanRequest_Writer.TABLE_HEADER_ROW, text);
        Assert.Contains(PlanRequest_Writer.TABLE_SEPARATOR_ROW, text);
    }

    /// <summary>
    /// And the section the app writes into is the one the PARSER skips — otherwise every ingested row
    /// would land in the owner's denominator twice, once as a table row and once as the ledger line
    /// beside it.
    /// </summary>
    [Fact]
    public void TheHeadingOpensASectionTheBarCannotSee()
    {
        Assert.True(PlanLedger_Sections.Opens_NonLedgerSection(PlanRequest_Writer.OWNER_REQUESTS_HEADING));

        Assert.StartsWith(
            PlanLedger_Sections.OWNER_REQUESTS_HEADING_PREFIX,
            PlanLedger_Sections.Read_HeadingTitle_OrNull(PlanRequest_Writer.OWNER_REQUESTS_HEADING),
            StringComparison.OrdinalIgnoreCase);
    }

    static string Read_Command(string fileName)
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "commands", fileName);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        throw new Exception($"could not locate kit/commands/{fileName} — the harness is not reading the file it asserts about");
    }
}
