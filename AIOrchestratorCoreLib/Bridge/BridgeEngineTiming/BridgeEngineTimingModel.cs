namespace AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;

internal sealed class BridgeEngineTimingModel(
    int mirrorTickMilliseconds,
    int ownerAggregationSeconds,
    int mirrorRetryBackoffSeconds,
    int tickLockAllowanceMilliseconds) : IBridgeEngineTiming
{
    public int MirrorTickMilliseconds { get; } = mirrorTickMilliseconds;
    public int OwnerAggregationSeconds { get; } = ownerAggregationSeconds;
    public int MirrorRetryBackoffSeconds { get; } = mirrorRetryBackoffSeconds;
    public int TickLockAllowanceMilliseconds { get; } = tickLockAllowanceMilliseconds;
}
