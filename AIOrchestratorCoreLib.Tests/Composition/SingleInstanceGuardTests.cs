using AIOrchestratorCoreLib.Composition;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Composition;

public class SingleInstanceGuardTests : IDisposable
{
    readonly string _root;
    readonly ISupervisionPaths _paths;

    public SingleInstanceGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-instance-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheFirstHost_GetsTheLock_AndTheSecondIsRefused_UntilTheFirstLetsGo()
    {
        using var first = SingleInstance_Guard.Try_Acquire(_paths);
        Assert.NotNull(first);
        Assert.True(File.Exists(_paths.InstanceLockFile));

        Assert.Null(SingleInstance_Guard.Try_Acquire(_paths));

        first.Dispose();

        using var again = SingleInstance_Guard.Try_Acquire(_paths);
        Assert.NotNull(again);
    }

    [Fact]
    public void AMissingRoot_IsCreated_RatherThanFailingTheLock()
    {
        Assert.False(Directory.Exists(_root));

        using var held = SingleInstance_Guard.Try_Acquire(_paths);

        Assert.NotNull(held);
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void TheLockFile_LivesUnderTheRoot_WhereNoOtherComponentComposesPathsByHand()
    {
        Assert.Equal(Path.Combine(_root, ".instance.lock"), _paths.InstanceLockFile);
    }
}
