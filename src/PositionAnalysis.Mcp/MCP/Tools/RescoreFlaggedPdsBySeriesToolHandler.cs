using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Forces a fresh LLM rescore of PDs flagged needs_rescore = 'Y' within the specified series.
/// </summary>
public class RescoreFlaggedPdsBySeriesToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RescoreFlaggedPdsBySeriesToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rescore_flagged_pds_by_series";

    public string Description => "Force a fresh LLM rescore of PDs flagged needs_rescore = 'Y' within the specified occupational series";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of 5-digit occupational series codes to rescore (e.g. ['00110', '00301'])"
            }
        },
        required = new[] { "series" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var series = arguments.GetStringList("series");
        if (series.Count == 0)
            return new { error = "Missing required parameter: series (array of series codes)" };

        await _scoringOrchestrator.RescoreFlaggedBySeriesAsync(series);

        return new
        {
            series,
            status = $"Rescore complete for needs_rescore-flagged PDs in series: {string.Join(", ", series)}. Run generate_documents to regenerate Word forms."
        };
    }
}
