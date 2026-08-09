using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

public class ProcessPdsBySeriesToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public ProcessPdsBySeriesToolHandler(IScoringOrchestrator scoringOrchestrator)
    {
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "process_pds_by_series";

    public string Description => "Start asynchronous scoring for staged PDs in one or more series";

    public object InputSchema => new
    {
        type = "object",
        required = new[] { "runId", "series" },
        properties = new
        {
            runId = new { type = "string" },
            series = new
            {
                type = "array",
                items = new { type = "string" }
            }
        }
    };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var runId = arguments.GetStringOrNull("runId");
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("runId is required", nameof(arguments));

        var series = arguments.GetStringList("series");
        if (series.Count == 0)
            throw new ArgumentException("series must include at least one item", nameof(arguments));

        _ = Task.Run(() => _scoringOrchestrator.ScoreBySeriesAsync(runId, series), CancellationToken.None);

        return Task.FromResult<object>(new
        {
            runId,
            seriesCount = series.Count,
            status = "processing_started"
        });
    }
}
