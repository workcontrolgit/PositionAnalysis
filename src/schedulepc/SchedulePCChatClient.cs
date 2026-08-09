using Azure;
using Azure.AI.OpenAI;
using elbruno.Extensions.AI.Claude;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using SchedulePC;
using Serilog;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json",
        optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var provider = configuration["AI:Provider"] ?? "Ollama";
    IChatClient chatClient = BuildChatClient(configuration, provider);
    var modelDisplay = GetModelDisplay(configuration, provider);
    var schedulePcMcpProjectPath = configuration["SchedulePCMcp:ProjectPath"]
        ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "SchedulePCMcp"));

    // Display banner using Spectre.Console
    AnsiConsole.Write(new Rule("[bold cyan]Schedule PC Position Evaluation[/]").RuleStyle("cyan").LeftJustified());
    AnsiConsole.MarkupLine("[grey]Authority: EO Implementing Schedule Policy/Career[/]");
    AnsiConsole.MarkupLine($"[teal]Provider:[/] [bold]{provider}[/]");
    AnsiConsole.MarkupLine($"[teal]Model:[/] [bold]{modelDisplay}[/]\n");

    await using var mcpClient = new StdioMcpClient(schedulePcMcpProjectPath);
    await mcpClient.StartAsync();

    var client = new SchedulePCChatClient(chatClient, mcpClient);
    await client.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception in SchedulePCChatClient");
    AnsiConsole.MarkupLine($"[red]❌ Fatal error:[/] {Markup.Escape(ex.Message)}");
    AnsiConsole.MarkupLine("[grey]Details logged to logs/error-*.log[/]");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// ── Helpers ───────────────────────────────────────────────────────────────────

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

static string GetModelDisplay(IConfiguration config, string provider) =>
    provider.ToLowerInvariant() switch
    {
        "ollama" => config["AI:Ollama:Model"] ?? "mistral",
        "azureopenai" => config["AI:AzureOpenAI:DeploymentName"] ?? "gpt-4o",
        _ => config["AI:Claude:DeploymentName"] ?? "claude-opus-4-6"
    };

class SchedulePCChatClient
{
    private readonly IChatClient _chatClient;
    private readonly StdioMcpClient _mcpClient;
    private string? _currentRunId;

    public SchedulePCChatClient(IChatClient chatClient, StdioMcpClient mcpClient)
    {
        _chatClient = chatClient;
        _mcpClient = mcpClient;
    }

    public async Task RunAsync()
    {
        PrintBanner();
        await RunChatLoopAsync();
    }

    private void PrintBanner()
    {
        var panel = new Panel("[bold cyan]Available Commands[/]")
            .BorderColor(Color.Cyan)
            .Padding(1, 1);
        AnsiConsole.Write(panel);

        AnsiConsole.MarkupLine("[grey]Run SchedulePC pipeline through MCP (SchedulePCMcp):[/]\n");
        AnsiConsole.MarkupLine("  [cyan]series {CODE}[/]        Stage PDs by occupational series (e.g., [bold]series 0301[/])");
        AnsiConsole.MarkupLine("  [cyan]grade {GRADE}[/]        Stage PDs by grade (e.g., [bold]grade 13[/] or [bold]grade 13-15[/])");
        AnsiConsole.MarkupLine("  [cyan]org {CODE}[/]           Stage PDs by org code (e.g., [bold]org EXEC-POL[/])");
        AnsiConsole.MarkupLine("  [cyan]stage[/]                Stage PDs with no filter");
        AnsiConsole.MarkupLine("  [cyan]run[/]                  Show current run id");
        AnsiConsole.MarkupLine("  [cyan]status[/]               Show processing status for current run");
        AnsiConsole.MarkupLine("  [cyan]process {S1,S2}[/]      Score staged PDs for series list (e.g., [bold]process 0301,0560[/])");
        AnsiConsole.MarkupLine("  [cyan]generate[/]             Generate Word documents for current run");
        AnsiConsole.MarkupLine("  [cyan]export[/]               Export current run to Excel");
        AnsiConsole.MarkupLine("  [cyan]help[/]               Show this help message");
        AnsiConsole.MarkupLine("  [cyan]exit[/]               Quit the application\n");
    }

