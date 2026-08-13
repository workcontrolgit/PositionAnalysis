using System.Text.Json;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.AiClients;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.Application.Services;

/// <summary>
/// Orchestrates LLM-based evaluation scoring of Position Descriptions
/// Fetches PDs, generates scoring prompts, calls AI client, parses responses, and persists results
/// </summary>
public class ScoringOrchestrator : IScoringOrchestrator
{
    private readonly IAiClient _aiClient;
    private readonly IPositionAnalysisEvalRepository _evalRepository;
    private readonly IPositionDescriptionRepository _pdRepository;
    private readonly ILogger<ScoringOrchestrator> _logger;

    private const string SystemPrompt = @"You are an expert federal HR specialist evaluating position descriptions against Schedule Policy/Career (Schedule PC) criteria under Executive Order 13957.
Analyze the position description and return a structured JSON evaluation.
Be objective and ground every finding in specific language from the duties text.";

    public ScoringOrchestrator(
        IAiClient aiClient,
        IPositionAnalysisEvalRepository evalRepository,
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

            LogLlmCost(pdNbr, aiResult);
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

    public async Task RescoreBySeriesAsync(IEnumerable<string> series)
    {
        if (series == null)
            throw new ArgumentNullException(nameof(series));

        var seriesList = series.ToList();
        if (seriesList.Count == 0)
        {
            _logger.LogWarning("No series provided for rescore");
            return;
        }

        _logger.LogInformation("Starting forced rescore across {SeriesCount} series", seriesList.Count);

        int totalScored = 0;
        int totalFailed = 0;

        foreach (var seriesCode in seriesList)
        {
            if (string.IsNullOrWhiteSpace(seriesCode))
                continue;

            try
            {
                var occupationalSeries = new OccupationalSeries(seriesCode);
                var allPds = await _evalRepository.GetBySeriesAsync(occupationalSeries);

                _logger.LogInformation("Force rescoring {Count} PDs in series {Series}", allPds.Count, seriesCode);

                foreach (var result in allPds)
                {
                    try
                    {
                        await ScoreAsync(result.PdNbr);
                        totalScored++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to rescore PD {PdNbr} in series {Series}", result.PdNbr, seriesCode);
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
                _logger.LogError(ex, "Unhandled error rescoring series {Series}", seriesCode);
                totalFailed++;
            }
        }

        _logger.LogInformation("Force rescore completed: {Scored} scored, {Failed} failed", totalScored, totalFailed);
    }

    public async Task RescoreAllAsync()
    {
        var counts = await _evalRepository.GetSeriesCountsAsync();
        var allSeries = counts.Select(c => c.Series).ToList();

        if (allSeries.Count == 0)
        {
            _logger.LogWarning("RescoreAllAsync: no staged series found");
            return;
        }

        _logger.LogInformation("RescoreAllAsync: force rescoring all {Count} staged series", allSeries.Count);
        await RescoreBySeriesAsync(allSeries);
    }

    /// <summary>
    /// Scores all PENDING Position Descriptions across every staged series
    /// </summary>
    public async Task ScoreAllAsync()
    {
        var workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        const int leaseMinutes = 15;

        var recoveredCount = await _evalRepository.RecoverExpiredClaimsAsync();
        _logger.LogInformation("ScoreAllAsync worker {WorkerId} recovered {RecoveredCount} expired claims", workerId, recoveredCount);

        while (await _evalRepository.ClaimNextPendingAsync(workerId, TimeSpan.FromMinutes(leaseMinutes)) is { } claimedResult)
        {
            await ScoreClaimedPdAsync(claimedResult, workerId);
        }

        _logger.LogInformation("ScoreAllAsync worker {WorkerId} found no more pending claims", workerId);
    }

    private async Task ScoreClaimedPdAsync(EvaluationResult claimedResult, string workerId)
    {
        EvaluationResult completedResult;

        try
        {
            var pd = await _pdRepository.GetByPdNbrAsync(claimedResult.PdNbr);
            if (pd is null)
            {
                completedResult = CreateFailedClaimResult(claimedResult, "PD not found");
            }
            else
            {
                var aiResult = await _aiClient.CompleteAsync(GenerateEvaluationPrompt(pd), SystemPrompt);
                if (aiResult.IsSuccess)
                    LogLlmCost(claimedResult.PdNbr, aiResult);
                completedResult = aiResult.IsSuccess
                    ? ParseLlmResponse(pd, aiResult.Content)
                    : CreateFailedClaimResult(claimedResult, $"AI error: {aiResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error scoring claimed PD {PdNbr}", claimedResult.PdNbr);
            completedResult = CreateFailedClaimResult(claimedResult, $"Unexpected error: {ex.Message}");
        }

        try
        {
            if (!await _evalRepository.CompleteClaimAsync(completedResult, workerId))
            {
                _logger.LogWarning("Lost or expired claim for PD {PdNbr}; completion was not applied", claimedResult.PdNbr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete claimed PD {PdNbr}; continuing worker loop", claimedResult.PdNbr);
        }
    }

    private static EvaluationResult CreateFailedClaimResult(EvaluationResult claimedResult, string errorMessage) => new()
    {
        PdSeqNum = claimedResult.PdSeqNum,
        PdNbr = claimedResult.PdNbr,
        Series = claimedResult.Series,
        Grade = claimedResult.Grade,
        Rating = "FAILED",
        JustificationSummary = errorMessage,
        OverallScore = 0,
        EvaluatedDate = DateTime.Now,
        EvaluatedBy = "LLM"
    };

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
            $"- Duty {d.SequenceNumber}: {d.Text} ({d.PercentTimeAllotted}% of time){(d.IsCritical ? " [CRITICAL]" : "")}"));

        return $@"Evaluate this federal position description against the four Schedule PC criteria:

POSITION: {pd.Title}
SERIES: {pd.Series}
GRADE: {pd.Grade}
ORGANIZATION: {pd.OrganizationName}

INTRODUCTION:
{pd.IntroText}

MAJOR DUTIES:
{dutiesSummary}

Return ONLY valid JSON in exactly this structure — no markdown fences, no additional text:
{{
  ""score"": <0-100 numeric overall score>,
  ""rating"": ""HIGH"" | ""MEDIUM"" | ""LOW"" | ""DOES_NOT_MEET"",
  ""justification"": ""<one-paragraph overall justification>"",
  ""isCandidate"": true | false,
  ""positionPurpose"": ""<1-2 objective sentences summarizing the position's overarching purpose, based on both the introduction and the major duties>"",
  ""criteria"": [
    {{
      ""name"": ""Policy-Determining"",
      ""triggered"": true | false,
      ""evidence"": ""<specific duty language supporting the finding>"",
      ""supportingDutyNumbers"": [<Duty number(s) above that this finding is based on>]
    }},
    {{
      ""name"": ""Policy-Making"",
      ""triggered"": true | false,
      ""evidence"": ""<specific duty language>"",
      ""supportingDutyNumbers"": [<Duty number(s) above that this finding is based on>]
    }},
    {{
      ""name"": ""Policy-Advocating"",
      ""triggered"": true | false,
      ""evidence"": ""<specific duty language>"",
      ""supportingDutyNumbers"": [<Duty number(s) above that this finding is based on>]
    }},
    {{
      ""name"": ""Confidential"",
      ""triggered"": true | false,
      ""evidence"": ""<specific duty language>"",
      ""supportingDutyNumbers"": [<Duty number(s) above that this finding is based on>]
    }}
  ]
}}

Schedule PC Criterion definitions:
- Policy-Determining: Position has authority to establish or set agency policy with significant discretion.
- Policy-Making: Position participates substantively in developing or formulating policy proposals.
- Policy-Advocating: Position represents the agency in advocating for policy positions to external parties.
- Confidential: Position requires a close confidential working relationship with a Schedule PC official.

Only list a duty number in supportingDutyNumbers if that specific duty's text actually supports the finding — do not list duties (e.g. ""other duties as assigned"") that provide no relevant evidence. Use an empty array if triggered is false.

positionPurpose must be descriptive, not evaluative, and should synthesize both the INTRODUCTION and MAJOR DUTIES sections above — do not just restate the introduction.";
    }

    private static string StripMarkdownFences(string response)
    {
        var trimmed = response.Trim();
        // Strip ```json ... ``` or ``` ... ```
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3].TrimEnd();
        }
        return trimmed.Trim();
    }

    private EvaluationResult ParseLlmResponse(PositionDescription pd, string llmResponse)
    {
        _logger.LogDebug("Parsing LLM response for {PdNbr}: {ResponseLength} characters",
            pd.PdNbr, llmResponse.Length);

        llmResponse = StripMarkdownFences(llmResponse);

        var evaluationResult = new EvaluationResult
        {
            PdSeqNum = pd.PdSeqNum,
            PdNbr = pd.PdNbr,
            Series = pd.Series,
            Grade = pd.Grade,
            EvaluatedDate = DateTime.Now,
            EvaluatedBy = "LLM",
            RawLlmResponse = llmResponse
        };

        try
        {
            // LLM output sometimes includes trailing commas; tolerate them.
            using var doc = JsonDocument.Parse(llmResponse, new JsonDocumentOptions { AllowTrailingCommas = true });
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

            if (root.TryGetProperty("positionPurpose", out var purposeElement) && purposeElement.ValueKind == JsonValueKind.String)
            {
                evaluationResult.PositionPurpose = purposeElement.GetString() ?? "";
            }

            if (root.TryGetProperty("criteria", out var criteriaElement) && criteriaElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var criterion in criteriaElement.EnumerateArray())
                {
                    var cs = new CriterionScore();

                    if (criterion.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        cs.CriterionName = nameEl.GetString() ?? "";

                    if (criterion.TryGetProperty("triggered", out var triggeredEl))
                    {
                        if (triggeredEl.ValueKind == JsonValueKind.True)
                            cs.Triggered = true;
                        else if (triggeredEl.ValueKind == JsonValueKind.False)
                            cs.Triggered = false;
                    }

                    if (criterion.TryGetProperty("evidence", out var evidenceEl) && evidenceEl.ValueKind == JsonValueKind.String)
                        cs.Evidence = evidenceEl.GetString() ?? "";

                    if (criterion.TryGetProperty("supportingDutyNumbers", out var dutyNumsEl) && dutyNumsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dutyNumEl in dutyNumsEl.EnumerateArray())
                        {
                            if (dutyNumEl.TryGetInt32(out var dutyNum))
                                cs.SupportingDutyNumbers.Add(dutyNum);
                        }
                    }

                    evaluationResult.CriteriaScores.Add(cs);
                }
            }

            if (root.TryGetProperty("isCandidate", out var isCandEl) &&
                (isCandEl.ValueKind == JsonValueKind.True || isCandEl.ValueKind == JsonValueKind.False))
                evaluationResult.IsCandidate = isCandEl.GetBoolean();
            else
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

    private void LogLlmCost(string pdNbr, AiCompletionResult aiResult)
    {
        _logger.LogInformation(
            "PD {PdNbr} LLM cost: {PromptTokens:N0} prompt + {CompletionTokens:N0} completion = {TotalTokens:N0} tokens, ~${Cost:F4}",
            pdNbr, aiResult.PromptTokens, aiResult.CompletionTokens, aiResult.TokensUsed, aiResult.EstimatedCostUsd);
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
