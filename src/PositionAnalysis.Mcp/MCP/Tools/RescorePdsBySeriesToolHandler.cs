using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class RescorePdsBySeriesToolHandler : IMcpStreamingToolHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ParallelBatchScorer _scorer;
    private readonly ICostGateService _costGate;
    private readonly ILogger<RescorePdsBySeriesToolHandler> _logger;

    public RescorePdsBySeriesToolHandler(
        IServiceScopeFactory scopeFactory,
        ParallelBatchScorer scorer,
        ICostGateService costGate,
        ILogger<RescorePdsBySeriesToolHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _scorer = scorer;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "rescore_by_series";

    public string Description =>
        "Force a fresh LLM rescore of all PDs in the specified occupational series, in parallel, overwriting existing results regardless of status. " +
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

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
        => InvokeStreamingAsync(arguments, null, null, cancellationToken);

    public async Task<object> InvokeStreamingAsync(
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
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

        List<string> pdNbrs = new();
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();
            foreach (var code in series)
            {
                try
                {
                    var rows = await evalRepo.GetBySeriesAsync(new OccupationalSeries(code));
                    pdNbrs.AddRange(rows.Select(r => r.PdNbr));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning("Skipping invalid series code '{Code}': {Message}", code, ex.Message);
                }
            }
        }

        if (!confirmed)
        {
            var estimatedCost = _costGate.Estimate(pdNbrs.Count);
            _logger.LogInformation(
                "rescore_by_series: awaiting confirmation for {Count} PDs (est. ${Cost:F2})",
                pdNbrs.Count, estimatedCost);
            return new
            {
                requiresConfirmation = true,
                pendingCount = pdNbrs.Count,
                estimatedCostUsd = estimatedCost,
                thresholdUsd = _costGate.ThresholdUsd
            };
        }

        var result = await _scorer.RunAsync(pdNbrs, reportProgressAsync, cancellationToken);

        return new
        {
            series,
            result.Total,
            result.Completed,
            result.Failed,
            status = $"{result.Message} Run generate_documents_all to regenerate Word forms."
        };
    }
}
