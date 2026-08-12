using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

/// <summary>
/// Gets processing status for one or more specific PD numbers.
/// </summary>
public class GetProcessingStatusByPdToolHandler : IMcpToolHandler
{
    private readonly IProcessingStatusService _processingStatusService;

    public GetProcessingStatusByPdToolHandler(IProcessingStatusService processingStatusService)
    {
        _processingStatusService = processingStatusService;
    }

    public string Name => "get_processing_status_by_pd";

    public string Description => "Get processing status filtered to ONLY the given PD numbers (not all series). Use this whenever the user names one or more specific PD numbers.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            pdNumbers = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of PD numbers to check status for (e.g. ['200028', 'D00240'])"
            }
        },
        required = new[] { "pdNumbers" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pdNumbers = arguments.GetStringList("pdNumbers");
        if (pdNumbers.Count == 0)
            return new { error = "Missing required parameter: pdNumbers (array of PD numbers)" };

        var statuses = await _processingStatusService.GetStatusByPdNumbersAsync(pdNumbers);

        var results = statuses.Select(s => new
        {
            pdNbr = s.PdNbr,
            series = s.Series,
            status = s.Status,
            rating = s.Rating
        }).ToList();

        return new
        {
            results
        };
    }
}
