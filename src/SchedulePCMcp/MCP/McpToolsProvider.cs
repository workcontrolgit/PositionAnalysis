using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace SchedulePCMcp.MCP;

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
