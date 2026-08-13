using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Forces a fresh LLM rescore of every staged PD, regardless of current status.
/// </summary>
public class RescoreAllPdsToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RescoreAllPdsToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rescore_all_pds";

    public string Description => "Force a fresh LLM rescore of every staged PD across all series, overwriting existing results regardless of status";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        await _scoringOrchestrator.RescoreAllAsync();

        return new
        {
            status = "Rescore complete for all staged PDs. Run generate_documents to regenerate Word forms."
        };
    }
}
