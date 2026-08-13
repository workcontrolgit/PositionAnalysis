using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Forces a fresh LLM rescore of every PD flagged needs_rescore = 'Y'.
/// </summary>
public class RescoreFlaggedPdsToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RescoreFlaggedPdsToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rescore_flagged_pds";

    public string Description => "Force a fresh LLM rescore of every PD flagged needs_rescore = 'Y' (e.g. records scored before per-duty-keyed evidence was captured)";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        await _scoringOrchestrator.RescoreFlaggedAsync();

        return new
        {
            status = "Rescore complete for all needs_rescore-flagged PDs. Run generate_documents to regenerate Word forms."
        };
    }
}
