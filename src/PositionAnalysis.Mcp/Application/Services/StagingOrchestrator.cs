using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.Application.Services;

/// <summary>
/// Orchestrates staging of Position Descriptions for evaluation
/// Queries PDs from the TEMP source using filtering criteria and inserts to SCHEDULE_PC_EVAL
/// </summary>
public class StagingOrchestrator : IStagingOrchestrator
{
    private readonly IPositionDescriptionRepository _pdRepository;
    private readonly IPositionAnalysisEvalRepository _evalRepository;
    private readonly ILogger<StagingOrchestrator> _logger;

    public StagingOrchestrator(
        IPositionDescriptionRepository pdRepository,
        IPositionAnalysisEvalRepository evalRepository,
        ILogger<StagingOrchestrator> logger)
    {
        _pdRepository = pdRepository ?? throw new ArgumentNullException(nameof(pdRepository));
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Stages Position Descriptions for evaluation.
    /// Returns staging and excluded-dutyless-header counts.
    /// </summary>
    public async Task<StagingResult> StageAsync(StagingFilter filter)
    {
        var stagingStarted = DateTime.Now;

        try
        {
            _logger.LogInformation("Starting staging with filter: {Filter}", filter);

            var result = await _evalRepository.StageFromMaxPdAsync(filter);

            var stagingDuration = DateTime.Now - stagingStarted;
            _logger.LogInformation(
                "Staging completed: {StagedCount} staged, {ExcludedWithoutDutiesCount} excluded without duties in {Duration:F2}s",
                result.StagedCount,
                result.ExcludedWithoutDutiesCount,
                stagingDuration.TotalSeconds);

            return result;
        }
        catch (OracleException ex)
        {
            _logger.LogError(ex, "Oracle error during staging");
            throw new InvalidOperationException("Staging failed due to database error", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during staging");
            throw new InvalidOperationException("Staging failed unexpectedly", ex);
        }
    }
}
