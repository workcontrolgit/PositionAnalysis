namespace SchedulePCMcp.Domain.Entities;

public sealed record StagingResult(int StagedCount, int ExcludedWithoutDutiesCount);