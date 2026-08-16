using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using PositionAnalysis.Mcp.MCP;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class RunUnattendedScoringToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;
    private readonly ProcessAllRunStatusService _runStatusService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICostGateService _costGate;
    private readonly ILogger<RunUnattendedScoringToolHandler> _logger;

    public RunUnattendedScoringToolHandler(
        IScoringOrchestrator scoringOrchestrator,
        ProcessAllRunStatusService runStatusService,
        IServiceScopeFactory scopeFactory,
        ICostGateService costGate,
        ILogger<RunUnattendedScoringToolHandler> logger)
    {
        _scoringOrchestrator = scoringOrchestrator;
        _runStatusService = runStatusService;
        _scopeFactory = scopeFactory;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "run_unattended_scoring";

    public string Description =>
        "Start asynchronous scoring for all staged PENDING PDs across every occupational series. " +
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

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            int pendingCount;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();
                var allRows = await evalRepo.GetAllAsync();
                pendingCount = allRows.Count(r => r.Rating == "PENDING");
            }

            var estimatedCost = _costGate.Estimate(pendingCount);
            _logger.LogInformation(
                "run_unattended_scoring: awaiting confirmation for {Count} PDs (est. ${Cost:F2})",
                pendingCount, estimatedCost);
            return new
            {
                requiresConfirmation = true,
                pendingCount,
                estimatedCostUsd = estimatedCost,
                thresholdUsd = _costGate.ThresholdUsd
            };
        }

        _ = _runStatusService.StartAsync(_scoringOrchestrator.ScoreAllAsync);

        return new
        {
            status = "processing_started",
            message = "Scoring all staged PDs across all series. Poll get_unattended_queue_status to track progress."
        };
    }
}
