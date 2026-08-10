using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

public class GetProcessingStatusToolHandler : IMcpToolHandler
{
    private readonly IProcessingStatusService _processingStatusService;

    public GetProcessingStatusToolHandler(IProcessingStatusService processingStatusService)
    {
        _processingStatusService = processingStatusService;
    }

    public string Name => "get_processing_status";

    public string Description => "Get processing progress grouped by occupational series";

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
