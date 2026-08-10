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

    public string Description => "Get total staged PD count by occupational series (pre-scoring snapshot)";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var report = await _reportingService.GetStagingReportAsync();

        var series = report.Select(item => new
        {
            series = item.Key.Code,
            total = item.Value.Total
        }).ToList();

        return new
        {
            totalSeries = series.Count,
            totalPds = series.Sum(s => s.total),
            series
        };
    }
}
