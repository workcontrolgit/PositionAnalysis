using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Exports evaluation results to Excel for specific occupational series.
/// </summary>
public class ExportResultsBySeriesToolHandler : IMcpToolHandler
{
    private readonly IExportOrchestrator _exportOrchestrator;

    public ExportResultsBySeriesToolHandler(IExportOrchestrator exportOrchestrator)
    {
        _exportOrchestrator = exportOrchestrator;
    }

    public string Name => "export_results_by_series";

    public string Description => "Export evaluation results to Excel filtered to ONLY the given occupational series (not all series). Use this whenever the user names one or more specific series.";

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
        var series = arguments.GetStringList("series");
        if (series.Count == 0)
            return new { error = "Missing required parameter: series (array of series codes)" };

        await _exportOrchestrator.ExportBySeriesAsync(series);

        var status = await _exportOrchestrator.GetExportStatusAsync();

        return new
        {
            total = status.Total,
            exported = status.Exported
        };
    }
}
