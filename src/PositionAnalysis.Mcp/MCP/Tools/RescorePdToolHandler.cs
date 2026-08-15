using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Forces a fresh LLM rescore of a single PD regardless of its current status.
/// Useful for correcting stale or wrong-criteria results without resetting all rows.
/// </summary>
public class RescorePdToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RescorePdToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rescore_pd";

    public string Description => "Force a fresh LLM rescore of a single PD by its PD number, overwriting any existing result regardless of current status";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            pd_nbr = new
            {
                type = "string",
                description = "The position description number to rescore (e.g. '200028')"
            }
        },
        required = new[] { "pd_nbr" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("pd_nbr", out var pdNbrEl) ||
            pdNbrEl.ValueKind != JsonValueKind.String)
        {
            return new { error = "Missing required parameter: pd_nbr" };
        }

        var pdNbr = pdNbrEl.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pdNbr))
            return new { error = "pd_nbr cannot be empty" };

        await _scoringOrchestrator.ScoreAsync(pdNbr);

        return new
        {
            pdNbr,
            status = $"Rescore complete for PD {pdNbr}. Run generate_documents_all to regenerate the Word form."
        };
    }
}
