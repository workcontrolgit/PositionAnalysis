using System.Text.RegularExpressions;
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
    private readonly IPositionDescriptionRepository _pdRepository;
    private readonly IDocumentGenerationStrategyFactory _documentFactory;
    private readonly OutputSettings _outputSettings;
    private readonly ILogger<DocumentGenerationOrchestrator> _logger;
    private readonly DocumentGenerationSettings _documentSettings;

    public DocumentGenerationOrchestrator(
        ISchedulePCEvalRepository evalRepository,
        IPositionDescriptionRepository pdRepository,
        IDocumentGenerationStrategyFactory documentFactory,
        OutputSettings outputSettings,
        ILogger<DocumentGenerationOrchestrator> logger,
        IOptions<DocumentGenerationSettings> options)
    {
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _pdRepository = pdRepository ?? throw new ArgumentNullException(nameof(pdRepository));
        _documentFactory = documentFactory ?? throw new ArgumentNullException(nameof(documentFactory));
        _outputSettings = outputSettings ?? throw new ArgumentNullException(nameof(outputSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _documentSettings = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    public async Task GenerateAllAsync()
    {
        _logger.LogInformation("Starting Word generation for all completed evaluations");

        var completedResults = await _evalRepository.GetByStatusAsync(EvaluationStatus.Complete);

        if (completedResults.Count == 0)
        {
            _logger.LogInformation("No completed results found; nothing to generate");
            return;
        }

        var (succeeded, failed) = await GenerateInternalAsync(completedResults);

        _logger.LogInformation("Generated {Count} Word documents ({Failed} failed)", succeeded, failed);
    }

    public async Task GenerateBySeriesAsync(IEnumerable<string> series)
    {
        if (series == null)
            throw new ArgumentNullException(nameof(series));

        var seriesList = series
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (seriesList.Count == 0)
        {
            _logger.LogInformation("No series provided; nothing to generate");
            return;
        }

        _logger.LogInformation("Starting Word generation across {SeriesCount} series", seriesList.Count);

        var selectedResults = new List<EvaluationResult>();
        foreach (var seriesCode in seriesList)
        {
            try
            {
                var occupationalSeries = new OccupationalSeries(seriesCode);
                var seriesResults = await _evalRepository.GetBySeriesAsync(occupationalSeries);
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
            _logger.LogInformation("No completed results found in requested series");
            return;
        }

        var (succeeded, failed) = await GenerateInternalAsync(dedupedResults);

        _logger.LogInformation(
            "Generated {Count} Word documents in selected series ({Failed} failed)",
            succeeded, failed);
    }

    public async Task<int> GetGenerationProgressAsync()
    {
        var completedResults = await _evalRepository.GetByStatusAsync(EvaluationStatus.Complete);

        _logger.LogInformation("Document generation progress: {Total} completed evaluations in DB", completedResults.Count);

        return completedResults.Count;
    }

    private async Task<(int succeeded, int failed)> GenerateInternalAsync(IEnumerable<EvaluationResult> results)
    {
        var successCount = 0;
        var failureCount = 0;

        foreach (var result in results)
        {
            string outputFilePath = string.Empty;
            try
            {
                var pd = await _pdRepository.GetByPdNbrAsync(result.PdNbr);
                if (pd == null)
                {
                    _logger.LogWarning("PD {PdNbr} not found in Oracle; skipping document generation", result.PdNbr);
                    failureCount++;
                    continue;
                }

                outputFilePath = _outputSettings.GetWordOutputPath(BuildFileName(pd));

                _logger.LogDebug(
                    "Generating Word document for PD {PdNbr}, output {OutputFile}",
                    result.PdNbr,
                    outputFilePath);

                var strategy = _documentFactory.CreateStrategy();
                await strategy.GenerateAsync(result, pd, outputFilePath);
                successCount++;
            }
            catch (FileNotFoundException ex)
            {
                failureCount++;
                _logger.LogError(
                    ex,
                    "Template not found while generating document for PD {PdNbr}. Template setting: {TemplatePath}",
                    result.PdNbr,
                    _documentSettings.TemplateFile);
                await MarkGenerationFailedAsync(result, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                failureCount++;
                _logger.LogError(
                    ex,
                    "Permission error while writing document for PD {PdNbr} to {OutputPath}",
                    result.PdNbr,
                    outputFilePath);
                await MarkGenerationFailedAsync(result, ex.Message);
            }
            catch (IOException ex)
            {
                failureCount++;
                _logger.LogError(
                    ex,
                    "I/O error while generating document for PD {PdNbr} to {OutputPath}",
                    result.PdNbr,
                    outputFilePath);
                await MarkGenerationFailedAsync(result, ex.Message);
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(
                    ex,
                    "Strategy error while generating document for PD {PdNbr}",
                    result.PdNbr);
                await MarkGenerationFailedAsync(result, ex.Message);
            }
        }

        return (successCount, failureCount);
    }

    private static string BuildFileName(PositionDescription pd)
    {
        // Source titles sometimes carry a leading numeric code, e.g. "015 - FOREIGN AFFAIRS OFFICER"; drop it for the filename.
        var title = Regex.Replace(pd.Title, @"^\d+\s*-\s*", "");
        var titleSlug = Regex.Replace(title, @"[^a-zA-Z0-9 -]", "");
        titleSlug = Regex.Replace(titleSlug, @"\s+", "-");
        return $"PD-{pd.PdNbr}_{titleSlug}_{pd.PayPlan}-{pd.Series.Code}-{pd.Grade.Value:D2}.docx";
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
                "Failed to persist generation failure status for PD {PdNbr}",
                result.PdNbr);
        }
    }

    private static bool IsCompletedResult(EvaluationResult result)
    {
        return !string.Equals(result.Rating, PendingRating, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(result.Rating, "FAILED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(result.Rating, GenerationFailedRating, StringComparison.OrdinalIgnoreCase);
    }
}
