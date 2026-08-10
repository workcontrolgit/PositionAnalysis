using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;

namespace SchedulePCMcp.Application.Interfaces;

/// <summary>
/// Orchestrates staging of Position Descriptions from the TEMP source into SCHEDULE_PC_EVAL
/// </summary>
public interface IStagingOrchestrator
{
    Task<StagingResult> StageAsync(StagingFilter filter);
}

/// <summary>
/// Provides reporting on staging/processing status
/// </summary>
public interface IReportingService
{
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStagingReportAsync();
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetProcessingReportAsync();
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
    /// <param name="pdNbr">The position description number</param>
    Task ScoreAsync(string pdNbr);

    /// <summary>
    /// Scores all Position Descriptions for specified occupational series
    /// </summary>
    /// <param name="series">Enumerable of occupational series codes (5-digit strings)</param>
    Task ScoreBySeriesAsync(IEnumerable<string> series);

    /// <summary>
    /// Scores all PENDING Position Descriptions across every staged series
    /// </summary>
    Task ScoreAllAsync();

    /// <summary>
    /// Forces a fresh rescore of all PDs in the specified series, regardless of current status
    /// </summary>
    Task RescoreBySeriesAsync(IEnumerable<string> series);

    /// <summary>
    /// Forces a fresh rescore of every staged PD across all series, regardless of current status
    /// </summary>
    Task RescoreAllAsync();

    /// <summary>
    /// Retrieves the evaluation result for a specific PD
    /// </summary>
    /// <param name="pdNbr">The position description number</param>
    /// <returns>The evaluation result or null if not found</returns>
    Task<EvaluationResult?> GetResultAsync(string pdNbr);
}

/// <summary>
/// Tracks live progress of scoring operations
/// </summary>
public interface IProcessingStatusService
{
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStatusAsync();
    Task<SeriesStatus?> GetStatusBySeriesAsync(OccupationalSeries series);
}
