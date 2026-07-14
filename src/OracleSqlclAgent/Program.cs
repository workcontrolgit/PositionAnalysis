using Anthropic;
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
        "Missing configuration: SqlclMcp:Path — update appsettings.json or appsettings.Development.json " +
        "with the full path to sql.exe from the Oracle SQL Developer VS Code extension.");

if (!File.Exists(sqlclPath))
    throw new InvalidOperationException(
        $"SQLcl binary not found at: {sqlclPath}\n" +
        "Update SqlclMcp:Path in appsettings.Development.json to the correct path.");

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

var skills = new AgentSkillsProvider();
skills.Register(new OracleSqlQuerySkill());
skills.Register(new OracleTableSchemaSkill());
skills.Register(new OracleTableConstraintsSkill());
skills.Register(new OracleTableRelationshipsSkill());
skills.Register(new OracleDatabaseInfoSkill());

const int skillCount = 5;

// ── 7. Build IChatClient ──────────────────────────────────────────────────────

var provider = configuration["AI:Provider"] ?? "Anthropic";
IChatClient chatClient = BuildChatClient(configuration, provider);
var modelDisplay = GetModelDisplay(configuration, provider);

// ── 8. Startup banner ─────────────────────────────────────────────────────────

const int W = 45;
string L(string s) => $"│  {s.PadRight(W)}│";
string T(string s) => $"│    - {s.PadRight(W - 4)}│";
Console.WriteLine($"┌{new string('─', W + 2)}┐");
Console.WriteLine(L("OracleSqlclAgent"));
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

// ── 9. UI style picker (2s timeout → Structured) ──────────────────────────────

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

// ── 10. Run agent ─────────────────────────────────────────────────────────────

var numCtxRaw = configuration["AI:Ollama:NumCtx"];
var numCtx = int.TryParse(numCtxRaw, out var n) ? (int?)n : null;
await new OracleAgent(chatClient, mcpTools, skills, style, numCtx).RunAsync();

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
        return (IChatClient)new OllamaApiClient(http, model, null!);
    }

    // Default: Anthropic
    var apiKey = config["AI:Anthropic:ApiKey"];
    var claude = string.IsNullOrEmpty(apiKey)
        ? new AnthropicClient()                         // uses ANTHROPIC_API_KEY env var
        : new AnthropicClient() { ApiKey = apiKey };
    var claudeModel = config["AI:Anthropic:Model"] ?? "claude-opus-4-6";
    return claude.AsIChatClient(claudeModel);
}

static string GetModelDisplay(IConfiguration config, string provider) =>
    string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase)
        ? config["AI:Ollama:Model"] ?? "llama3.2"
        : config["AI:Anthropic:Model"] ?? "claude-opus-4-6";
