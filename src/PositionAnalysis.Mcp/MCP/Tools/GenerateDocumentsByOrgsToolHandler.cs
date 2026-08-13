using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Generates Word evaluation documents for completed evaluations whose PD matches a bureau or org code.
/// </summary>
public class GenerateDocumentsByOrgsToolHandler : IMcpToolHandler
{
    private readonly IDocumentGenerationOrchestrator _documentGenerationOrchestrator;

    public GenerateDocumentsByOrgsToolHandler(IDocumentGenerationOrchestrator documentGenerationOrchestrator)
    {
        _documentGenerationOrchestrator = documentGenerationOrchestrator;
    }

    public string Name => "generate_documents_by_orgs";

    public string Description => "Generate Word evaluation documents filtered to ONLY completed evaluations matching the given bureau/org codes (not all series). Use this whenever the user names one or more specific org/bureau codes.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            orgCodes = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of bureau or org codes to match (e.g. ['15', '1500'])"
            }
        },
        required = new[] { "orgCodes" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var orgCodes = arguments.GetStringList("orgCodes");
        if (orgCodes.Count == 0)
            return new { error = "Missing required parameter: orgCodes (array of bureau/org codes)" };

        var (succeeded, failed) = await _documentGenerationOrchestrator.GenerateByOrgCodesAsync(orgCodes);

        return new
        {
            generated = succeeded,
            failed
        };
    }
}
