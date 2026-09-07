namespace AIOrchestratorCoreLib.Running.ExecutedTurn;

internal sealed class ExecutedTurnModel(
    int turnNumber,
    string requestId,
    int firstEntryIndex,
    int lastEntryIndex,
    DateTime endedUtc,
    string outcome,
    double? costUsd) : IExecutedTurn
{
    public int TurnNumber { get; } = turnNumber;
    public string RequestId { get; } = requestId;
    public int FirstEntryIndex { get; } = firstEntryIndex;
    public int LastEntryIndex { get; } = lastEntryIndex;
    public DateTime EndedUtc { get; } = endedUtc;
    public string Outcome { get; } = outcome;
    public double? CostUsd { get; } = costUsd;
}
