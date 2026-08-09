using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

public class GetStagingReportToolHandler : IMcpToolHandler
{
    private readonly IReportingService _reportingService;

    public GetStagingReportToolHandler(IReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    public string Name => "get_staging_report";

    public string Description => "Get staged counts grouped by occupational series for a run";

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

        var report = await _reportingService.GetStagingReportAsync(runId);

        // Keep response shape aligned with get_processing_status so clients can
        // render both reports using the same table columns.
        var series = report.Select(item => new
        {
            series = item.Key.Code,
            staged = item.Value.Staged,
            inProgress = item.Value.InProgress,
            complete = item.Value.Complete,
            failed = item.Value.Failed,
            percentComplete = item.Value.PercentComplete,
            total = item.Value.Total
        }).ToList();

        return new
        {
            runId,
            series
        };
    }
}
