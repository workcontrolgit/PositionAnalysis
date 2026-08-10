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
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" }
            }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var series = arguments.GetStringList("series");
        if (series.Count == 0)
        {
            await _documentGenerationOrchestrator.GenerateAllAsync();
        }
        else
        {
            await _documentGenerationOrchestrator.GenerateBySeriesAsync(series);
        }

        var generated = await _documentGenerationOrchestrator.GetGenerationProgressAsync();

        return new
        {
            generated
        };
    }
}
