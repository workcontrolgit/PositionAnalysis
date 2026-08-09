using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Domain.Enums;

namespace SchedulePCMcp.Infrastructure.Repositories;

/// <summary>
/// Repository for Position Description entities
/// Queries from Oracle MAX_PD_VW and PD_DUTIES
/// </summary>
public interface IPositionDescriptionRepository
{
    Task<List<PositionDescription>> GetAllAsync();
    Task<PositionDescription?> GetByPdNbrAsync(string pdNbr);
    Task<List<PositionDescription>> GetBySeriesAsync(OccupationalSeries series);
    Task<List<PositionDescription>> GetByGradeRangeAsync(Grade minGrade, Grade maxGrade);
    Task<List<PositionDescription>> GetByFilterAsync(StagingFilter filter);
}

/// <summary>
/// Repository for Schedule PC Evaluation results
/// Queries from and writes to Oracle SCHEDULE_PC_EVAL table
/// </summary>
public interface ISchedulePCEvalRepository
{
    Task<string> InsertAsync(EvaluationResult result);
    Task UpdateAsync(EvaluationResult result);
    Task<int> DeleteAllAsync();
    Task<string?> GetLatestRunIdAsync();
    Task<List<SeriesCounts>> GetSeriesCountsAsync(string runId);
    Task<EvaluationResult?> GetByRunAndPdAsync(string runId, string pdNbr);
    Task<List<EvaluationResult>> GetByRunAsync(string runId);
    Task<List<EvaluationResult>> GetByRunAndSeriesAsync(string runId, OccupationalSeries series);
    Task<List<EvaluationResult>> GetByStatusAsync(EvaluationStatus status);
    Task<int> GetCountByStatusAsync(string runId, EvaluationStatus status);
}

public sealed record SeriesCounts(string Series, int Staged, int InProgress, int Complete, int Failed);
