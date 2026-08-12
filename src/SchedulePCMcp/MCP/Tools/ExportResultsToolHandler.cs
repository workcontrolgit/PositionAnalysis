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

    public string Description => "Export all evaluation results to Excel";

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
