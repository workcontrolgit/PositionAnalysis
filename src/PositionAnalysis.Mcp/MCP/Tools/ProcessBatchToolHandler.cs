using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// MCP tool: score ALL pending PDs globally in parallel with progress.
/// Tool name: process_batch_all
///
/// Fetches every row with Rating = 'PENDING' from SCHEDULE_PC_EVAL,
/// then runs the 10-way parallel scoring loop with live progress notifications.
/// Unlike run_unattended_scoring (fire-and-forget), this tool is synchronous:
/// the response arrives only after all PDs have been scored.
/// </summary>
public sealed class ProcessBatchToolHandler : IMcpStreamingToolHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ParallelBatchScorer _scorer;
    private readonly ILogger<ProcessBatchToolHandler> _logger;

    public ProcessBatchToolHandler(
        IServiceScopeFactory scopeFactory,
        ParallelBatchScorer scorer,
        ILogger<ProcessBatchToolHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _scorer = scorer;
        _logger = logger;
    }

    public string Name => "process_batch_all";

    public string Description =>
        "Score ALL pending PDs globally in parallel (10-way concurrency) with live progress notifications. " +
        "Synchronous: the response arrives after all PDs complete.";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
        => InvokeStreamingAsync(arguments, null, null, cancellationToken);

    public async Task<object> InvokeStreamingAsync(
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
    {
        // Fetch all pending PD numbers upfront in a short-lived scope.
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

        _logger.LogInformation("process_batch_all: {Count} pending PDs found globally", pendingPdNbrs.Count);

        if (pendingPdNbrs.Count == 0)
            return new { total = 0, completed = 0, failed = 0, message = "No pending PDs found." };

        var result = await _scorer.RunAsync(pendingPdNbrs, reportProgressAsync, cancellationToken);
        return new { result.Total, result.Completed, result.Failed, result.Message };
    }
}
