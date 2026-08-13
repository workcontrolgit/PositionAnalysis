using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

/// <summary>
/// Gets processing status for evaluations whose PD matches a bureau or org code.
/// </summary>
public class GetProcessingStatusByOrgsToolHandler : IMcpToolHandler
{
    private readonly IProcessingStatusService _processingStatusService;

    public GetProcessingStatusByOrgsToolHandler(IProcessingStatusService processingStatusService)
    {
        _processingStatusService = processingStatusService;
    }

    public string Name => "get_processing_status_by_orgs";

    public string Description => "Get processing progress filtered to ONLY evaluations matching the given bureau/org codes (not all series). Use this whenever the user names one or more specific org/bureau codes.";

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

        var status = await _processingStatusService.GetStatusByOrgCodesAsync(orgCodes);

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
            series
        };
    }
}
