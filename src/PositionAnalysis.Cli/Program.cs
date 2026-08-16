using Azure;
using Azure.AI.OpenAI;
using elbruno.Extensions.AI.Claude;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OllamaSharp;
using PositionAnalysis.Cli;
using Serilog;
using Spectre.Console;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json",
        optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    // Always write logs under the Cli project's own logs/ folder, regardless of the
    // process's working directory (e.g. when "dotnet run" is invoked from the repo root).
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Serilog:WriteTo:1:Args:path"] = Path.Combine(ResolveProjectLogsDirectory(), "schedulepc-.log")
    })
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

try
{
    var unattendedMode = args.Length == 1 && args[0].Equals("--unattended", StringComparison.OrdinalIgnoreCase);
    var schedulePcMcpLaunchCommand = SchedulePcMcpLaunchResolver.Resolve(AppContext.BaseDirectory);

    if (unattendedMode)
    {
        // Unattended branch: use existing StdioMcpClient + ProcessAllRunner (Task 6 will migrate to McpClient)
        await using var unattendedMcpClient = new StdioMcpClient(schedulePcMcpLaunchCommand);
        await unattendedMcpClient.StartAsync();

        using var unattendedCancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; unattendedCancellation.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => unattendedMcpClient.KillProcess();

        Environment.ExitCode = await new ProcessAllRunner(
            unattendedMcpClient,
            TimeSpan.FromSeconds(30),
            (Func<TimeSpan, CancellationToken, Task>)Task.Delay,
            Log.Logger).RunAsync(unattendedCancellation.Token);
        return;
    }

    // ── PositionAnalysis.Mcp transport ──────────────────────────────────────────
    var paTransport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Command = schedulePcMcpLaunchCommand.Command,
        Arguments = TokenizeArguments(schedulePcMcpLaunchCommand.Arguments),
        WorkingDirectory = schedulePcMcpLaunchCommand.WorkingDirectory,
        Name = "PositionAnalysis.Mcp",
        StandardErrorLines = line => Log.Debug("[MCP] {Line}", line)
    });

    await using var paClient = await McpClient.CreateAsync(paTransport);

    // ── Oracle SQLcl MCP transport (optional) ───────────────────────────────────
    var sqlclPath = configuration["SqlclMcp:Path"];

    McpClient? oracleClient = null;
    if (!string.IsNullOrWhiteSpace(sqlclPath) && File.Exists(sqlclPath))
    {
        var oracleTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = sqlclPath,
            Arguments = ["-mcp"],
            Name = "OracleSqlcl",
            StandardErrorLines = line => Log.Debug("[Oracle MCP] {Line}", line)
        });
        oracleClient = await McpClient.CreateAsync(oracleTransport);
    }
    else
    {
        AnsiConsole.MarkupLine("[yellow]Oracle SQLcl MCP not configured or sql.exe not found — Oracle tools unavailable.[/]");
    }

    await using var oracleClientDisposer = oracleClient;

    // ── Progress notification handler (shared across both clients) ───────────────
    int lastPct = -1;

    ValueTask OnProgressNotification(JsonRpcNotification notification, CancellationToken _ct)
    {
        if (notification.Params?.Deserialize<ProgressNotificationParams>(McpJsonUtilities.DefaultOptions) is { } pn)
        {
            var current = (int)pn.Progress.Progress;
            var total = (int)(pn.Progress.Total ?? 0);
            if (total > 0)
            {
                var pct = (int)(current * 100.0 / total);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    Console.Error.Write($"\r  [{pct,3}%] {current}/{total} scored\u2026   ");
                }
            }
        }
        return ValueTask.CompletedTask;
    }

    await using var paProgressReg = paClient.RegisterNotificationHandler(
        NotificationMethods.ProgressNotification, OnProgressNotification);
    await using var oracleProgressReg = oracleClient?.RegisterNotificationHandler(
        NotificationMethods.ProgressNotification, OnProgressNotification);

    // ── Tool registry ─────────────────────────────────────────────────────────────
    var clients = oracleClient is not null
        ? new[] { paClient, oracleClient }
        : new[] { paClient };

    var registry = new McpToolRegistry();
    await registry.InitializeAsync(clients);

    // ── Chat client + agentic session ─────────────────────────────────────────────
    var provider = configuration["AI:Provider"] ?? "Ollama";
    IChatClient chatClient = BuildChatClient(configuration, provider);
    var modelDisplay = GetModelDisplay(configuration, provider);

    var session = new AgenticChatSession(chatClient, registry, modelDisplay);

    Console.CancelKeyPress += (_, e) => { e.Cancel = true; Environment.Exit(0); };

    await session.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception in PositionAnalysisChatClient");
    AnsiConsole.MarkupLine($"[red]\u274c Fatal error:[/] {Markup.Escape(ex.Message)}");
    AnsiConsole.MarkupLine("[grey]Details logged to logs/error-*.log[/]");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// \u2500\u2500 Helpers \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

