using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public sealed class ProcessBatchToolHandler : IMcpStreamingToolHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ParallelBatchScorer _scorer;
    private readonly ICostGateService _costGate;
    private readonly ILogger<ProcessBatchToolHandler> _logger;

    public ProcessBatchToolHandler(
        IServiceScopeFactory scopeFactory,
        ParallelBatchScorer scorer,
        ICostGateService costGate,
        ILogger<ProcessBatchToolHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _scorer = scorer;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "process_batch_all";

    public string Description =>
        "Score ALL pending PDs globally in parallel (10-way concurrency) with live progress notifications. " +
        "Synchronous: the response arrives after all PDs complete. " +
        "When estimated cost exceeds the threshold, returns a requiresConfirmation payload — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution when the cost gate requires confirmation."
            }
        }
    };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
        => InvokeStreamingAsync(arguments, null, null, cancellationToken);

    public async Task<object> InvokeStreamingAsync(
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        List<string> pendingPdNbrs;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();
            var allRows = await evalRepo.GetAllAsync();
            pendingPdNbrs = allRows
                .Where(r => r.Rating == "PENDING")
                .Select(r => r.PdNbr)
                .ToList();
        }

        if (!confirmed)
        {
            var estimatedCost = _costGate.Estimate(pendingPdNbrs.Count);
            _logger.LogInformation(
                "process_batch_all: awaiting confirmation for {Count} PDs (est. ${Cost:F2})",
                pendingPdNbrs.Count, estimatedCost);
            return new
            {
                requiresConfirmation = true,
                pendingCount = pendingPdNbrs.Count,
                estimatedCostUsd = estimatedCost,
                thresholdUsd = _costGate.ThresholdUsd
            };
        }

        _logger.LogInformation("process_batch_all: {Count} pending PDs found globally", pendingPdNbrs.Count);

        if (pendingPdNbrs.Count == 0)
            return new { total = 0, completed = 0, failed = 0, message = "No pending PDs found." };

        var result = await _scorer.RunAsync(pendingPdNbrs, reportProgressAsync, cancellationToken);
        return new { result.Total, result.Completed, result.Failed, result.Message };
    }
}
