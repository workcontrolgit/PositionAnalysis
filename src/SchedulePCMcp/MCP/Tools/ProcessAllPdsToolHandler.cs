using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

public class ProcessAllPdsToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public ProcessAllPdsToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
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
        _ = Task.Run(() => _scoringOrchestrator.ScoreAllAsync(), CancellationToken.None);

        return Task.FromResult<object>(new
        {
            status = "processing_started",
            message = "Scoring all staged PDs across all series. Poll get_processing_status to track progress."
        });
    }
}
