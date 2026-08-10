using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Infrastructure.Repositories;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.Application.Services;

/// <summary>
/// Orchestrates staging of Position Descriptions for evaluation
/// Queries PDs from MAX_PD_VW using filtering criteria and inserts to SCHEDULE_PC_EVAL
/// </summary>
public class StagingOrchestrator : IStagingOrchestrator
{
    private readonly IPositionDescriptionRepository _pdRepository;
    private readonly ISchedulePCEvalRepository _evalRepository;
    private readonly ILogger<StagingOrchestrator> _logger;

    public StagingOrchestrator(
        IPositionDescriptionRepository pdRepository,
        ISchedulePCEvalRepository evalRepository,
        ILogger<StagingOrchestrator> logger)
    {
        _pdRepository = pdRepository ?? throw new ArgumentNullException(nameof(pdRepository));
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Stages Position Descriptions for evaluation.
    /// Returns the count of successfully staged PDs.
    /// </summary>
    public async Task<int> StageAsync(StagingFilter filter)
    {
        var stagingStarted = DateTime.Now;

        try
        {
            _logger.LogInformation("Starting staging with filter: {Filter}", filter);

            var deletedCount = await _evalRepository.DeleteAllAsync();
            _logger.LogInformation("Cleared {DeletedCount} existing evaluation rows", deletedCount);

            var stagedCount = await _evalRepository.StageFromMaxPdAsync(filter);

            var stagingDuration = DateTime.Now - stagingStarted;
            _logger.LogInformation(
                "Staging completed: {StagedCount} staged in {Duration:F2}s",
                stagedCount, stagingDuration.TotalSeconds);

            return stagedCount;
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
