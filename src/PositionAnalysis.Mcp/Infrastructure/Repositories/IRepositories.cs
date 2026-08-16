using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Domain.Enums;

namespace PositionAnalysis.Mcp.Infrastructure.Repositories;

/// <summary>
/// Repository for Position Description entities
/// Queries from Oracle TEMP_PD_SCHED_PC and TEMP_PD_SCHED_PC_DUTIES
/// </summary>
public interface IPositionDescriptionRepository
{
    Task<List<PositionDescription>> GetAllAsync();
    Task<PositionDescription?> GetByPdNbrAsync(string pdNbr);
    Task<List<PositionDescription>> GetBySeriesAsync(OccupationalSeries series);
    Task<List<PositionDescription>> GetByGradeRangeAsync(Grade minGrade, Grade maxGrade);
    Task<List<PositionDescription>> GetByFilterAsync(StagingFilter filter);
    Task<List<string>> GetHumanSchedulePcPdNumbersAsync();
}

/// <summary>
/// Repository for Schedule PC Evaluation results
/// Queries from and writes to Oracle SCHEDULE_PC_EVAL table
/// </summary>
public interface IPositionAnalysisEvalRepository
{
    Task<string> InsertAsync(EvaluationResult result);
    Task UpdateAsync(EvaluationResult result);
    Task<int> DeleteAllAsync();
    Task<StagingResult> StageFromMaxPdAsync(StagingFilter filter);
    Task<List<SeriesCounts>> GetSeriesCountsAsync();
    Task<EvaluationResult?> GetByPdAsync(string pdNbr);
    Task<List<EvaluationResult>> GetAllAsync();
    Task<List<EvaluationResult>> GetBySeriesAsync(OccupationalSeries series);
    Task<List<EvaluationResult>> GetByStatusAsync(EvaluationStatus status);
    Task<int> GetCountByStatusAsync(EvaluationStatus status);
    Task<int> CountNewToStageAsync(StagingFilter filter);
    Task<int> ResetFailedAsync();
    Task UpdateRatingAsync(string pdNbr, OccupationalSeries series, string rating, bool isCandidate);
    Task<int> RecoverExpiredClaimsAsync() => throw new NotSupportedException();
    Task<EvaluationResult?> ClaimNextPendingAsync(string workerId, TimeSpan leaseDuration) => throw new NotSupportedException();
    Task<bool> CompleteClaimAsync(EvaluationResult result, string workerId) => throw new NotSupportedException();
    Task<QueueStatus> GetQueueStatusAsync() => throw new NotSupportedException();
}

public sealed record SeriesCounts(string Series, int Staged, int InProgress, int Complete, int Failed);
