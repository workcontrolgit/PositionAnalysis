using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

/// <summary>
/// Forces a fresh LLM rescore of all PDs in specified series, regardless of current status.
/// </summary>
public class RescorePdsBySeriesToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RescorePdsBySeriesToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rescore_pds_by_series";

    public string Description => "Force a fresh LLM rescore of all PDs in the specified occupational series, overwriting existing results regardless of status";

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
        if (!arguments.TryGetProperty("series", out var seriesEl) ||
            seriesEl.ValueKind != JsonValueKind.Array)
        {
            return new { error = "Missing required parameter: series (array of series codes)" };
        }

        var series = seriesEl.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (series.Count == 0)
            return new { error = "series array cannot be empty" };

        await _scoringOrchestrator.RescoreBySeriesAsync(series);

        return new
        {
            series,
            status = $"Rescore complete for series: {string.Join(", ", series)}. Run generate_documents to regenerate Word forms."
        };
    }
}
