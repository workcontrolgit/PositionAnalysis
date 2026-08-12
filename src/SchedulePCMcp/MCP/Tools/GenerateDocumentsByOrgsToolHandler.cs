using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;

namespace SchedulePCMcp.MCP.Tools;

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

    public string Description => "Generate Word evaluation documents for completed evaluations matching the specified bureau or org codes";

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

        await _documentGenerationOrchestrator.GenerateByOrgCodesAsync(orgCodes);

        var generated = await _documentGenerationOrchestrator.GetGenerationProgressAsync();

        return new
        {
            generated
        };
    }
}
