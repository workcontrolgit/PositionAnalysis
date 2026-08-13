using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Gets processing status for completed/staged/in-progress/failed evaluations in specific occupational series.
/// </summary>
public class GetProcessingStatusBySeriesToolHandler : IMcpToolHandler
{
    private readonly IProcessingStatusService _processingStatusService;

    public GetProcessingStatusBySeriesToolHandler(IProcessingStatusService processingStatusService)
    {
        _processingStatusService = processingStatusService;
    }

    public string Name => "get_processing_status_by_series";

    public string Description => "Get processing progress filtered to ONLY the given occupational series codes (not all series). Use this whenever the user names one or more specific series.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of 5-digit occupational series codes (e.g. ['00110', '00301'])"
            }
        },
        required = new[] { "series" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var seriesCodes = arguments.GetStringList("series");
        if (seriesCodes.Count == 0)
            return new { error = "Missing required parameter: series (array of series codes)" };

        var status = await _processingStatusService.GetStatusBySeriesAsync(seriesCodes);

        var series = status.Select(item => new
        {
            series = item.Key.Code,
            staged = item.Value.Staged,
            inProgress = item.Value.InProgress,
            complete = item.Value.Complete,
            failed = item.Value.Failed,
            percentComplete = item.Value.PercentComplete
        }).ToList();

        return new
        {
            series
        };
    }
}
