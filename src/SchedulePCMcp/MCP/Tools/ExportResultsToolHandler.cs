using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

public class ExportResultsToolHandler : IMcpToolHandler
{
    private readonly IExportOrchestrator _exportOrchestrator;

    public ExportResultsToolHandler(IExportOrchestrator exportOrchestrator)
    {
        _exportOrchestrator = exportOrchestrator;
    }

    public string Name => "export_results";

    public string Description => "Export evaluation results to Excel for a full run or selected series";

    public object InputSchema => new
    {
        type = "object",
        required = new[] { "runId" },
        properties = new
        {
            runId = new { type = "string" },
            series = new
            {
                type = "array",
                items = new { type = "string" }
            }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var runId = arguments.GetStringOrNull("runId");
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("runId is required", nameof(arguments));

        var series = arguments.GetStringList("series");
        if (series.Count == 0)
        {
            await _exportOrchestrator.ExportByRunAsync(runId);
        }
        else
        {
            await _exportOrchestrator.ExportBySeriesAsync(runId, series);
        }

        var status = await _exportOrchestrator.GetExportStatusAsync(runId);

        return new
        {
            runId,
            total = status.Total,
            exported = status.Exported
        };
    }
}
