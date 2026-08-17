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
        var unattendedTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = schedulePcMcpLaunchCommand.Command,
            Arguments = TokenizeArguments(schedulePcMcpLaunchCommand.Arguments),
            WorkingDirectory = schedulePcMcpLaunchCommand.WorkingDirectory,
            Name = "PositionAnalysis.Mcp",
            StandardErrorLines = line => Log.Debug("[MCP] {Line}", line)
        });

        await using var unattendedPaClient = await McpClient.CreateAsync(unattendedTransport);

        using var unattendedCancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; unattendedCancellation.Cancel(); };

        Environment.ExitCode = await new ProcessAllRunner(
            unattendedPaClient,
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

    using var interactiveCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        try { interactiveCts.Cancel(); } catch (ObjectDisposedException) { }
    };
    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        try { interactiveCts.Cancel(); } catch (ObjectDisposedException) { }
    };

    // ── Progress notification handler (shared across both clients) ───────────────
    int lastPct = -1;
    bool suppressProgress = false;

    ValueTask OnProgressNotification(JsonRpcNotification notification, CancellationToken _ct)
    {
        if (suppressProgress) return ValueTask.CompletedTask;
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

    var oracleConnectionName = configuration["SqlclMcp:ConnectionName"];
    var maxDisplayRows = int.TryParse(configuration["SqlclMcp:MaxDisplayRows"], out var n) ? n : 50;

    var session = new AgenticChatSession(chatClient, registry, modelDisplay,
        oracleConnectionName: oracleConnectionName,
        maxDisplayRows: maxDisplayRows,
        setSuppressProgress: v => { suppressProgress = v; if (!v) lastPct = -1; });

    await session.RunAsync(interactiveCts.Token);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception in CLI");
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

