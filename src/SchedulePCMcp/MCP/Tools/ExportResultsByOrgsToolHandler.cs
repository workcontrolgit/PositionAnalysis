using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

/// <summary>
/// Exports evaluation results to Excel for PDs matching a bureau or org code.
/// </summary>
public class ExportResultsByOrgsToolHandler : IMcpToolHandler
{
    private readonly IExportOrchestrator _exportOrchestrator;

    public ExportResultsByOrgsToolHandler(IExportOrchestrator exportOrchestrator)
    {
        _exportOrchestrator = exportOrchestrator;
    }

    public string Name => "export_results_by_orgs";

    public string Description => "Export evaluation results to Excel filtered to ONLY PDs matching the given bureau/org codes (not all series). Use this whenever the user names one or more specific org/bureau codes.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            orgCodes = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of bureau or org codes to match (e.g. ['15', '1500'])"
            }
        },
        required = new[] { "orgCodes" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var orgCodes = arguments.GetStringList("orgCodes");
        if (orgCodes.Count == 0)
            return new { error = "Missing required parameter: orgCodes (array of bureau/org codes)" };

        await _exportOrchestrator.ExportByOrgCodesAsync(orgCodes);

        var status = await _exportOrchestrator.GetExportStatusAsync();

        return new
        {
            total = status.Total,
            exported = status.Exported
        };
    }
}
