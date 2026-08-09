using System.Text.Json;

namespace SchedulePCMcp.MCP;

public interface IMcpToolHandler
{
    string Name { get; }
    string Description { get; }
    object InputSchema { get; }
    Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken);
}
