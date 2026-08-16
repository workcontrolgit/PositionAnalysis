using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class GenerateDocumentsToolHandler : IMcpToolHandler
{
    private readonly IDocumentGenerationOrchestrator _documentGenerationOrchestrator;

    public GenerateDocumentsToolHandler(IDocumentGenerationOrchestrator documentGenerationOrchestrator)
    {
        _documentGenerationOrchestrator = documentGenerationOrchestrator;
    }

    public string Name => "generate_documents_all";

    public string Description => "Generate Word evaluation documents for ALL completed evaluations with no filtering. Do NOT use this if the user names specific series, org codes, or PD numbers — use generate_documents_by_series, generate_documents_by_orgs, or generate_documents_by_pd instead.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new { type = "boolean", description = "Set to true to proceed after confirmation." }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) && c.GetBoolean();
        if (!confirmed)
        {
            var count = await _documentGenerationOrchestrator.CountAllAsync();
            return new { requiresConfirmation = true, pendingCount = count, estimatedCostUsd = 0m };
        }

        var (succeeded, failed) = await _documentGenerationOrchestrator.GenerateAllAsync();
        return new { generated = succeeded, failed };
    }
}
