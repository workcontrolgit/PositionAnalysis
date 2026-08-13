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
    private readonly ILogger<McpStdioServer> _logger;

    public McpStdioServer(McpToolsProvider toolsProvider, ILogger<McpStdioServer> logger)
    {
        _toolsProvider = toolsProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MCP stdio server started");

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
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            await HandleRequestAsync(line, stoppingToken);
        }

        _logger.LogInformation("MCP stdio server stopped");
    }

    private async Task HandleRequestAsync(string requestLine, CancellationToken cancellationToken)
    {
        JsonNode? request;
        try
        {
            request = JsonNode.Parse(requestLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid JSON request: {Request}", requestLine);
            await WriteResponseAsync(CreateErrorResponse(null, -32700, "Parse error"), cancellationToken);
            return;
        }

        var idNode = request?["id"];
        var method = request?["method"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(method))
        {
            await WriteResponseAsync(CreateErrorResponse(idNode, -32600, "Invalid Request: missing method"), cancellationToken);
            return;
        }

        try
        {
            switch (method)
            {
                case "initialize":
                    await WriteResponseAsync(CreateSuccessResponse(idNode, new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "PositionAnalysis.Mcp", version = "0.1.0" }
                    }), cancellationToken);
                    break;

                case "ping":
                    await WriteResponseAsync(CreateSuccessResponse(idNode, new { }), cancellationToken);
                    break;

                case "tools/list":
                    await WriteResponseAsync(CreateSuccessResponse(idNode, new
                    {
                        tools = _toolsProvider.ListTools()
                    }), cancellationToken);
                    break;

                case "tools/call":
                    {
                        var paramsNode = request?["params"];
                        var name = paramsNode?["name"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            await WriteResponseAsync(CreateErrorResponse(idNode, -32602, "Invalid params: name is required"), cancellationToken);
                            break;
                        }

                        var argsNode = paramsNode?["arguments"];
                        var argsElement = JsonSerializer.SerializeToElement(argsNode ?? new JsonObject(), JsonOptions);

                        var result = await _toolsProvider.CallToolAsync(name, argsElement, cancellationToken);
                        await WriteResponseAsync(CreateSuccessResponse(idNode, new
                        {
                            content = new[]
                            {
                                new
                                {
                                    type = "text",
                                    text = JsonSerializer.Serialize(result, JsonOptions)
                                }
                            }
                        }), cancellationToken);
                        break;
                    }

                default:
                    await WriteResponseAsync(CreateErrorResponse(idNode, -32601, $"Method not found: {method}"), cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing method {Method}", method);
            await WriteResponseAsync(CreateErrorResponse(idNode, -32000, ex.Message), cancellationToken);
        }
    }

    private static object CreateSuccessResponse(JsonNode? idNode, object result)
    {
        return new
        {
            jsonrpc = "2.0",
            id = idNode?.Deserialize<object?>(),
            result
        };
    }

    private static object CreateErrorResponse(JsonNode? idNode, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id = idNode?.Deserialize<object?>(),
            error = new
            {
                code,
                message
            }
        };
    }

    private static async Task WriteResponseAsync(object response, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        await Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
        await Console.Out.FlushAsync(cancellationToken);
    }
}
