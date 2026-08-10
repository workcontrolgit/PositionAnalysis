using System.Text.Json;
using SchedulePCMcp.Infrastructure.Repositories;

namespace SchedulePCMcp.MCP.Tools;

public class RetryFailedPdsToolHandler : IMcpToolHandler
{
    private readonly ISchedulePCEvalRepository _evalRepository;

    public RetryFailedPdsToolHandler(ISchedulePCEvalRepository evalRepository)
    {
        _evalRepository = evalRepository;
    }

    public string Name => "retry_failed_pds";

    public string Description => "Reset all FAILED evaluation rows back to PENDING so they will be re-scored on the next process_pds_by_series call";

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
                ? $"Reset {resetCount} failed PD(s) to PENDING. Run process_pds_by_series to re-score."
                : "No failed PDs found to reset."
        };
    }
}
