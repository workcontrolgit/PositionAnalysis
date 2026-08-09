using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.Enums;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Infrastructure.Config;
using SchedulePCMcp.Infrastructure.DocumentGeneration;
using SchedulePCMcp.Infrastructure.Repositories;

namespace SchedulePCMcp.Application.Services;

/// <summary>
/// Orchestrates Word document generation for completed Schedule PC evaluation results.
/// </summary>
public class DocumentGenerationOrchestrator : IDocumentGenerationOrchestrator
{
    private const string PendingRating = "PENDING";
    private const string GenerationFailedRating = "GENERATION_FAILED";

    private readonly ISchedulePCEvalRepository _evalRepository;
    private readonly IDocumentGenerationStrategyFactory _documentFactory;
    private readonly OutputSettings _outputSettings;
    private readonly ILogger<DocumentGenerationOrchestrator> _logger;
    private readonly DocumentGenerationSettings _documentSettings;

    public DocumentGenerationOrchestrator(
        ISchedulePCEvalRepository evalRepository,
        IDocumentGenerationStrategyFactory documentFactory,
        OutputSettings outputSettings,
        ILogger<DocumentGenerationOrchestrator> logger,
        IOptions<DocumentGenerationSettings> options)
    {
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _documentFactory = documentFactory ?? throw new ArgumentNullException(nameof(documentFactory));
        _outputSettings = outputSettings ?? throw new ArgumentNullException(nameof(outputSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _documentSettings = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    public async Task GenerateByRunAsync(string runId)
    {
        ValidateRunId(runId);

        _logger.LogInformation("Starting Word generation for run {RunId}", runId);

        var completedResults = (await _evalRepository.GetByStatusAsync(EvaluationStatus.Complete))
            .Where(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (completedResults.Count == 0)
        {
            _logger.LogInformation("No completed results found for run {RunId}; nothing to generate", runId);
            return;
        }

        var (succeeded, failed) = await GenerateInternalAsync(runId, completedResults);

        _logger.LogInformation(
            "Generated {Count} Word documents for run {RunId} ({Failed} failed)",
            succeeded,
            runId,
            failed);
    }

    public async Task GenerateBySeriesAsync(string runId, IEnumerable<string> series)
    {
        ValidateRunId(runId);

        if (series == null)
            throw new ArgumentNullException(nameof(series));

        var seriesList = series
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (seriesList.Count == 0)
        {
            _logger.LogInformation("No series provided for run {RunId}; nothing to generate", runId);
            return;
        }

        _logger.LogInformation(
            "Starting Word generation for run {RunId} across {SeriesCount} series",
            runId,
            seriesList.Count);

        var selectedResults = new List<EvaluationResult>();
        foreach (var seriesCode in seriesList)
        {
            try
            {
                var occupationalSeries = new OccupationalSeries(seriesCode);
                var seriesResults = await _evalRepository.GetByRunAndSeriesAsync(runId, occupationalSeries);
                selectedResults.AddRange(seriesResults.Where(IsCompletedResult));
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid series code {SeriesCode}; skipping", seriesCode);
            }
        }

        var dedupedResults = selectedResults
            .GroupBy(r => r.PdNbr, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (dedupedResults.Count == 0)
        {
            _logger.LogInformation(
                "No completed results found for run {RunId} in requested series",
                runId);
            return;
        }

        var (succeeded, failed) = await GenerateInternalAsync(runId, dedupedResults);

        _logger.LogInformation(
            "Generated {Count} Word documents for run {RunId} in selected series ({Failed} failed)",
            succeeded,
            runId,
            failed);
    }

    public async Task<int> GetGenerationProgressAsync(string runId)
    {
        ValidateRunId(runId);

        var completedResults = (await _evalRepository.GetByStatusAsync(EvaluationStatus.Complete))
            .Where(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var generatedCount = 0;
        foreach (var result in completedResults)
        {
            var outputFilePath = _outputSettings.GetWordOutputPath(runId, $"{result.PdNbr}_evaluation.docx");
            if (File.Exists(outputFilePath))
                generatedCount++;
        }

        _logger.LogInformation(
            "Document generation progress for run {RunId}: {Generated}/{Total}",
            runId,
            generatedCount,
            completedResults.Count);

        return generatedCount;
    }

    private async Task<(int succeeded, int failed)> GenerateInternalAsync(string runId, IEnumerable<EvaluationResult> results)
    {
        var successCount = 0;
        var failureCount = 0;

        foreach (var result in results)
        {
            var outputFilePath = _outputSettings.GetWordOutputPath(runId, $"{result.PdNbr}_evaluation.docx");

            try
            {
                _logger.LogDebug(
                    "Generating Word document for run {RunId}, PD {PdNbr}, output {OutputFile}",
                    runId,
                    result.PdNbr,
                    outputFilePath);

                var strategy = _documentFactory.CreateStrategy();
                await strategy.GenerateAsync(result, string.Empty, outputFilePath);
                successCount++;
            }
            catch (FileNotFoundException ex)
            {
                failureCount++;
                _logger.LogError(
                    ex,
                    "Template not found while generating document for run {RunId}, PD {PdNbr}. Template setting: {TemplatePath}",
                    runId,
                    result.PdNbr,
                    _documentSettings.TemplateFile);
                await MarkGenerationFailedAsync(result, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                failureCount++;
                _logger.LogError(
                    ex,
                    "Permission error while writing document for run {RunId}, PD {PdNbr} to {OutputPath}",
                    runId,
                    result.PdNbr,
                    outputFilePath);
                await MarkGenerationFailedAsync(result, ex.Message);
            }
            catch (IOException ex)
            {
                failureCount++;
                _logger.LogError(
                    ex,
                    "I/O error while generating document for run {RunId}, PD {PdNbr} to {OutputPath}",
                    runId,
                    result.PdNbr,
                    outputFilePath);
                await MarkGenerationFailedAsync(result, ex.Message);
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(
                    ex,
                    "Strategy error while generating document for run {RunId}, PD {PdNbr}",
                    runId,
                    result.PdNbr);
                await MarkGenerationFailedAsync(result, ex.Message);
            }
        }

        return (successCount, failureCount);
    }

    private async Task MarkGenerationFailedAsync(EvaluationResult result, string reason)
    {
        try
        {
            result.Rating = GenerationFailedRating;
            result.JustificationSummary =
                $"{result.JustificationSummary}{Environment.NewLine}[DOC_GEN_FAILED] {reason}".Trim();
            result.EvaluatedDate = DateTime.Now;
            result.EvaluatedBy = "SYSTEM";

            await _evalRepository.UpdateAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist generation failure status for run {RunId}, PD {PdNbr}",
                result.RunId,
                result.PdNbr);
        }
    }

    private static bool IsCompletedResult(EvaluationResult result)
    {
        return !string.Equals(result.Rating, PendingRating, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run ID cannot be null or empty", nameof(runId));
    }
}