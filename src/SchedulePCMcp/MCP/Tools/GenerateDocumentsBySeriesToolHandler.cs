using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

/// <summary>
/// Generates Word evaluation documents for completed evaluations in specific occupational series.
/// </summary>
public class GenerateDocumentsBySeriesToolHandler : IMcpToolHandler
{
    private readonly IDocumentGenerationOrchestrator _documentGenerationOrchestrator;

    public GenerateDocumentsBySeriesToolHandler(IDocumentGenerationOrchestrator documentGenerationOrchestrator)
    {
        _documentGenerationOrchestrator = documentGenerationOrchestrator;
    }

    public string Name => "generate_documents_by_series";

    public string Description => "Generate Word evaluation documents for completed evaluations in the specified occupational series";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of 5-digit occupational series codes (e.g. ['00110', '00301'])"
            }
        },
        required = new[] { "series" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var series = arguments.GetStringList("series");
        if (series.Count == 0)
            return new { error = "Missing required parameter: series (array of series codes)" };

        await _documentGenerationOrchestrator.GenerateBySeriesAsync(series);

        var generated = await _documentGenerationOrchestrator.GetGenerationProgressAsync();

        return new
        {
            generated
        };
    }
}
