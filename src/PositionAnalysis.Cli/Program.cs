using Azure;
using Azure.AI.OpenAI;
using elbruno.Extensions.AI.Claude;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
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
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

try
{
    var processAllMode = args.Length == 1 && args[0].Equals("--process-all", StringComparison.OrdinalIgnoreCase);
    var schedulePcMcpLaunchCommand = SchedulePcMcpLaunchResolver.Resolve(AppContext.BaseDirectory);

    await using var mcpClient = new StdioMcpClient(schedulePcMcpLaunchCommand);
    await mcpClient.StartAsync();

    using var processAllCancellation = new CancellationTokenSource();
    AppDomain.CurrentDomain.ProcessExit += (_, _) => mcpClient.KillProcess();

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        if (processAllMode)
        {
            processAllCancellation.Cancel();
            return;
        }

        mcpClient.KillProcess();
        Environment.Exit(0);
    };

    if (processAllMode)
    {
        Environment.ExitCode = await new ProcessAllRunner(
            mcpClient,
            TimeSpan.FromSeconds(30),
            (Func<TimeSpan, CancellationToken, Task>)Task.Delay,
            Log.Logger).RunAsync(processAllCancellation.Token);
        return;
    }

    var provider = configuration["AI:Provider"] ?? "Ollama";
    IChatClient chatClient = BuildChatClient(configuration, provider);
    var modelDisplay = GetModelDisplay(configuration, provider);
    var sqlclPath = configuration["SqlclMcp:Path"];

    // Display banner using Spectre.Console
    AnsiConsole.Write(new Rule("[bold cyan]Schedule PC Position Evaluation[/]").RuleStyle("cyan").LeftJustified());
    AnsiConsole.MarkupLine("[grey]Authority: EO Implementing Schedule Policy/Career[/]");
    AnsiConsole.MarkupLine($"[teal]Provider:[/] [bold]{provider}[/]");
    AnsiConsole.MarkupLine($"[teal]Model:[/] [bold]{modelDisplay}[/]\n");

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
        AnsiConsole.MarkupLine("[yellow]Oracle SQLcl MCP is not configured or sql.exe was not found. Continuing with PositionAnalysis.Mcp only.[/]");
    }

    await using var oracleClientDisposer = oracleMcpClient;
    Func<Task<IReadOnlyList<string>>>? oracleToolNamesProvider = oracleMcpClient is null
        ? null
        : () => ListOracleToolNamesAsync(oracleMcpClient);

    var client = new PositionAnalysisChatClient(chatClient, mcpClient, oracleToolNamesProvider);
    await client.RunAsync();
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

class PositionAnalysisChatClient
{
    private readonly IChatClient _chatClient;
    private readonly StdioMcpClient _mcpClient;
    private readonly Func<Task<IReadOnlyList<string>>>? _oracleToolNamesProvider;

    public PositionAnalysisChatClient(
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

        AnsiConsole.MarkupLine("[grey]PositionAnalysis is running in conversational mode.[/]");
        AnsiConsole.MarkupLine("[grey]Type any message to chat. Type [bold]exit[/] to quit.[/]\n");
    }

    private async Task RunChatLoopAsync()
    {
        while (true)
        {
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.Markup("[bold yellow]You ║[/] ");
            string? userInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("\n[green]\u2714[/] Exiting Schedule PC Chat Client.");
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
        var stagedCount = result.TryGetProperty("stagedCount", out var sc) && sc.TryGetInt32(out var n) ? n : 0;

        AnsiConsole.MarkupLine($"[green]Staging complete.[/] Staged: [bold]{stagedCount}[/] position descriptions.");

        var report = await _mcpClient.CallToolAsync("get_staging_report", new { });
        RenderStagingSummaryReport("Staging Report", report);
    }

    private async Task ShowStatusAsync()
    {
        var status = await _mcpClient.CallToolAsync("get_processing_status", new { });
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
            series
        });

