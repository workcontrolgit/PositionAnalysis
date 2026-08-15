using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// MCP tool: score all pending PDs in specified series in parallel with progress.
/// Tool name: process_batch_by_series
///
/// Fetches every PENDING row for the given series codes, then runs the
/// 10-way parallel scoring loop with live progress notifications.
/// </summary>
public sealed class ProcessBatchBySeriesToolHandler : IMcpStreamingToolHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ParallelBatchScorer _scorer;
    private readonly ILogger<ProcessBatchBySeriesToolHandler> _logger;

    public ProcessBatchBySeriesToolHandler(
        IServiceScopeFactory scopeFactory,
        ParallelBatchScorer scorer,
        ILogger<ProcessBatchBySeriesToolHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _scorer = scorer;
        _logger = logger;
    }

    public string Name => "process_batch_by_series";

    public string Description =>
        "Score all pending PDs in the specified series in parallel (10-way concurrency) with live progress notifications.";

    public object InputSchema => new
    {
        type = "object",
        required = new[] { "series" },
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Occupational series codes (5-digit strings, e.g. [\"00301\",\"00560\"])."
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
        var seriesCodes = ParseSeries(arguments);
        if (seriesCodes.Count == 0)
        {
            _logger.LogWarning("process_batch_by_series called with no series codes");
            return new { total = 0, completed = 0, failed = 0, message = "No series codes provided." };
        }

        // Fetch pending PD numbers for all requested series upfront.
        var pendingPdNbrs = new List<string>();
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();

            foreach (var code in seriesCodes)
            {
                try
                {
                    var rows = await evalRepo.GetBySeriesAsync(new OccupationalSeries(code));
                    pendingPdNbrs.AddRange(
                        rows.Where(r => r.Rating == "PENDING").Select(r => r.PdNbr));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning("Skipping invalid series code '{Code}': {Message}", code, ex.Message);
                }
            }
        }

        _logger.LogInformation(
            "process_batch_by_series: {PdCount} pending PDs across series [{Series}]",
            pendingPdNbrs.Count, string.Join(", ", seriesCodes));

        if (pendingPdNbrs.Count == 0)
            return new { total = 0, completed = 0, failed = 0, message = "No pending PDs found for the specified series." };

        var result = await _scorer.RunAsync(pendingPdNbrs, reportProgressAsync, cancellationToken);
        return new { result.Total, result.Completed, result.Failed, result.Message };
    }

    private static List<string> ParseSeries(JsonElement arguments) =>
        arguments.TryGetProperty("series", out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList()
            : [];
}
