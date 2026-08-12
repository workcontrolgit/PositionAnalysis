using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

/// <summary>
/// Generates Word evaluation documents for one or more specific PD numbers.
/// </summary>
public class GenerateDocumentsByPdToolHandler : IMcpToolHandler
{
    private readonly IDocumentGenerationOrchestrator _documentGenerationOrchestrator;

    public GenerateDocumentsByPdToolHandler(IDocumentGenerationOrchestrator documentGenerationOrchestrator)
    {
        _documentGenerationOrchestrator = documentGenerationOrchestrator;
    }

    public string Name => "generate_documents_by_pd";

    public string Description => "Generate Word evaluation documents filtered to ONLY the given PD numbers (not all series). Use this whenever the user names one or more specific PD numbers.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            pdNumbers = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of PD numbers to generate documents for (e.g. ['200028', 'D00240'])"
            }
        },
        required = new[] { "pdNumbers" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pdNumbers = arguments.GetStringList("pdNumbers");
        if (pdNumbers.Count == 0)
            return new { error = "Missing required parameter: pdNumbers (array of PD numbers)" };

        var (succeeded, failed) = await _documentGenerationOrchestrator.GenerateByPdNumbersAsync(pdNumbers);

        return new
        {
            generated = succeeded,
            failed
        };
    }
}
