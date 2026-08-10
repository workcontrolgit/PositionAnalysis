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

    public string Description => "Export evaluation results to Excel for all or selected series";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" }
            }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var series = arguments.GetStringList("series");
        if (series.Count == 0)
        {
            await _exportOrchestrator.ExportAllAsync();
        }
        else
        {
            await _exportOrchestrator.ExportBySeriesAsync(series);
        }

        var status = await _exportOrchestrator.GetExportStatusAsync();

        return new
        {
            total = status.Total,
            exported = status.Exported
        };
    }
}
