using System.Text.Json;
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

    public string Description => "Reset all FAILED evaluation rows back to PENDING so they will be re-scored on the next process_batch_by_series or run_unattended_scoring call";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
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
