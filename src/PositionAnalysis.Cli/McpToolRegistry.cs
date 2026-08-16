using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PositionAnalysis.Cli;

/// <summary>
/// Aggregates tools from multiple McpClient instances and dispatches LLM tool calls
/// to the correct client. Each McpClientTool retains an internal reference to its
/// parent McpClient, so dispatch is automatic via McpClientTool.CallAsync.
/// </summary>
public sealed class McpToolRegistry
{
    private IReadOnlyList<McpClientTool> _allTools = [];
    private Dictionary<string, McpClientTool> _toolsByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All tools as AITool instances, ready to pass to ChatOptions.Tools.</summary>
    public IReadOnlyList<AITool> Tools => _allTools.Cast<AITool>().ToList();

    /// <summary>
    /// Calls ListToolsAsync on each client and builds the combined tool registry.
    /// Must be called before accessing <see cref="Tools"/> or <see cref="CallToolAsync"/>.
    /// </summary>
    public async Task InitializeAsync(IEnumerable<McpClient> clients)
    {
        var all = new List<McpClientTool>();
        foreach (var client in clients)
        {
            var tools = await client.ListToolsAsync();
            all.AddRange(tools);
        }

        _allTools = all;
        _toolsByName = all.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Dispatches a tool call to the owning McpClient and returns the text content
    /// of the result as a JSON string.
    /// </summary>
    public async Task<string> CallToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken = default)
    {
        if (!_toolsByName.TryGetValue(name, out var tool))
            throw new InvalidOperationException($"Unknown tool: '{name}'. Available: {string.Join(", ", _toolsByName.Keys)}");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>(args),
            cancellationToken: cancellationToken);

        return string.Join("",
            result.Content
                  .OfType<TextContentBlock>()
                  .Select(t => t.Text ?? ""));
    }
}
