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

            var positionDescriptions = await _pdRepository.GetByFilterAsync(filter);
            _logger.LogInformation("Retrieved {Count} position descriptions for staging", positionDescriptions.Count);

            if (positionDescriptions.Count == 0)
            {
                _logger.LogWarning("No position descriptions found matching filter {Filter}", filter);
                return 0;
            }

            var stagedCount = 0;
            var failedCount = 0;

            foreach (var pd in positionDescriptions)
            {
                try
                {
                    var evaluationResult = new EvaluationResult
                    {
                        PdNbr = pd.PdNbr,
                        Series = pd.Series,
                        Grade = pd.Grade,
                        OverallScore = 0m,
                        Rating = "PENDING",
                        IsCandidate = false,
                        JustificationSummary = "Staged for evaluation",
                        RawLlmResponse = string.Empty,
                        EvaluatedDate = DateTime.Now,
                        EvaluatedBy = "SYSTEM"
                    };

                    var evalId = await _evalRepository.InsertAsync(evaluationResult);
                    _logger.LogDebug("Staged PD {PdNbr} with eval ID {EvalId}", pd.PdNbr, evalId);
                    stagedCount++;
                }
                catch (OracleException ex) when (ex.Number == 1 || ex.Message.Contains("UNIQUE"))
                {
                    _logger.LogWarning("PD {PdNbr} already exists in staging table", pd.PdNbr);
                    failedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stage PD {PdNbr}", pd.PdNbr);
                    failedCount++;
                }
            }

            var stagingDuration = DateTime.Now - stagingStarted;
            _logger.LogInformation(
                "Staging completed: {StagedCount} staged, {FailedCount} failed in {Duration:F2}s",
                stagedCount, failedCount, stagingDuration.TotalSeconds);

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
