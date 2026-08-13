namespace PositionAnalysis.Mcp.Domain.Entities;

public sealed record QueueStatus(int Pending, int InProgress, int Complete, int Failed)
{
    public bool IsDrained => Pending == 0 && InProgress == 0;
}
