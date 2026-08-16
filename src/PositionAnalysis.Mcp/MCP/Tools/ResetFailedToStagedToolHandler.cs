using System.Text.Json;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class ResetFailedToStagedToolHandler : IMcpToolHandler
{
    private readonly IPositionAnalysisEvalRepository _evalRepository;

    public ResetFailedToStagedToolHandler(IPositionAnalysisEvalRepository evalRepository)
    {
        _evalRepository = evalRepository;
    }

    public string Name => "reset_failed_to_staged";

    public string Description => "Reset all FAILED evaluation rows back to PENDING so they will be re-scored on the next process_batch_by_series or run_unattended_scoring call. Returns a requiresConfirmation payload first — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new { type = "boolean", description = "Set to true to approve the reset." }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            var failedCount = await _evalRepository.GetCountByStatusAsync(EvaluationStatus.Failed);
            return new { requiresConfirmation = true, pendingCount = failedCount, estimatedCostUsd = 0m };
        }

        var resetCount = await _evalRepository.ResetFailedAsync();
        return new
        {
            resetCount,
            status = resetCount > 0
                ? $"Reset {resetCount} failed PD(s) to PENDING. Run process_batch_by_series or run_unattended_scoring to re-score."
                : "No failed PDs found to reset."
        };
    }
}
