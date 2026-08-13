using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.ValueObjects;

namespace PositionAnalysis.Mcp.Application.Interfaces;

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
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetProcessingReportBySeriesAsync(IEnumerable<string> series);
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetProcessingReportByOrgCodesAsync(IEnumerable<string> orgCodes);
    Task<List<PdProcessingStatus>> GetProcessingStatusByPdNumbersAsync(IEnumerable<string> pdNumbers);
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
    /// Forces a fresh rescore of every PD flagged needs_rescore = 'Y'
    /// </summary>
    Task RescoreFlaggedAsync();

    /// <summary>
    /// Forces a fresh rescore of PDs flagged needs_rescore = 'Y' within the specified series
    /// </summary>
    Task RescoreFlaggedBySeriesAsync(IEnumerable<string> series);

    /// <summary>
    /// Forces a fresh rescore of the specified PDs, but only those flagged needs_rescore = 'Y'
    /// </summary>
    Task RescoreFlaggedByPdAsync(IEnumerable<string> pdNumbers);

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
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStatusBySeriesAsync(IEnumerable<string> series);
    Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStatusByOrgCodesAsync(IEnumerable<string> orgCodes);
    Task<List<PdProcessingStatus>> GetStatusByPdNumbersAsync(IEnumerable<string> pdNumbers);
}
