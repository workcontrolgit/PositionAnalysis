using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Forces a fresh LLM rescore of the specified PDs, but only those flagged needs_rescore = 'Y'.
/// </summary>
public class RescoreFlaggedPdsByPdToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RescoreFlaggedPdsByPdToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rescore_flagged_pds_by_pd";

    public string Description => "Force a fresh LLM rescore of the given PD numbers, but only those flagged needs_rescore = 'Y'";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            pdNumbers = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of PD numbers to rescore if flagged needs_rescore = 'Y' (e.g. ['200028', 'D00240'])"
            }
        },
        required = new[] { "pdNumbers" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pdNumbers = arguments.GetStringList("pdNumbers");
        if (pdNumbers.Count == 0)
            return new { error = "Missing required parameter: pdNumbers (array of PD numbers)" };

        await _scoringOrchestrator.RescoreFlaggedByPdAsync(pdNumbers);

        return new
        {
            pdNumbers,
            status = $"Rescore complete for needs_rescore-flagged PD(s): {string.Join(", ", pdNumbers)}. Run generate_documents_all to regenerate Word forms."
        };
    }
}
