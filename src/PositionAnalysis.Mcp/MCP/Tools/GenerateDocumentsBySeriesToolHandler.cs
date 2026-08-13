using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

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

    public string Description => "Generate Word evaluation documents filtered to ONLY completed evaluations in the given occupational series (not all series). Use this whenever the user names one or more specific series.";

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

        var (succeeded, failed) = await _documentGenerationOrchestrator.GenerateBySeriesAsync(series);

        return new
        {
            generated = succeeded,
            failed
        };
    }
}
