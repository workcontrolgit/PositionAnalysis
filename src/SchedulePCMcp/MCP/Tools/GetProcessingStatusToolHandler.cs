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

    public string Description => "Get processing progress by series for a run";

    public object InputSchema => new
    {
        type = "object",
        required = new[] { "runId" },
        properties = new
        {
            runId = new { type = "string" }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var runId = arguments.GetStringOrNull("runId");
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("runId is required", nameof(arguments));

        var status = await _processingStatusService.GetStatusAsync(runId);

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
            runId,
            series
        };
    }
}
