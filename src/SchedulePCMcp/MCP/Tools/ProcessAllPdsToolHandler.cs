using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.MCP;

namespace SchedulePCMcp.MCP.Tools;

public class ProcessAllPdsToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;
    private readonly ProcessAllRunStatusService _runStatusService;

    public ProcessAllPdsToolHandler(
        IScoringOrchestrator scoringOrchestrator,
        ProcessAllRunStatusService runStatusService)
    {
        _scoringOrchestrator = scoringOrchestrator;
        _runStatusService = runStatusService;
    }

    public string Name => "process_all_pds";

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
