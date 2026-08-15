using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.MCP;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class RunUnattendedScoringToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;
    private readonly ProcessAllRunStatusService _runStatusService;

    public RunUnattendedScoringToolHandler(
        IScoringOrchestrator scoringOrchestrator,
        ProcessAllRunStatusService runStatusService)
    {
        _scoringOrchestrator = scoringOrchestrator;
        _runStatusService = runStatusService;
    }

    public string Name => "run_unattended_scoring";

    public string Description => "Start asynchronous scoring for all staged PENDING PDs across every occupational series";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        _ = _runStatusService.StartAsync(_scoringOrchestrator.ScoreAllAsync);

        return Task.FromResult<object>(new
        {
            status = "processing_started",
            message = "Scoring all staged PDs across all series. Poll get_queue_status to track progress."
        });
    }
}
