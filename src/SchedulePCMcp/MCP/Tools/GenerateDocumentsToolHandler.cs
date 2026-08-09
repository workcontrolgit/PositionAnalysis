using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

public class GenerateDocumentsToolHandler : IMcpToolHandler
{
    private readonly IDocumentGenerationOrchestrator _documentGenerationOrchestrator;

    public GenerateDocumentsToolHandler(IDocumentGenerationOrchestrator documentGenerationOrchestrator)
    {
        _documentGenerationOrchestrator = documentGenerationOrchestrator;
    }

    public string Name => "generate_documents";

    public string Description => "Generate Word evaluation documents for all or selected series";

    public object InputSchema => new
    {
        type = "object",
        required = new[] { "runId" },
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

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var runId = arguments.GetStringOrNull("runId");
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("runId is required", nameof(arguments));

        var series = arguments.GetStringList("series");
        if (series.Count == 0)
        {
            await _documentGenerationOrchestrator.GenerateByRunAsync(runId);
        }
        else
        {
            await _documentGenerationOrchestrator.GenerateBySeriesAsync(runId, series);
        }

        var generated = await _documentGenerationOrchestrator.GetGenerationProgressAsync(runId);

        return new
        {
            runId,
            generated
        };
    }
}
