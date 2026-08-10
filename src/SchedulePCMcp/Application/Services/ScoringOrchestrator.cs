using System.Text.Json;
using Microsoft.Extensions.Logging;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Infrastructure.AiClients;
using SchedulePCMcp.Infrastructure.Repositories;

namespace SchedulePCMcp.Application.Services;

/// <summary>
/// Orchestrates LLM-based evaluation scoring of Position Descriptions
/// Fetches PDs, generates scoring prompts, calls AI client, parses responses, and persists results
/// </summary>
public class ScoringOrchestrator : IScoringOrchestrator
{
    private readonly IAiClient _aiClient;
    private readonly ISchedulePCEvalRepository _evalRepository;
    private readonly IPositionDescriptionRepository _pdRepository;
    private readonly ILogger<ScoringOrchestrator> _logger;

    private const string SystemPrompt = @"You are an expert HR specialist evaluating federal position descriptions against Schedule PC criteria.
Analyze the position description and provide a structured JSON evaluation.
Be objective, precise, and focus on alignment with required qualifications and job functions.";

    public ScoringOrchestrator(
        IAiClient aiClient,
        ISchedulePCEvalRepository evalRepository,
        IPositionDescriptionRepository pdRepository,
        ILogger<ScoringOrchestrator> logger)
    {
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _pdRepository = pdRepository ?? throw new ArgumentNullException(nameof(pdRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Scores a single Position Description using LLM evaluation
    /// Fetches the PD, generates prompt, calls AI, parses result, and persists to database
    /// </summary>
    public async Task ScoreAsync(string pdNbr)
    {
        if (string.IsNullOrWhiteSpace(pdNbr))
            throw new ArgumentException("Position description number cannot be null or empty", nameof(pdNbr));

        _logger.LogInformation("Starting evaluation scoring for PD {PdNbr}", pdNbr);

        try
        {
            var pd = await _pdRepository.GetByPdNbrAsync(pdNbr);
            if (pd == null)
            {
                _logger.LogError("Position description {PdNbr} not found", pdNbr);
                await UpdateResultStatusAsync(pdNbr, "FAILED", "PD not found");
                return;
            }

            _logger.LogDebug("Retrieved PD {PdNbr}: {Title} ({Series}/{Grade})",
                pd.PdNbr, pd.Title, pd.Series, pd.Grade);

            var userPrompt = GenerateEvaluationPrompt(pd);

            _logger.LogDebug("Calling AI client for LLM evaluation of {PdNbr}", pdNbr);
            var aiResult = await _aiClient.CompleteAsync(userPrompt, SystemPrompt);

            if (!aiResult.IsSuccess)
            {
                _logger.LogError("AI client failed for {PdNbr}: {ErrorMessage}", pdNbr, aiResult.ErrorMessage);
                await UpdateResultStatusAsync(pdNbr, "FAILED", $"AI error: {aiResult.ErrorMessage}");
                return;
            }

            var evaluationResult = ParseLlmResponse(pd, aiResult.Content);

            await _evalRepository.UpdateAsync(evaluationResult);
            _logger.LogInformation(
                "Successfully scored PD {PdNbr} with rating {Rating} (score: {Score:F1})",
                pdNbr, evaluationResult.Rating, evaluationResult.OverallScore);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error while scoring {PdNbr}", pdNbr);
            await UpdateResultStatusAsync(pdNbr, "FAILED", $"JSON parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error scoring {PdNbr}", pdNbr);
            await UpdateResultStatusAsync(pdNbr, "FAILED", $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Scores all Position Descriptions for specified occupational series
    /// Iterates through series and calls ScoreAsync for each PD
    /// </summary>
    public async Task ScoreBySeriesAsync(IEnumerable<string> series)
    {
        if (series == null)
            throw new ArgumentNullException(nameof(series));

        var seriesList = series.ToList();
        if (seriesList.Count == 0)
        {
            _logger.LogWarning("No series provided for scoring");
            return;
        }

        _logger.LogInformation("Starting batch scoring across {SeriesCount} series", seriesList.Count);

        int totalScored = 0;
        int totalFailed = 0;

        foreach (var seriesCode in seriesList)
        {
            if (string.IsNullOrWhiteSpace(seriesCode))
            {
                _logger.LogWarning("Skipping empty series code");
                continue;
            }

            try
            {
                var occupationalSeries = new OccupationalSeries(seriesCode);

                _logger.LogInformation("Processing series {Series}", seriesCode);

                var pdsToScore = await _evalRepository.GetBySeriesAsync(occupationalSeries);
                var unscored = pdsToScore.Where(r => r.Rating == "PENDING").ToList();

                _logger.LogDebug("Found {Count} unscored PDs for series {Series}", unscored.Count, seriesCode);

                foreach (var result in unscored)
                {
                    try
                    {
                        await ScoreAsync(result.PdNbr);
                        totalScored++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to score PD {PdNbr} in series {Series}",
                            result.PdNbr, seriesCode);
                        totalFailed++;
                    }
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid series code {Series}", seriesCode);
                totalFailed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing series {Series}", seriesCode);
                totalFailed++;
            }
        }

        _logger.LogInformation(
            "Batch scoring completed: {Scored} scored, {Failed} failed",
            totalScored, totalFailed);
    }

    /// <summary>
    /// Retrieves the evaluation result for a specific PD
    /// </summary>
    public async Task<EvaluationResult?> GetResultAsync(string pdNbr)
    {
        if (string.IsNullOrWhiteSpace(pdNbr))
            throw new ArgumentException("Position description number cannot be null or empty", nameof(pdNbr));

        try
        {
            _logger.LogDebug("Retrieving evaluation result for {PdNbr}", pdNbr);
            var result = await _evalRepository.GetByPdAsync(pdNbr);

            if (result == null)
            {
                _logger.LogWarning("No evaluation result found for {PdNbr}", pdNbr);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving result for {PdNbr}", pdNbr);
            throw;
        }
    }

    private string GenerateEvaluationPrompt(PositionDescription pd)
    {
        var dutiesSummary = string.Join("\n", pd.Duties.Select(d =>
            $"- {d.Text} ({d.PercentTimeAllotted}% of time){(d.IsCritical ? " [CRITICAL]" : "")}"));

        return $@"Evaluate this federal position description:

POSITION: {pd.Title}
SERIES: {pd.Series}
GRADE: {pd.Grade}
ORGANIZATION: {pd.OrganizationName}

INTRODUCTION:
{pd.IntroText}

MAJOR DUTIES:
{dutiesSummary}

Provide a detailed JSON evaluation with the following structure:
{{
  ""score"": <0-100 numeric score>,
  ""rating"": ""HIGH"" | ""MEDIUM"" | ""LOW"" | ""DOES_NOT_MEET"",
  ""justification"": ""<brief summary of overall evaluation>"",
  ""criteria"": [
    {{
      ""name"": ""<criterion name>"",
      ""score"": <0-100>,
      ""justification"": ""<explanation>""
    }}
  ]
}}

Evaluate based on:
1. Role clarity and specificity
2. Qualification requirements alignment
3. Duty distribution and criticality
4. Career progression potential
5. Schedule PC policy compliance

Return ONLY valid JSON, no additional text.";
    }

    private EvaluationResult ParseLlmResponse(PositionDescription pd, string llmResponse)
    {
        _logger.LogDebug("Parsing LLM response for {PdNbr}: {ResponseLength} characters",
            pd.PdNbr, llmResponse.Length);

        var evaluationResult = new EvaluationResult
        {
            PdNbr = pd.PdNbr,
            Series = pd.Series,
            Grade = pd.Grade,
            EvaluatedDate = DateTime.Now,
            EvaluatedBy = "LLM",
            RawLlmResponse = llmResponse
        };

        try
        {
            using var doc = JsonDocument.Parse(llmResponse);
            var root = doc.RootElement;

            if (root.TryGetProperty("score", out var scoreElement) && scoreElement.TryGetDecimal(out var score))
            {
                evaluationResult.OverallScore = Math.Clamp(score, 0, 100);
            }

            if (root.TryGetProperty("rating", out var ratingElement) && ratingElement.ValueKind == JsonValueKind.String)
            {
                var rating = ratingElement.GetString()?.ToUpper() ?? "DOES_NOT_MEET";
                evaluationResult.Rating = ValidateRating(rating);
            }

            if (root.TryGetProperty("justification", out var justElement) && justElement.ValueKind == JsonValueKind.String)
            {
                evaluationResult.JustificationSummary = justElement.GetString() ?? "";
            }

            if (root.TryGetProperty("criteria", out var criteriaElement) && criteriaElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var criterion in criteriaElement.EnumerateArray())
                {
                    var criterionScore = new CriterionScore();

                    if (criterion.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        criterionScore.CriterionName = nameEl.GetString() ?? "";

                    if (criterion.TryGetProperty("score", out var crScoreEl) && crScoreEl.TryGetDecimal(out var crScore))
                        criterionScore.Score = Math.Clamp(crScore, 0, 100);

                    if (criterion.TryGetProperty("justification", out var crJustEl) && crJustEl.ValueKind == JsonValueKind.String)
                        criterionScore.Justification = crJustEl.GetString() ?? "";

                    evaluationResult.CriteriaScores.Add(criterionScore);
                }
            }

            evaluationResult.IsCandidate = evaluationResult.Rating == "HIGH" ||
                                          evaluationResult.Rating == "MEDIUM";

            _logger.LogDebug("Successfully parsed LLM response: rating={Rating}, score={Score:F1}, criteria={CriteriaCount}",
                evaluationResult.Rating, evaluationResult.OverallScore, evaluationResult.CriteriaScores.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM JSON response for {PdNbr}", pd.PdNbr);
            evaluationResult.Rating = "FAILED";
            evaluationResult.JustificationSummary = $"JSON parsing error: {ex.Message}";
            evaluationResult.OverallScore = 0;
        }

        return evaluationResult;
    }

    private string ValidateRating(string rating)
    {
        return rating switch
        {
            "HIGH" => "HIGH",
            "MEDIUM" => "MEDIUM",
            "LOW" => "LOW",
            "DOES_NOT_MEET" => "DOES_NOT_MEET",
            _ => "DOES_NOT_MEET"
        };
    }

    private async Task UpdateResultStatusAsync(string pdNbr, string status, string errorMessage)
    {
        try
        {
            var result = await _evalRepository.GetByPdAsync(pdNbr);
            if (result != null)
            {
                result.Rating = status;
                result.JustificationSummary = errorMessage;
                result.OverallScore = 0;
                await _evalRepository.UpdateAsync(result);
                _logger.LogError("Updated PD {PdNbr} status to {Status}: {Message}", pdNbr, status, errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for {PdNbr}", pdNbr);
        }
    }
}
