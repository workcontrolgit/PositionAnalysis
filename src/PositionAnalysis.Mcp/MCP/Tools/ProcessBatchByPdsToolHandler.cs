using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// MCP tool: score an explicit list of PD numbers in parallel with progress.
/// Tool name: process_batch_by_pds
/// </summary>
public sealed class ProcessBatchByPdsToolHandler : IMcpStreamingToolHandler
{
    private readonly ParallelBatchScorer _scorer;
    private readonly ILogger<ProcessBatchByPdsToolHandler> _logger;

    public ProcessBatchByPdsToolHandler(ParallelBatchScorer scorer, ILogger<ProcessBatchByPdsToolHandler> logger)
    {
        _scorer = scorer;
        _logger = logger;
    }

    public string Name => "process_batch_by_pds";

    public string Description =>
        "Score an explicit list of PD numbers in parallel (10-way concurrency) with live progress notifications.";

    public object InputSchema => new
    {
        type = "object",
        required = new[] { "pdNbrs" },
        properties = new
        {
            pdNbrs = new
            {
                type = "array",
                items = new { type = "string" },
                description = "PD numbers to score."
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
        var pdNbrs = ParsePdNbrs(arguments);

        if (pdNbrs.Count == 0)
        {
            _logger.LogWarning("process_batch_by_pds called with no PD numbers");
            return new { total = 0, completed = 0, failed = 0, message = "No PD numbers provided." };
        }

        _logger.LogInformation("process_batch_by_pds: {Count} PDs submitted", pdNbrs.Count);
        var result = await _scorer.RunAsync(pdNbrs, reportProgressAsync, cancellationToken);
        return new { result.Total, result.Completed, result.Failed, result.Message };
    }

    private static List<string> ParsePdNbrs(JsonElement arguments) =>
        arguments.TryGetProperty("pdNbrs", out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList()
            : [];
}
