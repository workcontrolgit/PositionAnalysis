using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class ExportResultsToolHandler : IMcpToolHandler
{
    private readonly IExportOrchestrator _exportOrchestrator;

    public ExportResultsToolHandler(IExportOrchestrator exportOrchestrator)
    {
        _exportOrchestrator = exportOrchestrator;
    }

    public string Name => "export_results";

    public string Description => "Export ALL evaluation results to Excel with no filtering. Do NOT use this if the user names specific series, org codes, or PD numbers — use export_results_by_series, export_results_by_orgs, or export_results_by_pd instead.";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        await _exportOrchestrator.ExportAllAsync();

        var status = await _exportOrchestrator.GetExportStatusAsync();

        return new
        {
            total = status.Total,
            exported = status.Exported
        };
    }
}
