using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Generates Word evaluation documents for only the human-confirmed Schedule P/C baseline PDs
/// (TEMP_PD_SCHED_PC.SCHEDULE_PC_IND = 'Y').
/// </summary>
public class GenerateDocumentsConfirmedSchedulePcToolHandler : IMcpToolHandler
{
    private readonly IDocumentGenerationOrchestrator _documentGenerationOrchestrator;
    private readonly IPositionDescriptionRepository _pdRepository;

    public GenerateDocumentsConfirmedSchedulePcToolHandler(
        IDocumentGenerationOrchestrator documentGenerationOrchestrator,
        IPositionDescriptionRepository pdRepository)
    {
        _documentGenerationOrchestrator = documentGenerationOrchestrator;
        _pdRepository = pdRepository;
    }

    public string Name => "generate_documents_confirmed_schedule_pc";

    public string Description =>
        "Generate Word evaluation documents for only the PDs with a human-confirmed Schedule P/C baseline determination " +
        "(SCHEDULE_PC_IND = 'Y').";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution after the confirmation prompt."
            }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pdNumbers = await _pdRepository.GetHumanSchedulePcPdNumbersAsync();
        if (pdNumbers.Count == 0)
            return new { error = "No PDs found with SCHEDULE_PC_IND = 'Y'" };

        var confirmed = arguments.TryGetProperty("confirmed", out var c) && c.GetBoolean();
        if (!confirmed)
        {
            var count = await _documentGenerationOrchestrator.CountByPdNumbersAsync(pdNumbers);
            return new { requiresConfirmation = true, pendingCount = count, estimatedCostUsd = 0m };
        }

        var (succeeded, failed) = await _documentGenerationOrchestrator.GenerateByPdNumbersAsync(pdNumbers);
        return new { generated = succeeded, failed };
    }
}
