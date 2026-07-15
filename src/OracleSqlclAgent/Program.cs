using Azure;
using Azure.AI.OpenAI;
using elbruno.Extensions.AI.Claude;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using OllamaSharp;
using OracleSqlclAgent;
using OracleSqlclAgent.Skills;
using Serilog;
using Spectre.Console;

// ── 1. Configuration ─────────────────────────────────────────────────────────

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json",
        optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

// ── 2. Serilog — error-only file logging ─────────────────────────────────────

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        path: Path.Combine("logs", "error-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{

// ── 3. Validate SQLcl path ────────────────────────────────────────────────────

var sqlclPath = configuration["SqlclMcp:Path"]
    ?? throw new InvalidOperationException(
        "Missing configuration: SqlclMcp:Path — update appsettings.json or user secrets " +
        "with the full path to sql.exe from the Oracle SQL Developer VS Code extension.");

if (!File.Exists(sqlclPath))
    throw new InvalidOperationException(
        $"SQLcl binary not found at: {sqlclPath}\n" +
        "Update SqlclMcp:Path in user secrets to the correct path.");

// ── 4. Start SQLcl MCP server ─────────────────────────────────────────────────

var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Command = sqlclPath,
    Arguments = ["-mcp"],
    Name = "sqlcl"
});

await using var mcpClient = await McpClient.CreateAsync(transport);

// ── 5. Enumerate MCP tools ────────────────────────────────────────────────────

var mcpTools = (await mcpClient.ListToolsAsync()).Cast<AITool>().ToList();

// ── 6. Register skills ────────────────────────────────────────────────────────

var skills = new OracleSqlclAgent.AgentSkillsProvider();
skills.Register(new OracleSqlQuerySkill());
skills.Register(new OracleTableSchemaSkill());
skills.Register(new OracleTableConstraintsSkill());
skills.Register(new OracleTableRelationshipsSkill());
skills.Register(new OracleDatabaseInfoSkill());

const int skillCount = 5;

// ── 7. Build IChatClient ──────────────────────────────────────────────────────

var provider = configuration["AI:Provider"] ?? "Claude";
IChatClient chatClient = BuildChatClient(configuration, provider);
var modelDisplay = GetModelDisplay(configuration, provider);

// ── 8. Build MAF agent ────────────────────────────────────────────────────────

var systemPrompt = $"""
    You are an Oracle database assistant. The database connection is hr_local.
    Always call the connect tool before running any query.
    Format query results as markdown tables. Use **bold** for key values.
    Do not list your internal skill names. Do not introduce yourself with a menu.
    Wait for the user's question and answer it directly.

    Available skills:
    {skills.BuildSkillsContext()}
    """;

AIAgent mafAgent = chatClient.AsAIAgent(
    name: "OracleAgent",
    instructions: systemPrompt,
    tools: [.. mcpTools]);

// ── 9. Startup banner ─────────────────────────────────────────────────────────

const int W = 45;
string L(string s) => $"│  {s.PadRight(W)}│";
string T(string s) => $"│    - {s.PadRight(W - 4)}│";
Console.WriteLine($"┌{new string('─', W + 2)}┐");
Console.WriteLine(L("OracleSqlclAgent (Microsoft Agent Framework)"));
Console.WriteLine(L($"Provider  : {provider}"));
Console.WriteLine(L($"Model     : {modelDisplay}"));
Console.WriteLine(L($"Tools ({mcpTools.Count})  :"));
foreach (var tool in mcpTools)
    Console.WriteLine(T(tool.Name ?? "(unnamed)"));
Console.WriteLine(L($"Skills ({skillCount}) :"));
Console.WriteLine(T("oracle-sql-query"));
Console.WriteLine(T("oracle-table-schema"));
Console.WriteLine(T("oracle-table-constraints"));
Console.WriteLine(T("oracle-table-relationships"));
Console.WriteLine(T("oracle-database-info"));
Console.WriteLine(L("Status    : READY"));
Console.WriteLine($"└{new string('─', W + 2)}┘");
Console.WriteLine();

// ── 10. UI style picker (2s timeout → Structured) ─────────────────────────────

var style = UiStyle.Structured;

AnsiConsole.MarkupLine("[bold]Select UI style:[/]");
AnsiConsole.MarkupLine("  [cyan][[1]][/] Structured - rules, spinners [grey](default)[/]");
AnsiConsole.MarkupLine("  [cyan][[2]][/] Minimal    - rule-separated turns");
AnsiConsole.MarkupLine("  [cyan][[3]][/] Panels     - bordered panel per message");
AnsiConsole.Markup("[grey]Choice [[1]]:[/] ");

try
{
    if (!Console.IsInputRedirected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && !Console.KeyAvailable)
            await Task.Delay(100);

        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            style = key.KeyChar switch
            {
                '2' => UiStyle.Minimal,
                '3' => UiStyle.Panels,
                _   => UiStyle.Structured
            };
        }
    }
}
catch (OperationCanceledException) { }

AnsiConsole.MarkupLine($"[green]{style}[/]\n");

// ── 11. Run agent ─────────────────────────────────────────────────────────────

await new OracleAgent(mafAgent, style).RunAsync();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception in OracleSqlclAgent");
    AnsiConsole.MarkupLine($"[red]Fatal error:[/] {ex.Message.EscapeMarkup()}");
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
        var model    = config["AI:Ollama:Model"]    ?? "llama3.2";
        var http     = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = Timeout.InfiniteTimeSpan };
        IChatClient ollama = (IChatClient)new OllamaApiClient(http, model, null!);

        // Inject num_ctx into every request via middleware if configured
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
        var aoaiEndpoint   = config["AI:AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException(
                "Missing AI:AzureOpenAI:Endpoint — set it in user secrets:\n" +
                "  dotnet user-secrets set \"AI:AzureOpenAI:Endpoint\" \"https://<resource>.cognitiveservices.azure.com/\"");
        var aoaiDeployment = config["AI:AzureOpenAI:DeploymentName"] ?? "gpt-5-mini";
        var aoaiApiKey     = config["AI:AzureOpenAI:ApiKey"]
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
    var claudeEndpoint   = config["AI:Claude:Endpoint"]
        ?? throw new InvalidOperationException(
            "Missing AI:Claude:Endpoint — set it in user secrets:\n" +
            "  dotnet user-secrets set \"AI:Claude:Endpoint\" \"https://<resource>.services.ai.azure.com/anthropic/v1/messages\"");
    var claudeDeployment = config["AI:Claude:DeploymentName"] ?? "claude-opus-4-6";
    var claudeApiKey     = config["AI:Claude:ApiKey"]
        ?? throw new InvalidOperationException(
            "Missing AI:Claude:ApiKey — set it in user secrets:\n" +
            "  dotnet user-secrets set \"AI:Claude:ApiKey\" \"<your-key>\"");

    return new AzureClaudeClient(
        endpoint:  new Uri(claudeEndpoint),
        modelId:   claudeDeployment,
        apiKey:    claudeApiKey);
}

static string GetModelDisplay(IConfiguration config, string provider) =>
    provider.ToLowerInvariant() switch
    {
        "ollama"      => config["AI:Ollama:Model"]              ?? "llama3.2",
        "azureopenai" => config["AI:AzureOpenAI:DeploymentName"] ?? "gpt-5-mini",
        _             => config["AI:Claude:DeploymentName"]      ?? "claude-opus-4-6"
    };
