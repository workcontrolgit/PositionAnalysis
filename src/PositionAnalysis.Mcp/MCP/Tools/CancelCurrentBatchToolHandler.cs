using System.Text.Json;
using PositionAnalysis.Mcp.Application.Services;

namespace PositionAnalysis.Mcp.MCP.Tools;

public sealed class CancelCurrentBatchToolHandler : IMcpToolHandler
{
    private readonly BatchCancellationService _cancel;

    public CancelCurrentBatchToolHandler(BatchCancellationService cancel)
        => _cancel = cancel;

    public string Name => "cancel_current_batch";

    public string Description =>
        "Immediately cancels the currently running batch scoring operation. " +
        "The batch stops after finishing its current in-flight records.";

    public object InputSchema => new { type = "object", properties = new { } };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        _cancel.CancelCurrent();
        return Task.FromResult<object>(new
        {
            status = "cancellation_requested",
            message = "Batch cancellation signal sent."
        });
    }
}
