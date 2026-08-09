using Azure;
using Azure.AI.OpenAI;
using elbruno.Extensions.AI.Claude;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using OllamaSharp;
using SchedulePC;
using Serilog;
using Spectre.Console;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
    var sqlclPath = configuration["SqlclMcp:Path"];

    // Display banner using Spectre.Console
    AnsiConsole.Write(new Rule("[bold cyan]Schedule PC Position Evaluation[/]").RuleStyle("cyan").LeftJustified());
    AnsiConsole.MarkupLine("[grey]Authority: EO Implementing Schedule Policy/Career[/]");
    AnsiConsole.MarkupLine($"[teal]Provider:[/] [bold]{provider}[/]");
    AnsiConsole.MarkupLine($"[teal]Model:[/] [bold]{modelDisplay}[/]\n");

    await using var mcpClient = new StdioMcpClient(schedulePcMcpProjectPath);
    await mcpClient.StartAsync();

    McpClient? oracleMcpClient = null;
    if (!string.IsNullOrWhiteSpace(sqlclPath) && File.Exists(sqlclPath))
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = sqlclPath,
            Arguments = ["-mcp"],
            Name = "sqlcl"
        });

        oracleMcpClient = await McpClient.CreateAsync(transport);
    }
    else
    {
        AnsiConsole.MarkupLine("[yellow]Oracle SQLcl MCP is not configured or sql.exe was not found. Continuing with SchedulePCMcp only.[/]");
    }

    await using var oracleClientDisposer = oracleMcpClient;
    Func<Task<IReadOnlyList<string>>>? oracleToolNamesProvider = oracleMcpClient is null
        ? null
        : () => ListOracleToolNamesAsync(oracleMcpClient);

    var client = new SchedulePCChatClient(chatClient, mcpClient, oracleToolNamesProvider);
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

static async Task<IReadOnlyList<string>> ListOracleToolNamesAsync(McpClient mcpClient)
{
    var tools = (await mcpClient.ListToolsAsync()).Cast<AITool>();
    return tools
        .Select(tool => string.IsNullOrWhiteSpace(tool.Name) ? "(unnamed)" : tool.Name!)
        .ToList();
}

class SchedulePCChatClient
{
    private readonly IChatClient _chatClient;
    private readonly StdioMcpClient _mcpClient;
    private readonly Func<Task<IReadOnlyList<string>>>? _oracleToolNamesProvider;
    private string? _currentRunId;

    public SchedulePCChatClient(
        IChatClient chatClient,
        StdioMcpClient mcpClient,
        Func<Task<IReadOnlyList<string>>>? oracleToolNamesProvider)
    {
        _chatClient = chatClient;
        _mcpClient = mcpClient;
        _oracleToolNamesProvider = oracleToolNamesProvider;
    }

    public async Task RunAsync()
    {
        PrintBanner();
        await ShowAvailableToolsAsync();
        await RunChatLoopAsync();
    }

    private void PrintBanner()
    {
        var panel = new Panel("[bold cyan]Chat Mode[/]")
            .BorderColor(Color.Cyan)
            .Padding(1, 1);
        AnsiConsole.Write(panel);

        AnsiConsole.MarkupLine("[grey]SchedulePC is running in conversational mode.[/]");
        AnsiConsole.MarkupLine("[grey]Type any message to chat. Type [bold]exit[/] to quit.[/]\n");
    }

