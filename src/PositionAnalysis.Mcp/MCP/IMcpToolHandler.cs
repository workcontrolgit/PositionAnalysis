using System.Text.Json;

namespace PositionAnalysis.Mcp.MCP;

public interface IMcpToolHandler
{
    string Name { get; }
    string Description { get; }
    object InputSchema { get; }
    Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>
/// Extended handler for tools that push <c>notifications/progress</c> messages
/// during execution.  All existing IMcpToolHandler implementations are unaffected;
/// only tools that opt in implement this interface.
/// </summary>
public interface IMcpStreamingToolHandler : IMcpToolHandler
{
    /// <param name="arguments">Tool arguments JSON.</param>
    /// <param name="progressToken">
    ///   Token echoed in each <c>notifications/progress</c> message.
    ///   Null when the caller did not supply <c>_meta.progressToken</c>.
    /// </param>
    /// <param name="reportProgressAsync">
    ///   Delegate the tool calls to emit a progress notification.
    ///   Signature: (current, total).  Null when no token was supplied.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<object> InvokeStreamingAsync(
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken);
}
