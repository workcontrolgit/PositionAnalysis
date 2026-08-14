using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Re-derives HIGH/MEDIUM/LOW ratings for already-scored PDs from their stored triggered-criteria
/// count using the current rating thresholds, without calling the LLM. Use this after changing
/// only the rating scale/cutoffs — no rescore (and no LLM cost) is required.
/// </summary>
public class RebucketRatingsToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RebucketRatingsToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rebucket_ratings";

    public string Description => "Re-derive HIGH/MEDIUM/LOW ratings for already-scored PDs from their stored triggered-criteria count, using the current rating thresholds. No LLM call is made. Optionally filtered by series or PD numbers";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Optional list of 5-digit occupational series codes to filter by"
            },
            pdNumbers = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Optional list of PD numbers to filter by"
            }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pdNumbers = arguments.GetStringList("pdNumbers");
        var series = arguments.GetStringList("series");

        var changed = await _scoringOrchestrator.RebucketRatingsAsync(series, pdNumbers);

        return new
        {
            status = $"Rebucket complete: {changed} PD(s) had their rating changed. No LLM tokens were used.",
            changedCount = changed
        };
    }
}
