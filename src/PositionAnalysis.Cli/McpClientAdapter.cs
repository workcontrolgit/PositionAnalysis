using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PositionAnalysis.Cli;

/// <summary>
/// Wraps McpClient to implement IProcessAllMcpClient for use by ProcessAllRunner.
/// </summary>
internal sealed class McpClientAdapter : IProcessAllMcpClient
{
    private readonly McpClient _client;

    public McpClientAdapter(McpClient client)
    {
        _client = client;
    }

    public async Task<JsonElement> CallToolAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await _client.CallToolAsync(name, arguments: null, progress: null);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }
}
