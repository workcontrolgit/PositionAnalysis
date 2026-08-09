using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;

namespace SchedulePCMcp.Application.Interfaces;

/// <summary>
/// Orchestrates staging of Position Descriptions from MAX_PD_VW into SCHEDULE_PC_EVAL
/// </summary>
public interface IStagingOrchestrator
{
    Task<string> StageAsync(StagingFilter filter);
    Task<RunMetadata?> GetRunMetadataAsync(string runId);
}

/// <summary>
/// Provides reporting on staging/processing status
/// </summary>
public interface IReportingService
{
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStagingReportAsync(string runId);
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetProcessingReportAsync(string runId);
}

/// <summary>
/// Orchestrates LLM-based scoring of Position Descriptions
/// Handles single PD scoring, batch scoring by series, and result retrieval
/// </summary>
public interface IScoringOrchestrator
{
    /// <summary>
    /// Scores a single Position Description using LLM evaluation
    /// </summary>
    /// <param name="runId">The evaluation run identifier</param>
    /// <param name="pdNbr">The position description number</param>
    Task ScoreAsync(string runId, string pdNbr);

    /// <summary>
    /// Scores all Position Descriptions for specified occupational series
    /// </summary>
    /// <param name="runId">The evaluation run identifier</param>
    /// <param name="series">Enumerable of occupational series codes (4-digit strings)</param>
    Task ScoreBySeriesAsync(string runId, IEnumerable<string> series);

    /// <summary>
    /// Retrieves the evaluation result for a specific PD in a run
    /// </summary>
    /// <param name="runId">The evaluation run identifier</param>
    /// <param name="pdNbr">The position description number</param>
    /// <returns>The evaluation result or null if not found</returns>
    Task<EvaluationResult?> GetResultAsync(string runId, string pdNbr);
}

/// <summary>
/// Tracks live progress of scoring operations
/// </summary>
public interface IProcessingStatusService
{
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStatusAsync(string runId);
    Task<SeriesStatus?> GetStatusBySeriesAsync(string runId, OccupationalSeries series);
}

