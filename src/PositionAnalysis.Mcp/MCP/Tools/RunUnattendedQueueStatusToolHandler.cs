using System.Text.Json;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class RunUnattendedQueueStatusToolHandler : IMcpToolHandler
{
    private readonly IPositionAnalysisEvalRepository _evaluationRepository;
    private readonly ProcessAllRunStatusService _runStatusService;

    public RunUnattendedQueueStatusToolHandler(
        IPositionAnalysisEvalRepository evaluationRepository,
        ProcessAllRunStatusService runStatusService)
    {
        _evaluationRepository = evaluationRepository;
        _runStatusService = runStatusService;
    }

    public string Name => "get_unattended_queue_status";

    public string Description => "Get aggregate Schedule PC worker queue status";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var status = await _evaluationRepository.GetQueueStatusAsync();
        var runStatus = _runStatusService.GetStatus();

        return new
        {
            pending = status.Pending,
            inProgress = status.InProgress,
            complete = status.Complete,
            failed = status.Failed,
            isDrained = status.IsDrained,
            runActive = runStatus.IsActive,
            runCompleted = runStatus.IsCompleted,
            runFailed = runStatus.HasFailed,
            runError = runStatus.Error
        };
    }
}
