using System.Text.Json;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class StagePdsClearToolHandler : IMcpToolHandler
{
    private readonly IPositionAnalysisEvalRepository _evalRepository;

    public StagePdsClearToolHandler(IPositionAnalysisEvalRepository evalRepository)
    {
        _evalRepository = evalRepository;
    }

    public string Name => "stage_pds_clear";

    public string Description => "Delete all records from SCHEDULE_PC_EVAL. Returns a requiresConfirmation payload first — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new { type = "boolean", description = "Set to true to approve deletion." }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            var staged   = await _evalRepository.GetCountByStatusAsync(EvaluationStatus.Staged);
            var inProg   = await _evalRepository.GetCountByStatusAsync(EvaluationStatus.InProgress);
            var complete = await _evalRepository.GetCountByStatusAsync(EvaluationStatus.Complete);
            var failed   = await _evalRepository.GetCountByStatusAsync(EvaluationStatus.Failed);
            return new { requiresConfirmation = true, pendingCount = staged + inProg + complete + failed, estimatedCostUsd = 0m };
        }

        var deletedCount = await _evalRepository.DeleteAllAsync();
        return new { status = "cleared", deletedCount };
    }
}
