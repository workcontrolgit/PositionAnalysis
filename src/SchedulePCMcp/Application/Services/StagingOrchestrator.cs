using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.Enums;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Infrastructure.Repositories;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.Application.Services;

/// <summary>
/// Orchestrates staging of Position Descriptions for evaluation runs
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
    /// Stages Position Descriptions for a new evaluation run
    /// </summary>
    public async Task<string> StageAsync(StagingFilter filter)
    {
        var runId = $"RUN_{DateTime.Now:yyyyMMdd_HHmmss}";
        var stagingStarted = DateTime.Now;

        try
        {
            _logger.LogInformation("Starting staging run {RunId} with filter: {Filter}", runId, filter);

            // Query Position Descriptions using the filter
            var positionDescriptions = await _pdRepository.GetByFilterAsync(filter);
            _logger.LogInformation("Retrieved {Count} position descriptions for staging", positionDescriptions.Count);

            if (positionDescriptions.Count == 0)
            {
                _logger.LogWarning("No position descriptions found matching filter {Filter}", filter);
                return runId;
            }

            var stagedCount = 0;
            var failedCount = 0;

            // Stage each Position Description
            foreach (var pd in positionDescriptions)
            {
                try
                {
                    // Create an EvaluationResult for staging
                    var evaluationResult = new EvaluationResult
                    {
                        RunId = runId,
                        PdNbr = pd.PdNbr,
                        Series = pd.Series,
                        Grade = pd.Grade,
                        OverallScore = 0m,
                        Rating = "PENDING",
                        IsCandidate = false,
                        JustificationSummary = $"Staged for evaluation",
                        RawLlmResponse = string.Empty,
                        EvaluatedDate = DateTime.Now,
                        EvaluatedBy = "SYSTEM"
                    };

                    // Insert to SCHEDULE_PC_EVAL
                    var evalId = await _evalRepository.InsertAsync(evaluationResult);
                    _logger.LogDebug("Staged PD {PdNbr} with eval ID {EvalId}", pd.PdNbr, evalId);
                    stagedCount++;
                }
                catch (OracleException ex) when (ex.Number == 1 || ex.Message.Contains("UNIQUE"))
                {
                    // PD already staged in this run
                    _logger.LogWarning("PD {PdNbr} already exists in run {RunId}", pd.PdNbr, runId);
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
                "Staging completed for run {RunId}: {StagedCount} staged, {FailedCount} failed in {Duration:F2}s",
                runId, stagedCount, failedCount, stagingDuration.TotalSeconds);

            return runId;
        }
        catch (OracleException ex)
        {
            _logger.LogError(ex, "Oracle error during staging run {RunId}", runId);
            throw new InvalidOperationException($"Staging run {runId} failed due to database error", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during staging run {RunId}", runId);
            throw new InvalidOperationException($"Staging run {runId} failed unexpectedly", ex);
        }
    }

    /// <summary>
    /// Retrieves metadata for a specific staging/evaluation run
    /// </summary>
    public async Task<RunMetadata?> GetRunMetadataAsync(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run ID cannot be null or empty", nameof(runId));

        try
        {
            _logger.LogInformation("Retrieving metadata for run {RunId}", runId);

            // Query evaluation results for this run
            var evaluationResults = await _evalRepository.GetByRunAsync(runId);

            if (evaluationResults.Count == 0)
            {
                _logger.LogWarning("No evaluation results found for run {RunId}", runId);
                return null;
            }

            // Extract first result to get timestamps (assumes all results in run have same timing)
            var firstResult = evaluationResults.First();

            // Calculate counts by status
            var stagedCount = evaluationResults.Count(r => r.Rating == "PENDING");
            var scoredCount = evaluationResults.Count(r => r.Rating != "PENDING");

            var runMetadata = new RunMetadata
            {
                RunId = runId,
                CreatedDate = firstResult.EvaluatedDate,
                CreatedBy = firstResult.EvaluatedBy,
                TotalPdCount = evaluationResults.Count,
                StagedPdCount = stagedCount,
                ScoredPdCount = scoredCount,
                FailedScoringCount = 0
            };

            _logger.LogInformation("Retrieved metadata for run {RunId}: {Metadata}", runId, runMetadata);
            return runMetadata;
        }
        catch (OracleException ex)
        {
            _logger.LogError(ex, "Oracle error retrieving metadata for run {RunId}", runId);
            throw new InvalidOperationException($"Failed to retrieve metadata for run {runId}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving metadata for run {RunId}", runId);
            throw new InvalidOperationException($"Failed to retrieve metadata for run {runId}", ex);
        }
    }
}
