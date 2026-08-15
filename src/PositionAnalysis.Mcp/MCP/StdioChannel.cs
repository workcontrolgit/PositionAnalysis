using System.Text.Json;

namespace PositionAnalysis.Mcp.MCP;

/// <summary>
/// Thread-safe singleton for writing JSON-RPC messages to stdout.
///
/// Without this, concurrent progress notifications emitted from Parallel.ForEachAsync
/// worker threads would race against each other and against the final tool response,
/// corrupting the newline-delimited JSON-RPC wire format.
///
/// Both McpStdioServer (tool responses) and ProcessBatchPdsToolHandler (progress
/// notifications) must go through this class — never write to Console.Out directly.
/// </summary>
public sealed class StdioChannel
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    // Degree-1 semaphore: one writer at a time across all threads.
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Serialize <paramref name="message"/> and write it as a single line to stdout.</summary>
    public async Task WriteAsync(object message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _lock.WaitAsync(ct);
        try
        {
            await Console.Out.WriteLineAsync(json.AsMemory(), ct);
            await Console.Out.FlushAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Emit a JSON-RPC <c>notifications/progress</c> message.
    /// Safe to call concurrently from multiple threads.
    /// </summary>
    public Task WriteProgressAsync(string progressToken, double progress, double total, CancellationToken ct = default)
        => WriteAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/progress",
            @params = new { progressToken, progress, total }
        }, ct);
}