    private async Task RunChatLoopAsync()
    {
        while (true)
        {
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.Markup("[bold cyan]📋 SchedulePC>[/] ");
            string? userInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("\n[green]✓[/] Exiting Schedule PC Chat Client.");
                break;
            }

            if (userInput.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                PrintBanner();
                continue;
            }

            await ProcessUserInputAsync(userInput);
        }
    }

    private async Task ProcessUserInputAsync(string userInput)
    {
        AnsiConsole.WriteLine();

        var tokens = userInput.Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return;

        string command = tokens[0].ToLowerInvariant();
        string value = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : "";
        try
        {
            switch (command)
            {
                case "series":
                    await StageAsync(new Dictionary<string, object?> { ["series"] = value.Trim() });
                    break;

                case "grade":
                    if (!TryParseGradeRange(value, out var gradeMin, out var gradeMax))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid grade format. Use[/] [bold]grade 13[/] [red]or[/] [bold]grade 13-15[/].");
                        return;
                    }
                    await StageAsync(new Dictionary<string, object?>
                    {
                        ["gradeMin"] = gradeMin,
                        ["gradeMax"] = gradeMax
                    });
                    break;

                case "org":
                    await StageAsync(new Dictionary<string, object?> { ["orgCode"] = value.Trim() });
                    break;

                case "stage":
                    await StageAsync(new Dictionary<string, object?>());
                    break;

                case "run":
                    AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(_currentRunId)
                        ? "[yellow]No active run yet. Stage first.[/]"
                        : $"[green]Current run:[/] [bold]{Markup.Escape(_currentRunId)}[/]");
                    break;

                case "status":
                    await EnsureRunAsync();
                    await ShowStatusAsync();
                    break;

                case "process":
                    await EnsureRunAsync();
                    await ProcessAsync(value);
                    break;

                case "generate":
                    await EnsureRunAsync();
                    await GenerateAsync();
                    break;

                case "export":
                    await EnsureRunAsync();
                    await ExportAsync();
                    break;

                case "pd":
                    AnsiConsole.MarkupLine("[yellow]The MCP server currently does not expose a PD-specific lookup tool.[/]");
                    break;

                default:
                    AnsiConsole.MarkupLine("[yellow]Unknown command.[/] Type [bold]help[/] to see MCP commands.");
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]MCP command failed:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task StageAsync(Dictionary<string, object?> arguments)
    {
        var result = await _mcpClient.CallToolAsync("stage_pds", arguments);
        _currentRunId = result.GetProperty("runId").GetString();

        AnsiConsole.MarkupLine($"[green]Staging complete.[/] Run: [bold]{Markup.Escape(_currentRunId ?? "") }[/]");

        if (!string.IsNullOrWhiteSpace(_currentRunId))
        {
            var report = await _mcpClient.CallToolAsync("get_staging_report", new { runId = _currentRunId });
            RenderSeriesReport("Staging Report", report);
        }
    }

    private async Task ShowStatusAsync()
    {
        var status = await _mcpClient.CallToolAsync("get_processing_status", new { runId = _currentRunId });
        RenderSeriesReport("Processing Status", status);
    }

    private async Task ProcessAsync(string seriesInput)
    {
        var series = ParseSeriesList(seriesInput);
        if (series.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Provide one or more series.[/] Example: [bold]process 0301,0560[/]");
            return;
        }

        var result = await _mcpClient.CallToolAsync("process_pds_by_series", new
        {
            runId = _currentRunId,
            series
        });

        AnsiConsole.MarkupLine($"[green]{Markup.Escape(result.GetProperty("status").GetString() ?? "processing_started")}[/]");
    }

    private async Task GenerateAsync()
    {
        var result = await _mcpClient.CallToolAsync("generate_documents", new { runId = _currentRunId });
        AnsiConsole.MarkupLine($"[green]Documents generated:[/] [bold]{result.GetProperty("generated").GetInt32()}[/]");
    }

    private async Task ExportAsync()
    {
        var result = await _mcpClient.CallToolAsync("export_results", new { runId = _currentRunId });
        AnsiConsole.MarkupLine($"[green]Export status:[/] [bold]{result.GetProperty("exported").GetInt32()}[/] / {result.GetProperty("total").GetInt32()}");
    }

    private static List<string> ParseSeriesList(string input)
    {
        return input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryParseGradeRange(string value, out int gradeMin, out int gradeMax)
    {
        gradeMin = 0;
        gradeMax = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Contains('-'))
        {
            var parts = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out gradeMin) || !int.TryParse(parts[1], out gradeMax))
                return false;
        }
        else
        {
            if (!int.TryParse(trimmed, out gradeMin))
                return false;
            gradeMax = gradeMin;
        }

        return gradeMin >= 1 && gradeMax <= 15 && gradeMin <= gradeMax;
    }

    private Task EnsureRunAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentRunId))
            throw new InvalidOperationException("No active run. Stage first with series/grade/org/stage.");

        return Task.CompletedTask;
    }

    private static void RenderSeriesReport(string title, JsonElement report)
    {
        AnsiConsole.Write(new Rule($"[bold teal]{Markup.Escape(title)}[/]").RuleStyle("teal").LeftJustified());
        AnsiConsole.WriteLine();

        var table = new Spectre.Console.Table()
            .BorderColor(Color.Teal)
            .AddColumn(new TableColumn("[bold cyan]SERIES[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]STAGED[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]IN_PROGRESS[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]COMPLETE[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]FAILED[/]").Centered());

        if (report.TryGetProperty("series", out var seriesArray) && seriesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in seriesArray.EnumerateArray())
            {
                var series = item.TryGetProperty("series", out var s) ? s.GetString() ?? "" : "";
                var staged = item.TryGetProperty("staged", out var st) && st.TryGetInt32(out var stagedValue) ? stagedValue : 0;
                var inProgress = item.TryGetProperty("inProgress", out var ip) && ip.TryGetInt32(out var inProgressValue) ? inProgressValue : 0;
                var complete = item.TryGetProperty("complete", out var c) && c.TryGetInt32(out var completeValue) ? completeValue : 0;
                var failed = item.TryGetProperty("failed", out var f) && f.TryGetInt32(out var failedValue) ? failedValue : 0;

                table.AddRow(
                    Markup.Escape(series),
                    staged.ToString(),
                    inProgress.ToString(),
                    complete.ToString(),
                    failed.ToString());
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}

public sealed class StdioMcpClient : IAsyncDisposable
{
    private readonly string _projectPath;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _process;
    private int _requestId;

    public StdioMcpClient(string projectPath)
    {
        _projectPath = projectPath;
    }

    public async Task StartAsync()
    {
        if (_process != null)
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run",
            WorkingDirectory = _projectPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start SchedulePCMcp process");

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
            clientInfo = new { name = "SchedulePC", version = "0.1.0" },
            protocolVersion = "2024-11-05"
        });
    }

    public async Task<JsonElement> CallToolAsync(string name, object arguments)
    {
        var result = await SendRequestAsync("tools/call", new { name, arguments });

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

    private async Task<JsonElement> SendRequestAsync(string method, object @params)
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
                    if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var responseId) || responseId != id)
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

    public async ValueTask DisposeAsync()
    {
        if (_process == null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":9999,\"method\":\"shutdown\"}");
                await _process.StandardInput.FlushAsync();
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // no-op during shutdown
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _requestLock.Dispose();
        }
    }
}
