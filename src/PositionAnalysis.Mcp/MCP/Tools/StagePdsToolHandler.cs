using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.ValueObjects;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class StagePdsToolHandler : IMcpToolHandler
{
    private readonly IStagingOrchestrator _stagingOrchestrator;

    public StagePdsToolHandler(IStagingOrchestrator stagingOrchestrator)
    {
        _stagingOrchestrator = stagingOrchestrator;
    }

    public string Name => "stage_pds";

    public string Description => "Stage position descriptions from Oracle into SCHEDULE_PC_EVAL for evaluation. Returns a requiresConfirmation payload first — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new { type = "string", description = "5-digit occupational series" },
            orgCode = new { type = "string" },
            confirmed = new { type = "boolean", description = "Set to true to approve staging." }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
            return new { requiresConfirmation = true, estimatedCostUsd = 0m };

        var series = arguments.GetStringOrNull("series");
        var orgCode = arguments.GetStringOrNull("orgCode");

        var filter = new StagingFilter(
            new Grade(13),
            new Grade(15),
            !string.IsNullOrWhiteSpace(series) ? new OccupationalSeries(series) : null,
            orgCode);

        var result = await _stagingOrchestrator.StageAsync(filter);

        return new
        {
            stagedCount = result.StagedCount,
            excludedWithoutDutiesCount = result.ExcludedWithoutDutiesCount,
            status = "staged"
        };
    }
}