static IChatClient BuildChatClient(IConfiguration config, string provider)
{
    if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
    {
        var endpoint = config["AI:Ollama:Endpoint"] ?? "http://localhost:11434";
        var model = config["AI:Ollama:Model"] ?? "mistral";
        var http = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = Timeout.InfiniteTimeSpan };
        IChatClient ollama = (IChatClient)new OllamaApiClient(http, model, null!);

        var numCtxRaw = config["AI:Ollama:NumCtx"];
        if (int.TryParse(numCtxRaw, out var numCtx))
        {
            return ollama.AsBuilder()
                .Use(async (messages, options, next, ct) =>
                {
                    options ??= new ChatOptions();
                    options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                    options.AdditionalProperties["num_ctx"] = numCtx;
                    await next(messages, options, ct);
                })
                .Build();
        }

        return ollama;
    }

    if (string.Equals(provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
    {
        var aoaiEndpoint = config["AI:AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException(
                "Missing AI:AzureOpenAI:Endpoint — set it in user secrets:\n" +
                "  dotnet user-secrets set \"AI:AzureOpenAI:Endpoint\" \"https://<resource>.cognitiveservices.azure.com/\"");
        var aoaiDeployment = config["AI:AzureOpenAI:DeploymentName"] ?? "gpt-4o";
        var aoaiApiKey = config["AI:AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "Missing AI:AzureOpenAI:ApiKey — set it in user secrets:\n" +
                "  dotnet user-secrets set \"AI:AzureOpenAI:ApiKey\" \"<your-key>\"");

        return new AzureOpenAIClient(
                new Uri(aoaiEndpoint),
                new AzureKeyCredential(aoaiApiKey))
            .GetChatClient(aoaiDeployment)
            .AsIChatClient();
    }

    // Default: Claude via Azure AI Foundry
    var claudeEndpoint = config["AI:Claude:Endpoint"]
        ?? throw new InvalidOperationException(
            "Missing AI:Claude:Endpoint — set it in user secrets:\n" +
            "  dotnet user-secrets set \"AI:Claude:Endpoint\" \"https://<resource>.services.ai.azure.com/anthropic/v1/messages\"");
    var claudeDeployment = config["AI:Claude:DeploymentName"] ?? "claude-opus-4-6";
    var claudeApiKey = config["AI:Claude:ApiKey"]
        ?? throw new InvalidOperationException(
            "Missing AI:Claude:ApiKey — set it in user secrets:\n" +
            "  dotnet user-secrets set \"AI:Claude:ApiKey\" \"<your-key>\"");

    return new AzureClaudeClient(
        endpoint: new Uri(claudeEndpoint),
        modelId: claudeDeployment,
        apiKey: claudeApiKey);
}

// Walks up from the running assembly's directory to find the Cli project folder,
// so logs land in src/PositionAnalysis.Cli/logs even when launched from bin/Debug/... or a different cwd.
static string ResolveProjectLogsDirectory()
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
    {
        if (File.Exists(Path.Combine(dir, "PositionAnalysis.Cli.csproj")))
            return Path.Combine(dir, "logs");

        dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    return Path.Combine(Directory.GetCurrentDirectory(), "logs");
}

static string GetModelDisplay(IConfiguration config, string provider) =>
    provider.ToLowerInvariant() switch
    {
        "ollama" => config["AI:Ollama:Model"] ?? "mistral",
        "azureopenai" => config["AI:AzureOpenAI:DeploymentName"] ?? "gpt-4o",
        _ => config["AI:Claude:DeploymentName"] ?? "claude-opus-4-6"
    };

static string[] TokenizeArguments(string args)
{
    if (string.IsNullOrWhiteSpace(args)) return [];
    return Regex.Matches(args, @"[^\s""]+|""[^""]*""")
                .Select(m => m.Value.Trim('"'))
                .ToArray();
}


public sealed class StdioMcpClient : IAsyncDisposable, ISchedulePcMcpClient
{
    private readonly string _command;
    private readonly string _arguments;
    private readonly string? _workingDirectory;
    private readonly string _clientName;
    // Degree-1 semaphore: JSON-RPC over stdio is strictly sequential (one
    // request in flight at a time).  The lock also guards stdout writes so
    // interleaved progress notifications from the server are read in-order.
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _process;
    private int _requestId;

    public StdioMcpClient(SchedulePcMcpLaunchCommand launchCommand)
        : this(launchCommand.Command, launchCommand.Arguments, launchCommand.WorkingDirectory, "PositionAnalysis")
    {
    }

    public StdioMcpClient(string command, string arguments, string? workingDirectory, string clientName)
    {
        _command = command;
        _arguments = arguments;
        _workingDirectory = workingDirectory;
        _clientName = clientName;
    }

    public async Task StartAsync()
    {
        if (_process != null)
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = _command,
            Arguments = _arguments,
            WorkingDirectory = _workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PositionAnalysis.Mcp process");

        // MCP server log lines are consumed silently (not echoed) to keep the chat prompt clean.
        _ = Task.Run(async () =>
        {
            while (_process != null && !_process.HasExited)
            {
                var line = await _process.StandardError.ReadLineAsync();
                if (line == null)
                    break;
            }
        });

        await SendRequestAsync("initialize", new
        {
            clientInfo = new { name = _clientName, version = "0.1.0" },
            protocolVersion = "2024-11-05"
        });
    }

    public async Task<JsonElement> CallToolAsync(string name, object arguments)
        => await CallToolWithProgressAsync(name, arguments, onProgress: null);

    /// <inheritdoc/>
    public async Task<JsonElement> CallToolWithProgressAsync(
        string name,
        object arguments,
        Action<double, double>? onProgress)
    {
        // When the caller wants progress notifications, generate a unique token
        // and embed it in _meta.progressToken per the MCP spec.  The server
        // reads this token and echoes it in every notifications/progress message
        // so the client can correlate them with this specific request.
        object @params = onProgress is not null
            ? new { name, arguments, _meta = new { progressToken = Guid.NewGuid().ToString("N") } }
            : new { name, arguments };

        var result = await SendRequestAsync("tools/call", @params, onProgress);

        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var type) || type.GetString() != "text")
                    continue;

                if (!item.TryGetProperty("text", out var text))
                    continue;

                using var parsed = JsonDocument.Parse(text.GetString() ?? "{}");
                return parsed.RootElement.Clone();
            }
        }

        return result.Clone();
    }

    public async Task<IReadOnlyList<string>> ListToolNamesAsync()
    {
        var result = await SendRequestAsync("tools/list", new { });

        if (!result.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var names = new List<string>();
        foreach (var tool in tools.EnumerateArray())
        {
            if (!tool.TryGetProperty("name", out var nameElement))
                continue;

            var name = nameElement.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name!);
        }

        return names;
    }

    private async Task<JsonElement> SendRequestAsync(string method, object @params)
        => await SendRequestAsync(method, @params, onProgress: null);

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object @params,
        Action<double, double>? onProgress)
    {
        if (_process == null)
            throw new InvalidOperationException("MCP process is not started");

        await _requestLock.WaitAsync();
        try
        {
            var id = Interlocked.Increment(ref _requestId);
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params
            });

            await _process.StandardInput.WriteLineAsync(payload);
            await _process.StandardInput.FlushAsync();

            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync();
                if (line == null)
                    throw new InvalidOperationException("MCP process terminated unexpectedly");

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch
                {
                    continue;
                }

                using (doc)
                {
                    var root = doc.RootElement;

                    // ── Progress notifications ────────────────────────────────
                    // JSON-RPC notifications have no "id" field.  The server
                    // sends these interleaved with the final response while the
                    // batch tool is running.  We dispatch them to the callback
                    // and keep reading until the real response arrives.
                    if (!root.TryGetProperty("id", out var idElement))
                    {
                        if (onProgress is not null &&
                            root.TryGetProperty("method", out var methodEl) &&
                            methodEl.GetString() == "notifications/progress" &&
                            root.TryGetProperty("params", out var progressParams))
                        {
                            var current = progressParams.TryGetProperty("progress", out var p) ? p.GetDouble() : 0;
                            var total   = progressParams.TryGetProperty("total",    out var t) ? t.GetDouble() : 0;
                            onProgress(current, total);
                        }
                        continue; // not a response — keep reading
                    }

                    // ── Regular response ─────────────────────────────────────
                    if (!idElement.TryGetInt32(out var responseId) || responseId != id)
                        continue;

                    if (root.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var m) ? m.GetString() : "Unknown MCP error";
                        throw new InvalidOperationException(message ?? "Unknown MCP error");
                    }

                    if (root.TryGetProperty("result", out var result))
                        return result.Clone();

                    throw new InvalidOperationException("MCP response missing result");
                }
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public void KillProcess()
    {
        try
        {
            if (_process != null && !_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process == null)
            return;

        try
        {
            KillProcess();
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _requestLock.Dispose();
        }
    }
}

