using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class StagePdsToolHandler : IMcpToolHandler
{
    private readonly IStagingOrchestrator _stagingOrchestrator;
    private readonly IPositionAnalysisEvalRepository _evalRepository;

    public StagePdsToolHandler(IStagingOrchestrator stagingOrchestrator, IPositionAnalysisEvalRepository evalRepository)
    {
        _stagingOrchestrator = stagingOrchestrator;
        _evalRepository = evalRepository;
    }

    public string Name => "stage_pds";

    public string Description => "Add new position descriptions from the Oracle source into SCHEDULE_PC_EVAL for evaluation (add-only, does not delete existing records). Returns a requiresConfirmation payload first — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new { type = "boolean", description = "Set to true to approve staging." }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        var filter = new StagingFilter(new Grade(13), new Grade(15), null, null);

        if (!confirmed)
        {
            var newCount = await _evalRepository.CountNewToStageAsync(filter);
            return new { requiresConfirmation = true, pendingCount = newCount, estimatedCostUsd = 0m };
        }

        var result = await _stagingOrchestrator.StageAsync(filter);

        return new
        {
            stagedCount = result.StagedCount,
            excludedWithoutDutiesCount = result.ExcludedWithoutDutiesCount,
            status = "staged"
        };
    }
}
