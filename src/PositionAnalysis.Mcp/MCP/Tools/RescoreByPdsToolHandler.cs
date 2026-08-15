using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Forces a fresh LLM rescore of one or more PDs, overwriting any existing result regardless of current status.
/// </summary>
public class RescoreByPdsToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RescoreByPdsToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rescore_by_pds";

    public string Description => "Force a fresh LLM rescore of one or more PDs by their PD numbers, overwriting any existing result regardless of current status";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            pds = new
            {
                type = "array",
                items = new { type = "string" },
                description = "One or more position description numbers to rescore (e.g. ['200028', '200029'])"
            }
        },
        required = new[] { "pds" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("pds", out var pdsEl) ||
            pdsEl.ValueKind != JsonValueKind.Array)
        {
            return new { error = "Missing required parameter: pds (array of PD numbers)" };
        }

        var pds = pdsEl.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (pds.Count == 0)
            return new { error = "pds array cannot be empty" };

        foreach (var pdNbr in pds)
            await _scoringOrchestrator.ScoreAsync(pdNbr);

        return new
        {
            pds,
            status = $"Rescore complete for PD(s): {string.Join(", ", pds)}. Run generate_documents_by_pd to regenerate Word forms."
        };
    }
}
