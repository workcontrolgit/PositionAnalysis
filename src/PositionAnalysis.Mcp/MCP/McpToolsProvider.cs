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

    public async Task<object> CallToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IMcpToolHandler>();
        var handler = handlers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

        if (handler == null)
        {
            throw new InvalidOperationException($"Unknown MCP tool: {name}");
        }

        return await handler.InvokeAsync(arguments, cancellationToken);
    }
}
