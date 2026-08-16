using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PositionAnalysis.Mcp.MCP;

/// <summary>
/// Lightweight JSON-RPC over stdio bridge for MCP-style tool calls.
/// </summary>
public class McpStdioServer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly McpToolsProvider _toolsProvider;
    private readonly StdioChannel _channel;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<McpStdioServer> _logger;

    // Tracks in-flight tool calls keyed by JSON-RPC request ID so that
    // notifications/cancelled can abort the right background task.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingRequests = new();

    public McpStdioServer(
        McpToolsProvider toolsProvider,
        StdioChannel channel,
        IHostApplicationLifetime lifetime,
        ILogger<McpStdioServer> logger)
    {
        _toolsProvider = toolsProvider;
        _channel = channel;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MCP stdio server started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await Console.In.ReadLineAsync().WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (line == null)
                {
                    // stdin closed — the client process has exited; shut down this server.
                    _logger.LogInformation("stdin closed — stopping MCP server");
                    _lifetime.StopApplication();
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // HandleRequestAsync returns immediately for tool/call (dispatched to background)
                // and synchronously for all other methods, so the read loop never blocks.
                await HandleRequestAsync(line, stoppingToken);
            }
        }
        finally
        {
            // Cancel every in-flight batch when the server stops.
            foreach (var (_, cts) in _pendingRequests)
            {
                try { cts.Cancel(); cts.Dispose(); } catch { }
            }
            _pendingRequests.Clear();
        }

        _logger.LogInformation("MCP stdio server stopped");
    }

    private async Task HandleRequestAsync(string requestLine, CancellationToken stoppingToken)
    {
        JsonNode? request;
        try
        {
            request = JsonNode.Parse(requestLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid JSON request: {Request}", requestLine);
            await _channel.WriteAsync(CreateErrorResponse(null, -32700, "Parse error"), stoppingToken);
            return;
        }

        var idNode = request?["id"];
        var method = request?["method"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(method))
        {
            await _channel.WriteAsync(CreateErrorResponse(idNode, -32600, "Invalid Request: missing method"), stoppingToken);
            return;
        }

        // Notifications have no id and expect no response — handle before the switch.
        if (idNode == null && method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase))
        {
            if (method.Equals("notifications/cancelled", StringComparison.OrdinalIgnoreCase))
            {
                // Cancel the matching in-flight tool call so its batch stops promptly.
                var ridNode = request?["params"]?["requestId"];
                _logger.LogInformation(
                    "Received notifications/cancelled — requestId node: {Raw}, pending keys: [{Keys}]",
                    ridNode?.ToJsonString() ?? "<null>",
                    string.Join(", ", _pendingRequests.Keys));

                if (ridNode is not null)
                {
                    var requestId = NormalizeIdKey(ridNode);
                    if (_pendingRequests.TryRemove(requestId, out var pendingCts))
                    {
                        _logger.LogInformation(
                            "Cancelling in-flight request {RequestId} per client notifications/cancelled", requestId);
                        pendingCts.Cancel();
                        pendingCts.Dispose();
                    }
                    else
                    {
                        _logger.LogWarning(
                            "notifications/cancelled: no pending request found for id {RequestId}", requestId);
                    }
                }
            }
            else
            {
                _logger.LogDebug("Received notification: {Method}", method);
            }
            return;
        }

        try
        {
            switch (method)
            {
                case "initialize":
                    await _channel.WriteAsync(CreateSuccessResponse(idNode, new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "PositionAnalysis.Mcp", version = "0.1.0" }
                    }), stoppingToken);
                    break;

                case "ping":
                    await _channel.WriteAsync(CreateSuccessResponse(idNode, new { }), stoppingToken);
                    break;

                case "tools/list":
                    await _channel.WriteAsync(CreateSuccessResponse(idNode, new
                    {
                        tools = _toolsProvider.ListTools()
                    }), stoppingToken);
                    break;

                case "tools/call":
                    {
                        var paramsNode = request?["params"];
                        var name = paramsNode?["name"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            await _channel.WriteAsync(CreateErrorResponse(idNode, -32602, "Invalid params: name is required"), stoppingToken);
                            break;
                        }

                        var argsNode = paramsNode?["arguments"];
                        var argsElement = JsonSerializer.SerializeToElement(argsNode ?? new JsonObject(), JsonOptions);

                        // MCP spec: the client embeds a progress token in params._meta.progressToken.
                        var progressToken = paramsNode?["_meta"]?["progressToken"]?.GetValue<string>();

                        // Per-request CTS so this tool call can be cancelled independently of the host.
                        var requestId = idNode is not null ? NormalizeIdKey(idNode) : Guid.NewGuid().ToString();
                        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        _pendingRequests[requestId] = requestCts;
                        _logger.LogInformation("Registered pending request {RequestId}", requestId);

                        Func<double, double, Task>? reportProgressAsync = progressToken is not null
                            ? (current, total) => _channel.WriteProgressAsync(progressToken, current, total, stoppingToken)
                            : null;

                        // Dispatch to background so the read loop keeps running and can
                        // receive notifications/cancelled while the batch executes.
                        _ = ExecuteToolCallAsync(
                            name, argsElement, progressToken, reportProgressAsync,
                            idNode, requestId, requestCts, stoppingToken);
                        break;
                    }

                default:
                    await _channel.WriteAsync(CreateErrorResponse(idNode, -32601, $"Method not found: {method}"), stoppingToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing method {Method}", method);
            await _channel.WriteAsync(CreateErrorResponse(idNode, -32000, ex.Message), stoppingToken);
        }
    }

    /// <summary>
    /// Executes a tool call on a background task and writes the JSON-RPC response when done.
    /// Cancelled gracefully when <c>notifications/cancelled</c> arrives for this request ID.
    /// </summary>
    private async Task ExecuteToolCallAsync(
        string name,
        JsonElement argsElement,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        JsonNode? idNode,
        string requestId,
        CancellationTokenSource requestCts,
        CancellationToken stoppingToken)
    {
        try
        {
            var result = await _toolsProvider.CallToolAsync(
                name, argsElement, progressToken, reportProgressAsync, requestCts.Token);

            await _channel.WriteAsync(CreateSuccessResponse(idNode, new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(result, JsonOptions)
                    }
                }
            }), stoppingToken);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Cancelled via notifications/cancelled — send an error response so the client unblocks.
            _logger.LogInformation("Tool call {Name} (request {RequestId}) was cancelled by client", name, requestId);
            try
            {
                await _channel.WriteAsync(
                    CreateErrorResponse(idNode, -32000, "Request cancelled by client"), stoppingToken);
            }
            catch { /* server may be shutting down */ }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed executing tool {Name}", name);
            try
            {
                await _channel.WriteAsync(CreateErrorResponse(idNode, -32000, ex.Message), stoppingToken);
            }
            catch { /* server may be shutting down */ }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool call {Name} aborted due to server shutdown", name);
        }
        finally
        {
            if (_pendingRequests.TryRemove(requestId, out _))
                requestCts.Dispose();
        }
    }

    /// <summary>
    /// Normalizes a JSON-RPC request ID node to a string key for dictionary lookup.
    /// Handles both integer IDs (5 → "5") and string IDs ("5" → "5") so that
    /// the original tools/call id and the notifications/cancelled requestId always match,
    /// regardless of how the SDK serializes the type.
    /// </summary>
    private static string NormalizeIdKey(JsonNode node)
    {
        if (node is JsonValue val)
        {
            if (val.TryGetValue<long>(out var lng)) return lng.ToString();
            if (val.TryGetValue<string>(out var str)) return str;
        }
        return node.ToJsonString();
    }

    private static object CreateSuccessResponse(JsonNode? idNode, object result) => new
    {
        jsonrpc = "2.0",
        id = idNode?.Deserialize<object?>(),
        result
    };

    private static object CreateErrorResponse(JsonNode? idNode, int code, string message) => new
    {
        jsonrpc = "2.0",
        id = idNode?.Deserialize<object?>(),
        error = new { code, message }
    };
}