    private async Task RunChatLoopAsync()
    {
        while (true)
        {
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.Markup("[bold yellow]You ›[/] ");
            string? userInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("\n[green]✓[/] Exiting Schedule PC Chat Client.");
                break;
            }

            await ProcessUserInputAsync(userInput);
        }
    }

    private async Task ProcessUserInputAsync(string userInput)
    {
        AnsiConsole.WriteLine();
        try
        {
            if (await TryHandleNaturalLanguageWorkflowAsync(userInput))
                return;

            await HandleChatFallbackAsync(userInput);
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
            RenderStagingSummaryReport("Staging Report", report);
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

    private async Task ShowAvailableToolsAsync()
    {
        await ShowToolsForClientAsync(_mcpClient, "SchedulePCMcp Tools");
        await ShowOracleToolsAsync();
    }

    private async Task ShowOracleToolsAsync()
    {
        if (_oracleToolNamesProvider is null)
        {
            AnsiConsole.MarkupLine("[yellow]Oracle SQLcl MCP client is not available.[/]");
            return;
        }

        await ShowToolsForProviderAsync(_oracleToolNamesProvider, "Oracle SQLcl MCP Tools");
    }

    private static async Task ShowToolsForClientAsync(StdioMcpClient client, string title)
    {
        await ShowToolsForProviderAsync(client.ListToolNamesAsync, title);
    }

    private static async Task ShowToolsForProviderAsync(Func<Task<IReadOnlyList<string>>> provider, string title)
    {
        IReadOnlyList<string> toolNames;
        try
        {
            toolNames = await provider();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Unable to list {Markup.Escape(title)} right now:[/] {Markup.Escape(ex.Message)}");
            return;
        }

        if (toolNames.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No tools were reported for {Markup.Escape(title)}.[/]");
            return;
        }

        var table = new Spectre.Console.Table()
            .BorderColor(Color.Cyan)
            .AddColumn(new TableColumn("[bold cyan]MCP TOOL[/]"));

        foreach (var toolName in toolNames)
            table.AddRow(Markup.Escape(toolName));

        AnsiConsole.Write(new Rule($"[bold teal]{Markup.Escape(title)}[/]").RuleStyle("teal").LeftJustified());
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private async Task HandleChatFallbackAsync(string userInput)
    {
        IReadOnlyList<string> toolNames;
        try
        {
            toolNames = await _mcpClient.ListToolNamesAsync();
        }
        catch
        {
            toolNames = Array.Empty<string>();
        }
        IReadOnlyList<string> oracleToolNames = Array.Empty<string>();
        if (_oracleToolNamesProvider is not null)
        {
            try
            {
                oracleToolNames = await _oracleToolNamesProvider();
            }
            catch
            {
                oracleToolNames = Array.Empty<string>();
            }
        }

        var scheduleToolsContext = toolNames.Count == 0
            ? "No SchedulePCMcp tools discovered"
            : string.Join(", ", toolNames);
        var oracleToolsContext = oracleToolNames.Count == 0
            ? "No Oracle SQLcl MCP tools discovered"
            : string.Join(", ", oracleToolNames);

        var runContext = string.IsNullOrWhiteSpace(_currentRunId)
            ? "No active run id yet."
            : $"Active run id: {_currentRunId}";

        var prompt =
            "You are SchedulePC Assistant for Schedule Policy/Career evaluations. " +
            "You can answer naturally and guide the user on available MCP workflow commands. " +
            "SchedulePCMcp tools: " + scheduleToolsContext + ". " +
            "Oracle SQLcl MCP tools: " + oracleToolsContext + ". " +
            runContext + " " +
            "User message: " + userInput;

        var response = await _chatClient.GetResponseAsync(prompt);
        var text = response.Text;

        AnsiConsole.MarkupLine("[bold green]Assistant:[/]");
        if (string.IsNullOrWhiteSpace(text))
        {
            AnsiConsole.MarkupLine("[grey](No response)[/]");
            return;
        }

        MarkdigSpectreRenderer.Render(text);
    }

    private async Task<bool> TryHandleNaturalLanguageWorkflowAsync(string userInput)
    {
        var normalized = userInput.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return true;

        if (IsIdentityPrompt(normalized))
        {
            AnsiConsole.MarkupLine("[green]I am SchedulePC Assistant.[/] I can run staging, processing, status, generation, and export through natural language.");
            return true;
        }

        if (IsToolsListPrompt(normalized))
        {
            await ShowAvailableToolsAsync();
            return true;
        }

        if (IsRunIdPrompt(normalized))
        {
            var runId = await ResolveRunIdFromInputAsync(userInput);
            AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(runId)
                ? "[yellow]No run id found yet.[/] Stage data first to create one."
                : $"[green]Current run:[/] [bold]{Markup.Escape(runId)}[/]");
            return true;
        }

        if (IsStagingReportPrompt(normalized))
        {
            var runId = await ResolveRunIdFromInputAsync(userInput, preferLatestWhenUnspecified: true);
            if (string.IsNullOrWhiteSpace(runId))
            {
                AnsiConsole.MarkupLine("[yellow]No run id found.[/] Stage data first to create one, then ask for staging report.");
                return true;
            }

            var report = await _mcpClient.CallToolAsync("get_staging_report", new { runId });
            _currentRunId = runId;
            RenderStagingSummaryReport("Staging Report", report);
            return true;
        }

        if (IsProcessingStatusPrompt(normalized))
        {
            var runId = await ResolveRunIdFromInputAsync(userInput, preferLatestWhenUnspecified: true);
            if (string.IsNullOrWhiteSpace(runId))
            {
                AnsiConsole.MarkupLine("[yellow]No run id found.[/] Stage data first to create one, then ask for processing status.");
                return true;
            }

            _currentRunId = runId;
            await ShowStatusAsync();
            return true;
        }

        if (IsGeneratePrompt(normalized))
        {
            if (!TryEnsureRun())
                return true;

            await GenerateAsync();
            return true;
        }

        if (IsExportPrompt(normalized))
        {
            if (!TryEnsureRun())
                return true;

            await ExportAsync();
            return true;
        }

        if (IsProcessPrompt(normalized))
        {
            if (!TryEnsureRun())
                return true;

            var series = ExtractSeriesCodes(userInput);
            if (series.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Please include one or more series codes.[/] Example: [bold]process series 0301, 0560[/].");
                return true;
            }

            await ProcessAsync(string.Join(",", series));
            return true;
        }

        if (IsStagePrompt(normalized))
        {
            var arguments = BuildStageArguments(userInput);
            await StageAsync(arguments);
            return true;
        }

        return false;
    }

    private static bool IsRunIdPrompt(string normalized) =>
        normalized.Contains("current run") ||
        normalized.Contains("run id") ||
        normalized is "run";

    private static bool IsStagingReportPrompt(string normalized) =>
        normalized.Contains("staging report") ||
        normalized.Contains("stage report") ||
        normalized.Contains("get_staging_report") ||
        normalized.Contains("show staged report") ||
        normalized.Contains("staged report");

    private static bool IsProcessingStatusPrompt(string normalized) =>
        normalized.Contains("processing status") ||
        normalized.Contains("status report") ||
        normalized.Contains("get_processing_status") ||
        normalized is "status" ||
        normalized.Contains("show status");

    private static bool IsStagePrompt(string normalized) =>
        normalized.StartsWith("stage") ||
        normalized.Contains("stage pds") ||
        normalized.Contains("stage data") ||
        normalized.Contains("load for staging");

    private static bool IsProcessPrompt(string normalized) =>
        normalized.StartsWith("process") ||
        normalized.Contains("process pds") ||
        normalized.Contains("start processing") ||
        normalized.Contains("run processing");

    private static bool IsGeneratePrompt(string normalized) =>
        normalized.StartsWith("generate") ||
        normalized.Contains("generate documents") ||
        normalized.Contains("create documents");

    private static bool IsExportPrompt(string normalized) =>
        normalized.StartsWith("export") ||
        normalized.Contains("export results") ||
        normalized.Contains("download excel");

    private bool TryEnsureRun()
    {
        if (!string.IsNullOrWhiteSpace(_currentRunId))
            return true;

        AnsiConsole.MarkupLine("[yellow]No active run yet.[/] Ask to stage data first (for example: [bold]stage series 0301[/]).");
        return false;
    }

    private async Task<string?> ResolveRunIdFromInputAsync(string userInput, bool preferLatestWhenUnspecified = false)
    {
        var runFromInput = ExtractRunId(userInput);
        if (!string.IsNullOrWhiteSpace(runFromInput))
        {
            _currentRunId = runFromInput;
            return runFromInput;
        }

        if (preferLatestWhenUnspecified)
        {
            var latestPreferred = await TryGetLatestRunIdAsync();
            if (!string.IsNullOrWhiteSpace(latestPreferred))
            {
                _currentRunId = latestPreferred;
                return latestPreferred;
            }
        }

        if (!string.IsNullOrWhiteSpace(_currentRunId))
            return _currentRunId;

        var latest = await TryGetLatestRunIdAsync();
        if (!string.IsNullOrWhiteSpace(latest))
        {
            _currentRunId = latest;
            return latest;
        }

        return null;
    }

    private async Task<string?> TryGetLatestRunIdAsync()
    {

        try
        {
            var latest = await _mcpClient.CallToolAsync("get_latest_run", new { });
            if (latest.TryGetProperty("runId", out var runIdEl))
            {
                var latestRunId = runIdEl.GetString();
                if (!string.IsNullOrWhiteSpace(latestRunId))
                    return latestRunId;
            }
        }
        catch
        {
            // If latest-run lookup fails, caller handles null with guidance.
        }

        return null;
    }

    private static Dictionary<string, object?> BuildStageArguments(string userInput)
    {
        var args = new Dictionary<string, object?>();
        var series = ExtractSeriesCodes(userInput);
        if (series.Count == 1)
            args["series"] = series[0];

        var gradeRegex = new Regex(@"(?:grade|gs\-?)\s*(\d{1,2})(?:\s*[-to]+\s*(\d{1,2}))?", RegexOptions.IgnoreCase);
        var gradeMatch = gradeRegex.Match(userInput);
        if (gradeMatch.Success && int.TryParse(gradeMatch.Groups[1].Value, out var minGrade))
        {
            var maxGrade = minGrade;
            if (gradeMatch.Groups.Count > 2 && int.TryParse(gradeMatch.Groups[2].Value, out var parsedMax))
                maxGrade = parsedMax;

            args["gradeMin"] = minGrade;
            args["gradeMax"] = maxGrade;
        }

        var orgRegex = new Regex(@"(?:org|organization|org code)\s+([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        var orgMatch = orgRegex.Match(userInput);
        if (orgMatch.Success)
            args["orgCode"] = orgMatch.Groups[1].Value;

        return args;
    }

    private static List<string> ExtractSeriesCodes(string input)
    {
        var matches = Regex.Matches(input, @"\b\d{4}\b");
        return matches
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ExtractRunId(string input)
    {
        var match = Regex.Match(input, @"\b\d{4}-\d{2}-\d{2}-\d{4}\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
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

    private static void RenderStagingSummaryReport(string title, JsonElement report)
    {
        AnsiConsole.Write(new Rule($"[bold teal]{Markup.Escape(title)}[/]").RuleStyle("teal").LeftJustified());
        AnsiConsole.WriteLine();

        var runId = report.TryGetProperty("runId", out var runIdElement)
            ? runIdElement.GetString() ?? string.Empty
            : string.Empty;

        var table = new Spectre.Console.Table()
            .BorderColor(Color.Teal)
            .AddColumn(new TableColumn("[bold cyan]RUN_ID[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]SERIES[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]STAGED[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]IN_PROGRESS[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]COMPLETE[/]").Centered())
            .AddColumn(new TableColumn("[bold cyan]FAILED[/]").Centered());

        if (report.TryGetProperty("series", out var seriesArray) && seriesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in seriesArray.EnumerateArray())
            {
                var series = item.TryGetProperty("series", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                var staged = item.TryGetProperty("staged", out var st) && st.TryGetInt32(out var stagedValue) ? stagedValue : 0;
                var inProgress = item.TryGetProperty("inProgress", out var ip) && ip.TryGetInt32(out var inProgressValue) ? inProgressValue : 0;
                var complete = item.TryGetProperty("complete", out var c) && c.TryGetInt32(out var completeValue) ? completeValue : 0;
                var failed = item.TryGetProperty("failed", out var f) && f.TryGetInt32(out var failedValue) ? failedValue : 0;

                table.AddRow(
                    Markup.Escape(runId),
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

    private static bool IsIdentityPrompt(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        return normalized is "who are you" or "who r u" or "whoami" or "who am i";
    }

    private static bool IsToolsListPrompt(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        return normalized is "list mcp tools" or "show mcp tools" or "what mcp tools" or "what are the mcp tools";
    }
}

public sealed class StdioMcpClient : IAsyncDisposable
{
    private readonly string _command;
    private readonly string _arguments;
    private readonly string? _workingDirectory;
    private readonly string _clientName;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _process;
    private int _requestId;

    public StdioMcpClient(string projectPath)
        : this("dotnet", "run", projectPath, "SchedulePC")
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
            clientInfo = new { name = _clientName, version = "0.1.0" },
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
