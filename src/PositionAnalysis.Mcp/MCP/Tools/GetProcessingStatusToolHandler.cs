using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class GetProcessingStatusToolHandler : IMcpToolHandler
{
    private readonly IProcessingStatusService _processingStatusService;

    public GetProcessingStatusToolHandler(IProcessingStatusService processingStatusService)
    {
        _processingStatusService = processingStatusService;
    }

    public string Name => "get_processing_status";

    public string Description => "Get processing progress for ALL occupational series with no filtering. Do NOT use this if the user names specific series, org codes, or PD numbers — use get_processing_status_by_series, get_processing_status_by_orgs, or get_processing_status_by_pd instead.";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var status = await _processingStatusService.GetStatusAsync();

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
