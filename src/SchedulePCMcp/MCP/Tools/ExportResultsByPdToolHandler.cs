using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

/// <summary>
/// Exports evaluation results to Excel for one or more specific PD numbers.
/// </summary>
public class ExportResultsByPdToolHandler : IMcpToolHandler
{
    private readonly IExportOrchestrator _exportOrchestrator;

    public ExportResultsByPdToolHandler(IExportOrchestrator exportOrchestrator)
    {
        _exportOrchestrator = exportOrchestrator;
    }

    public string Name => "export_results_by_pd";

    public string Description => "Export evaluation results to Excel filtered to ONLY the given PD numbers (not all series). Use this whenever the user names one or more specific PD numbers.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            pdNumbers = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of PD numbers to export results for (e.g. ['200028', 'D00240'])"
            }
        },
        required = new[] { "pdNumbers" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pdNumbers = arguments.GetStringList("pdNumbers");
        if (pdNumbers.Count == 0)
            return new { error = "Missing required parameter: pdNumbers (array of PD numbers)" };

        await _exportOrchestrator.ExportByPdNumbersAsync(pdNumbers);

        var status = await _exportOrchestrator.GetExportStatusAsync();

        return new
        {
            total = status.Total,
            exported = status.Exported
        };
    }
}
