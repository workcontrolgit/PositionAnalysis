using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class RescorePdsBySeriesToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICostGateService _costGate;
    private readonly ILogger<RescorePdsBySeriesToolHandler> _logger;

    public RescorePdsBySeriesToolHandler(
        IScoringOrchestrator scoringOrchestrator,
        IServiceScopeFactory scopeFactory,
        ICostGateService costGate,
        ILogger<RescorePdsBySeriesToolHandler> logger)
    {
        _scoringOrchestrator = scoringOrchestrator;
        _scopeFactory = scopeFactory;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "rescore_by_series";

    public string Description =>
        "Force a fresh LLM rescore of all PDs in the specified occupational series, overwriting existing results regardless of status. " +
        "When estimated cost exceeds the threshold, returns a requiresConfirmation payload — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of 5-digit occupational series codes to rescore (e.g. ['00110', '00301'])"
            },
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution when the cost gate requires confirmation."
            }
        },
        required = new[] { "series" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("series", out var seriesEl) ||
            seriesEl.ValueKind != JsonValueKind.Array)
            return new { error = "Missing required parameter: series (array of series codes)" };

        var series = seriesEl.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (series.Count == 0)
            return new { error = "series array cannot be empty" };

        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            int totalCount = 0;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();
                foreach (var code in series)
                {
                    try
                    {
                        var rows = await evalRepo.GetBySeriesAsync(new OccupationalSeries(code));
                        totalCount += rows.Count;
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogWarning("Skipping invalid series code '{Code}': {Message}", code, ex.Message);
                    }
                }
            }

            var estimatedCost = _costGate.Estimate(totalCount);
            _logger.LogInformation(
                "rescore_by_series: awaiting confirmation for {Count} PDs (est. ${Cost:F2})",
                totalCount, estimatedCost);
            return new
            {
                requiresConfirmation = true,
                pendingCount = totalCount,
                estimatedCostUsd = estimatedCost,
                thresholdUsd = _costGate.ThresholdUsd
            };
        }

        await _scoringOrchestrator.RescoreBySeriesAsync(series);

        return new
        {
            series,
            status = $"Rescore complete for series: {string.Join(", ", series)}. Run generate_documents_all to regenerate Word forms."
        };
    }
}
