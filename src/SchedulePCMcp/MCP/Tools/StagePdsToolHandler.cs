using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Domain.ValueObjects;

namespace SchedulePCMcp.MCP.Tools;

public class StagePdsToolHandler : IMcpToolHandler
{
    private readonly IStagingOrchestrator _stagingOrchestrator;

    public StagePdsToolHandler(IStagingOrchestrator stagingOrchestrator)
    {
        _stagingOrchestrator = stagingOrchestrator;
    }

    public string Name => "stage_pds";

    public string Description => "Stage position descriptions from Oracle into a new evaluation run";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            gradeMin = new { type = "integer" },
            gradeMax = new { type = "integer" },
            series = new { type = "string", description = "4-digit occupational series" },
            orgCode = new { type = "string" }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var min = arguments.GetIntOrNull("gradeMin");
        var max = arguments.GetIntOrNull("gradeMax");
        var series = arguments.GetStringOrNull("series");
        var orgCode = arguments.GetStringOrNull("orgCode");

        var filter = new StagingFilter(
            min != null ? new Grade(min.Value) : null,
            max != null ? new Grade(max.Value) : null,
            !string.IsNullOrWhiteSpace(series) ? new OccupationalSeries(series) : null,
            orgCode);

        var runId = await _stagingOrchestrator.StageAsync(filter);

        return new
        {
            runId,
            status = "staged"
        };
    }
}
