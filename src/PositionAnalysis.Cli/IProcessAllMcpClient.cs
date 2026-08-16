using System.Text.Json;

namespace PositionAnalysis.Cli;

/// <summary>
/// Thin abstraction over McpClient used by ProcessAllRunner, enabling unit testing.
/// </summary>
internal interface IProcessAllMcpClient
{
    Task<JsonElement> CallToolAsync(string name, CancellationToken cancellationToken = default);
}
