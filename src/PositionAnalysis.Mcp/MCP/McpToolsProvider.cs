using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace PositionAnalysis.Mcp.MCP;

public sealed record ProcessAllRunStatus(bool IsActive, bool IsCompleted, bool HasFailed, string? Error);

public sealed class ProcessAllRunStatusService
{
    private readonly object _syncRoot = new();
    private ProcessAllRunStatus _status = new(false, false, false, null);

    public ProcessAllRunStatus GetStatus()
    {
        lock (_syncRoot)
            return _status;
    }

    public Task StartAsync(Func<Task> operation)
    {
        lock (_syncRoot)
            _status = new ProcessAllRunStatus(true, false, false, null);

        return ObserveAsync(operation);
    }

    private async Task ObserveAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            lock (_syncRoot)
                _status = new ProcessAllRunStatus(false, true, false, null);
        }
        catch (Exception exception)
        {
            lock (_syncRoot)
                _status = new ProcessAllRunStatus(false, false, true, exception.Message);
        }
    }
}

public class McpToolsProvider
{
    private readonly IServiceScopeFactory _scopeFactory;

    public McpToolsProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public IReadOnlyList<object> ListTools()
    {
        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IMcpToolHandler>();

        return handlers
            .Select(handler => new
            {
                name = handler.Name,
                description = handler.Description,
                inputSchema = handler.InputSchema
            })
            .Cast<object>()
            .ToList();
    }

    /// <param name="name">Tool name.</param>
    /// <param name="arguments">Tool arguments JSON.</param>
    /// <param name="progressToken">
    ///   Value from the client's <c>_meta.progressToken</c>; null when absent.
    /// </param>
    /// <param name="reportProgressAsync">
    ///   Delegate that writes a <c>notifications/progress</c> message to stdout.
    ///   Null when <paramref name="progressToken"/> is null.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<object> CallToolAsync(
        string name,
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IMcpToolHandler>();
        var handler = handlers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

        if (handler == null)
            throw new InvalidOperationException($"Unknown MCP tool: {name}");

        // Tools that opt into streaming get the progress delegate; all others use the plain path.
        if (handler is IMcpStreamingToolHandler streamingHandler)
            return await streamingHandler.InvokeStreamingAsync(arguments, progressToken, reportProgressAsync, cancellationToken);

        return await handler.InvokeAsync(arguments, cancellationToken);
    }
}