        AnsiConsole.MarkupLine($"[green]{Markup.Escape(result.GetProperty("status").GetString() ?? "processing_started")}[/]");
    }

    private async Task GenerateAsync()
    {
        var result = await _mcpClient.CallToolAsync("generate_documents", new { });
        AnsiConsole.MarkupLine($"[green]Documents generated:[/] [bold]{result.GetProperty("generated").GetInt32()}[/]");
    }

    private async Task ExportAsync()
    {
        var result = await _mcpClient.CallToolAsync("export_results", new { });
        AnsiConsole.MarkupLine($"[green]Export status:[/] [bold]{result.GetProperty("exported").GetInt32()}[/] / {result.GetProperty("total").GetInt32()}");
    }

    private async Task RescoreBySeriesAsync(string seriesInput)
    {
        var series = ParseSeriesList(seriesInput);
        if (series.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Please include one or more series codes.[/] Example: [bold]rescore series 0110,0301[/]");
            return;
        }

        var result = await _mcpClient.CallToolAsync("rescore_pds_by_series", new { series });
        var status = result.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        AnsiConsole.MarkupLine($"[green]Rescore complete:[/] series [bold]{Markup.Escape(string.Join(", ", series))}[/]");
        if (!string.IsNullOrWhiteSpace(status))
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(status)}[/]");
    }

    private async Task RescoreAllAsync()
    {
        var result = await _mcpClient.CallToolAsync("rescore_all_pds", new { });
        var status = result.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        AnsiConsole.MarkupLine("[green]Rescore complete:[/] all staged PDs");
        if (!string.IsNullOrWhiteSpace(status))
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(status)}[/]");
    }

    private async Task RescorePdAsync(string pdNbr)
    {
        var result = await _mcpClient.CallToolAsync("rescore_pd", new { pd_nbr = pdNbr });
        var status = result.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        AnsiConsole.MarkupLine($"[green]Rescore complete:[/] PD [bold]{Markup.Escape(pdNbr)}[/]");
        if (!string.IsNullOrWhiteSpace(status))
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(status)}[/]");
    }

    private async Task RescoreHumanSchedulePcAsync()
    {
        var result = await _mcpClient.CallToolAsync("rescore_human_schedule_pc_pds", new { });

        if (result.TryGetProperty("error", out var errorEl))
        {
            AnsiConsole.MarkupLine($"[red]Rescore refused:[/] {Markup.Escape(errorEl.GetString() ?? "")}");
            return;
        }

        var selectedCount = result.TryGetProperty("selectedCount", out var sc) && sc.TryGetInt32(out var n1) ? n1 : 0;
        var scoredCount = result.TryGetProperty("scoredCount", out var scd) && scd.TryGetInt32(out var n2) ? n2 : 0;
        var failedCount = result.TryGetProperty("failedCount", out var fc) && fc.TryGetInt32(out var n3) ? n3 : 0;
        var status = result.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

        AnsiConsole.MarkupLine($"[green]Rescore complete:[/] {scoredCount}/{selectedCount} human-flagged Schedule P/C PDs (failed: {failedCount})");
        if (!string.IsNullOrWhiteSpace(status))
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(status)}[/]");

        if (failedCount > 0 && result.TryGetProperty("failedPdNumbers", out var failedEl))
        {
            var failedPdNumbers = failedEl.EnumerateArray().Select(e => e.GetString()).Where(v => v is not null);
            AnsiConsole.MarkupLine($"[yellow]Failed PDs:[/] {Markup.Escape(string.Join(", ", failedPdNumbers))}");
        }
    }

    private async Task RetryFailedAsync()
    {
        var result = await _mcpClient.CallToolAsync("retry_failed_pds", new { });
        var resetCount = result.TryGetProperty("resetCount", out var r) && r.TryGetInt32(out var n) ? n : 0;
        var status = result.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        AnsiConsole.MarkupLine($"[green]Retry reset:[/] [bold]{resetCount}[/] failed PD(s) reset to PENDING.");
        if (!string.IsNullOrWhiteSpace(status))
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(status)}[/]");
    }

    private async Task ProcessAllAsync()
    {
        var result = await _mcpClient.CallToolAsync("process_all_pds", new { });
        var status = result.TryGetProperty("status", out var s) ? s.GetString() ?? "processing_started" : "processing_started";
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(status)}[/] — scoring all staged PDs across all series.");
    }

    private async Task ClearSchedulePcEvalAsync()
    {
        var result = await _mcpClient.CallToolAsync("clear_schedule_pc_eval", new { });
        var deleted = result.TryGetProperty("deletedCount", out var d) && d.TryGetInt32(out var count) ? count : 0;
        AnsiConsole.MarkupLine($"[green]SCHEDULE_PC_EVAL cleared.[/] Deleted rows: [bold]{deleted}[/]");
    }

    private async Task ShowAvailableToolsAsync()
    {
        await ShowToolsForClientAsync(_mcpClient, "PositionAnalysis.Mcp Tools");
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
            ? "No PositionAnalysis.Mcp tools discovered"
            : string.Join(", ", toolNames);
        var oracleToolsContext = oracleToolNames.Count == 0
            ? "No Oracle SQLcl MCP tools discovered"
            : string.Join(", ", oracleToolNames);

        var prompt =
            "You are PositionAnalysis Assistant for Schedule Policy/Career evaluations. " +
            "You can answer naturally and guide the user on available MCP workflow commands. " +
            "PositionAnalysis.Mcp tools: " + scheduleToolsContext + ". " +
            "Oracle SQLcl MCP tools: " + oracleToolsContext + ". " +
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
            AnsiConsole.MarkupLine("[green]I am PositionAnalysis Assistant.[/] I can run staging, processing, status, generation, and export through natural language.");
            return true;
        }

        if (IsToolsListPrompt(normalized))
        {
            await ShowAvailableToolsAsync();
            return true;
        }

        if (IsClearTablePrompt(normalized))
        {
            await ClearSchedulePcEvalAsync();
            return true;
        }

        if (IsStagingReportPrompt(normalized))
        {
            var report = await _mcpClient.CallToolAsync("get_staging_report", new { });
            RenderStagingSummaryReport("Staging Report", report);
            return true;
        }

        if (IsProcessingStatusPrompt(normalized))
        {
            await ShowStatusAsync();
            return true;
        }

        if (IsGeneratePrompt(normalized))
        {
            await GenerateAsync();
            return true;
        }

        if (IsExportPrompt(normalized))
        {
            await ExportAsync();
            return true;
        }

        if (IsRetryFailedPrompt(normalized))
        {
            await RetryFailedAsync();
            return true;
        }

        if (IsRescoreAllPrompt(normalized))
        {
            await RescoreAllAsync();
            return true;
        }

        if (IsRescoreBySeriesPrompt(normalized))
        {
            await RescoreBySeriesAsync(userInput);
            return true;
        }

        if (IsRescoreHumanSchedulePcPrompt(normalized))
        {
            await RescoreHumanSchedulePcAsync();
            return true;
        }

        if (IsRescorePdPrompt(normalized))
        {
            var pdNbr = ExtractPdNbr(userInput);
            if (string.IsNullOrWhiteSpace(pdNbr))
            {
                AnsiConsole.MarkupLine("[yellow]Please include a PD number.[/] Example: [bold]rescore_pd 200028[/]");
                return true;
            }

            await RescorePdAsync(pdNbr);
            return true;
        }

        if (IsProcessAllPrompt(normalized))
        {
            await ProcessAllAsync();
            return true;
        }

        if (IsProcessPrompt(normalized))
        {
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

    private static bool IsClearTablePrompt(string normalized) =>
        normalized.Contains("clear schedule_pc_eval") ||
        normalized.Contains("clear schedule pc eval") ||
        normalized.Contains("remove all records") ||
        normalized.Contains("delete all records") ||
        normalized.Contains("truncate schedule_pc_eval") ||
        normalized.Contains("reset staging table");

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
        var matches = Regex.Matches(input, @"\b\d{5}\b");
        return matches
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                var series = item.TryGetProperty("series", out var s) ? s.GetString() ?? string.Empty : string.Empty;
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

    private static bool IsIdentityPrompt(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        return normalized is "who are you" or "who r u" or "whoami" or "who am i";
    }

    private static bool IsRetryFailedPrompt(string normalized) =>
        normalized.Contains("retry_failed_pds") ||
        normalized.Contains("retry failed") ||
        normalized.Contains("reset failed") ||
        normalized.Contains("requeue failed");

    private static bool IsProcessAllPrompt(string normalized) =>
        normalized.Contains("process_all_pds") ||
        normalized.Contains("process all pds") ||
        normalized.Contains("process all") ||
        normalized.Contains("score all");

    private static bool IsRescoreAllPrompt(string normalized) =>
        normalized.Contains("rescore_all") ||
        normalized.Contains("rescore all") ||
        normalized.Contains("re-score all") ||
        normalized.Contains("force rescore all");

    private static bool IsRescoreBySeriesPrompt(string normalized) =>
        normalized.Contains("rescore_pds_by_series") ||
        normalized.Contains("rescore series") ||
        normalized.Contains("rescore by series") ||
        normalized.Contains("re-score series");

    private static bool IsRescoreHumanSchedulePcPrompt(string normalized) =>
        normalized.Contains("rescore_human_schedule_pc_pds") ||
        normalized.Contains("rescore human") ||
        normalized.Contains("re-score human") ||
        normalized.Contains("rescore the 90") ||
        normalized.Contains("score the 90");

    private static bool IsRescorePdPrompt(string normalized) =>
        normalized.StartsWith("rescore_pd") ||
        normalized.StartsWith("rescore pd") ||
        normalized.Contains("rescore pd") ||
        normalized.Contains("rescore_pd") ||
        normalized.Contains("force score") ||
        normalized.Contains("re-score pd");

    private static string ExtractPdNbr(string userInput)
    {
        var match = Regex.Match(userInput, @"\b(\d{5,})\b");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static bool IsToolsListPrompt(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        return normalized is "list mcp tools" or "show mcp tools" or "what mcp tools" or "what are the mcp tools";
    }
}

public sealed class StdioMcpClient : IAsyncDisposable, ISchedulePcMcpClient
{
    private readonly string _command;
    private readonly string _arguments;
    private readonly string? _workingDirectory;
    private readonly string _clientName;
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

public sealed class ProcessAllRunner
{
    private readonly ISchedulePcMcpClient _mcpClient;
    private readonly TimeSpan _pollInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ILogger _logger;

    public ProcessAllRunner(
        ISchedulePcMcpClient mcpClient,
        TimeSpan pollInterval,
        Func<TimeSpan, Task> delayAsync,
        ILogger? logger = null)
        : this(mcpClient, pollInterval, (delay, _) => delayAsync(delay), logger)
    {
    }

    public ProcessAllRunner(
        ISchedulePcMcpClient mcpClient,
        TimeSpan pollInterval,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        ILogger? logger = null)
    {
        _mcpClient = mcpClient;
        _pollInterval = pollInterval;
        _delayAsync = delayAsync;
        _logger = logger ?? Log.Logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Information("Starting unattended Schedule PC scoring run");
            cancellationToken.ThrowIfCancellationRequested();
            await _mcpClient.CallToolAsync("process_all_pds", new { });

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await _mcpClient.CallToolAsync("get_queue_status", new { });
                var status = ParseQueueStatus(response);

                _logger.Information(
                    "Schedule PC queue status: Pending={Pending}, InProgress={InProgress}, Complete={Complete}, Failed={Failed}, IsDrained={IsDrained}, RunFailed={RunFailed}",
                    status.Pending,
                    status.InProgress,
                    status.Complete,
                    status.Failed,
                    status.IsDrained,
                    status.RunFailed);

                if (status.RunFailed)
                {
                    _logger.Error("Schedule PC unattended scoring run failed: {RunError}", status.RunError);
                    return 1;
                }

                if (status.IsDrained)
                {
                    var exitCode = status.Failed == 0 ? 0 : 1;
                    _logger.Information("Schedule PC unattended scoring run finished with exit code {ExitCode}", exitCode);
                    return exitCode;
                }

                await _delayAsync(_pollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Information("Schedule PC unattended scoring run was cancelled");
            return 2;
        }
    }

    private static QueueStatusResponse ParseQueueStatus(JsonElement response)
    {
        var pending = GetRequiredInt32(response, "pending");
        var inProgress = GetRequiredInt32(response, "inProgress");
        var isDrained = GetRequiredBoolean(response, "isDrained");
        var calculatedDrained = pending == 0 && inProgress == 0;
        if (isDrained != calculatedDrained)
            throw new InvalidOperationException("get_queue_status response has an isDrained field inconsistent with pending and inProgress.");

        return new QueueStatusResponse(
            pending,
            inProgress,
            GetRequiredInt32(response, "complete"),
            GetRequiredInt32(response, "failed"),
            isDrained,
            GetRequiredBoolean(response, "runFailed"),
            GetRequiredNullableString(response, "runError"));
    }

    private static int GetRequiredInt32(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value))
            throw new InvalidOperationException($"get_queue_status response is missing a valid integer '{propertyName}' field.");

        return value;
    }

    private static bool GetRequiredBoolean(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) || property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"get_queue_status response is missing a valid boolean '{propertyName}' field.");

        return property.GetBoolean();
    }

    private static string? GetRequiredNullableString(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) || property.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
            throw new InvalidOperationException($"get_queue_status response is missing a valid string or null '{propertyName}' field.");

        return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
    }

    private sealed record QueueStatusResponse(
        int Pending,
        int InProgress,
        int Complete,
        int Failed,
        bool IsDrained,
        bool RunFailed,
        string? RunError);
}
