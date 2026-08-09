namespace SchedulePCMcp.Domain.Enums;

/// <summary>
/// Evaluation status throughout the Schedule PC pipeline
/// </summary>
public enum EvaluationStatus
{
    Staged = 0,
    InProgress = 1,
    Complete = 2,
    Failed = 3
}
