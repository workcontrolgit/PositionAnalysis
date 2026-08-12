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

    public string Description => "Generate Word evaluation documents for all completed evaluations";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        await _documentGenerationOrchestrator.GenerateAllAsync();

        var generated = await _documentGenerationOrchestrator.GetGenerationProgressAsync();

        return new
        {
            generated
        };
    }
}
