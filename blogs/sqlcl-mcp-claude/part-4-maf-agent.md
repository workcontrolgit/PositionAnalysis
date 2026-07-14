# Talk to Oracle with an AI Agent — Part 4: C# Console Agent with Microsoft.Extensions.AI

Parts 1–3 showed how Claude Code talks to Oracle through the SQLcl MCP server
using built-in skills. This part builds a standalone C# console application
that does the same thing — no Claude Code required.

You type a plain-English question. The agent queries your local Oracle HR
database through the SQLcl MCP server and answers you — with results rendered
as formatted tables right in the terminal.

**Series:**
- Part 1: Setup — VS Code extension, Docker HR schema
- Part 2: MCP Server Configuration + Claude Skills
- Part 3: Prompt Demos — Querying Oracle in Plain English
- Part 4: C# Console Agent ← you are here

← [Part 3: Prompt Demos](#)

---

## What We're Building

A .NET 10 console app (`OracleSqlclAgent`) that:

1. Starts the SQLcl binary as an MCP subprocess
2. Discovers the database tools it exposes
3. Accepts natural language questions in a Spectre.Console TUI
4. Runs an agentic loop — calling tools, feeding results back, looping until done
5. Renders the response (tables, bold text, code blocks) using Markdig + Spectre.Console

The agent supports two AI backends via configuration: **Anthropic Claude** or
**local Ollama** — switchable without code changes.

---

## Prerequisites

- Docker Desktop with the Oracle HR container running (from Part 1)
- `hr_local` saved connection in SQLcl (from Part 1)
- .NET 10 SDK
- An Anthropic API key **or** a local Ollama installation

---

## Create the Project

```bash
mkdir OracleSqlclAgent
cd OracleSqlclAgent
dotnet new console --framework net10.0
```

Install the packages:

```bash
dotnet add package Anthropic
dotnet add package ModelContextProtocol
dotnet add package Microsoft.Extensions.AI
dotnet add package OllamaSharp
dotnet add package Spectre.Console
dotnet add package Markdig
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.Configuration.UserSecrets
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet add package Serilog
dotnet add package Serilog.Sinks.File
```

> **Note:** There is no `Microsoft.AgentFramework` NuGet package. The agent
> abstraction in this project is built directly on `Microsoft.Extensions.AI`,
> which provides the `IChatClient` interface that both the Anthropic SDK and
> OllamaSharp implement.

---

## Configuration

Create `appsettings.json`:

```json
{
  "AI": {
    "Provider": "Anthropic",
    "Anthropic": {
      "Model": "claude-opus-4-6"
    },
    "Ollama": {
      "Endpoint": "http://localhost:11434",
      "Model": "llama3.2"
    }
  },
  "SqlclMcp": {
    "Path": "C:\\Users\\<you>\\.vscode\\extensions\\oracle.sql-developer-<version>-win32-x64\\dbtools\\sqlcl\\bin\\sql.exe"
  }
}
```

Use .NET user secrets for machine-specific values so credentials and paths
stay out of source control:

```bash
dotnet user-secrets init
dotnet user-secrets set "SqlclMcp:Path" "C:\Users\<you>\.vscode\extensions\oracle.sql-developer-26.2.0-win32-x64\dbtools\sqlcl\bin\sql.exe"
dotnet user-secrets set "AI:Provider" "Anthropic"
dotnet user-secrets set "AI:Anthropic:ApiKey" "<your-api-key>"
```

To use local Ollama instead:

```bash
dotnet user-secrets set "AI:Provider" "Ollama"
dotnet user-secrets set "AI:Ollama:Model" "gemma4:latest"
dotnet user-secrets set "AI:Ollama:NumCtx" "32768"
```

---

## Connect to the SQLcl MCP Server

`ModelContextProtocol` starts the SQLcl binary as a subprocess and exposes its
tools over stdio:

```csharp
using ModelContextProtocol.Client;

var sqlclPath = configuration["SqlclMcp:Path"]
    ?? throw new InvalidOperationException("Missing SqlclMcp:Path");

var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Command = sqlclPath,
    Arguments = ["-mcp"],
    Name = "sqlcl"
});

await using var mcpClient = await McpClient.CreateAsync(transport);

// Enumerate the tools SQLcl exposes
var mcpTools = (await mcpClient.ListToolsAsync()).Cast<AITool>().ToList();
```

When this runs, `mcpTools` contains the same tools Claude Code uses:

```
connections_list, connect, disconnect, sql_run, sqlcl_run,
schema_information, request_status
```

---

## Build the IChatClient

`Microsoft.Extensions.AI` defines `IChatClient` as a common interface for any
AI backend. Both `Anthropic` and `OllamaSharp` implement it:

```csharp
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;

static IChatClient BuildChatClient(IConfiguration config, string provider)
{
    if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
    {
        var endpoint = config["AI:Ollama:Endpoint"] ?? "http://localhost:11434";
        var model    = config["AI:Ollama:Model"]    ?? "llama3.2";
        var http     = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = Timeout.InfiniteTimeSpan };
        return (IChatClient)new OllamaApiClient(http, model, null!);
    }

    // Anthropic — uses ANTHROPIC_API_KEY env var if no key in config
    var apiKey = config["AI:Anthropic:ApiKey"];
    var claude = string.IsNullOrEmpty(apiKey)
        ? new AnthropicClient()
        : new AnthropicClient() { ApiKey = apiKey };
    var claudeModel = config["AI:Anthropic:Model"] ?? "claude-opus-4-6";
    return claude.AsIChatClient(claudeModel);
}
```

The rest of the agent code never references Anthropic or Ollama directly —
it only calls `IChatClient`.

---

## Define Oracle Skills as Classes

Skills are local abstract base classes — modular prompt packages that give the
agent focused expertise for specific task types:

```csharp
// Base class — defined locally (no NuGet package required)
public abstract class AgentClassSkill
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    protected abstract string Instructions { get; }
    public string GetInstructions() => Instructions;
}
```

Each Oracle skill is a sealed class:

```csharp
public sealed class OracleSqlQuerySkill : AgentClassSkill
{
    public override string Name => "oracle-sql-query";
    public override string Description =>
        "To run general SQL queries against the Oracle HR database";

    protected override string Instructions => """
        ## Oracle SQL Query

        Use this skill when the user asks to query Oracle data.

        Steps:
        1. Call connect with connection_name = "hr_local"
        2. Build the appropriate SQL based on the user's request
        3. Call sql_run with the SQL
        4. Format results as a markdown table
        5. Call disconnect when done
        """;
}

public sealed class OracleTableSchemaSkill : AgentClassSkill
{
    public override string Name => "oracle-table-schema";
    public override string Description =>
        "To describe Oracle table structure — columns, data types, nullability";

    protected override string Instructions => """
        ## Oracle Table Schema

        Use when the user asks about a table's structure or columns.

        Steps:
        1. Call connect with connection_name = "hr_local"
        2. Run: SELECT column_name, data_type, nullable, data_length
                 FROM user_tab_columns
                 WHERE table_name = UPPER('<table>')
                 ORDER BY column_id
        3. Present columns with type and nullability as a markdown table
        4. Call disconnect when done
        """;
}
```

Register all five skills with a provider:

```csharp
public sealed class AgentSkillsProvider
{
    private readonly List<AgentClassSkill> _skills = new();

    public void Register(AgentClassSkill skill) => _skills.Add(skill);

    public string BuildSkillsContext() =>
        string.Join("\n", _skills.Select(s => $"- {s.Name}: {s.Description}"));
}

var skills = new AgentSkillsProvider();
skills.Register(new OracleSqlQuerySkill());
skills.Register(new OracleTableSchemaSkill());
skills.Register(new OracleTableConstraintsSkill());
skills.Register(new OracleTableRelationshipsSkill());
skills.Register(new OracleDatabaseInfoSkill());
```

---

## The Agentic Loop

The agent loop is manual — call the model, check for tool requests, execute
them, feed results back, repeat until the model returns a final answer:

```csharp
private async Task<string> RunToolLoopAsync(CancellationToken ct)
{
    var options = new ChatOptions { Tools = tools };

    var response = await chatClient.GetResponseAsync(_history, options, ct);

    while (true)
    {
        var toolCalls = response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();

        // No more tool calls — the model is done
        if (toolCalls.Count == 0)
        {
            _history.AddMessages(response);
            return response.Text ?? string.Empty;
        }

        // Append assistant's tool-call messages to history
        foreach (var msg in response.Messages)
            _history.Add(msg);

        // Execute each tool and append the result
        foreach (var call in toolCalls)
        {
            var fn = tools.FirstOrDefault(t => t.Name == call.Name) as AIFunction;
            object? rawResult = fn is null
                ? $"Tool '{call.Name}' not found."
                : await fn.InvokeAsync(new AIFunctionArguments(call.Arguments), ct);

            _history.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent(call.CallId ?? string.Empty, rawResult)]));
        }

        // Ask the model again with tool results in history
        response = await chatClient.GetResponseAsync(_history, options, ct);
    }
}
```

The MCP tools (`connect`, `sql_run`, `disconnect`, etc.) are the same `AITool`
objects as before — the loop treats them identically to any other tool.

For Ollama, pass `num_ctx` via `AdditionalProperties` so the context window
is respected:

```csharp
var additional = new AdditionalPropertiesDictionary();
if (numCtx.HasValue) additional["num_ctx"] = numCtx.Value;
var options = new ChatOptions { Tools = tools, AdditionalProperties = additional };
```

---

## Terminal UI with Spectre.Console

The agent uses Spectre.Console for a polished terminal experience:

```csharp
public enum UiStyle { Structured, Minimal, Panels }
```

A Spectre spinner shows while the model is thinking:

```csharp
var response = await AnsiConsole.Status()
    .Spinner(Spectre.Console.Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("blue"))
    .StartAsync("[blue]Thinking…[/]", _ =>
        chatClient.GetResponseAsync(_history, options, ct));
```

The three UI styles differ in how the prompt and response are framed:

```
Structured — horizontal rules, yellow "You ›" prompt
Minimal    — rule-separated turns, same aesthetics
Panels     — bordered panel per message
```

At startup, a 2-second timeout lets you pick a style; it defaults to
Structured if you don't press a key.

---

## Markdown Rendering with Markdig

Model responses contain markdown (`**bold**`, tables, code blocks). Printing
them raw shows the syntax literally. Markdig parses the markdown AST and
Spectre.Console renders each block as a widget:

```csharp
// Instead of AnsiConsole.MarkupLine(text)
MarkdigSpectreRenderer.Render(text);
```

The renderer walks the Markdig AST and outputs native Spectre.Console widgets:

| Markdown element | Spectre.Console widget |
|-----------------|----------------------|
| Heading | `Rule` with styled text |
| Paragraph | `MarkupLine` with inline formatting |
| Bullet/ordered list | Indented `MarkupLine` with bullet |
| Fenced code block | `Panel` with grey border |
| Pipe table | `Table` with teal border |
| **Bold**, *italic* | `[bold]` / `[italic]` markup |

Tables use `Markup.Escape` on all cell content to prevent Spectre markup
errors from model output that happens to contain bracket characters.

---

## Error Logging with Serilog

The agent logs errors to a rolling file — nothing goes to the console:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        path: Path.Combine("logs", "error-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error)
    .CreateLogger();
```

Unhandled exceptions are caught at the top level, logged to file, and shown
as a friendly Spectre.Console error message.

---

## Startup Banner

When the app starts, it prints a box showing what loaded:

```
┌───────────────────────────────────────────────────┐
│  OracleSqlclAgent                                 │
│  Provider  : Anthropic                            │
│  Model     : claude-opus-4-6                      │
│  Tools (7)  :                                     │
│    - connections_list                             │
│    - connect                                      │
│    - disconnect                                   │
│    - sql_run                                      │
│    - sqlcl_run                                    │
│    - schema_information                           │
│    - request_status                               │
│  Skills (5) :                                     │
│    - oracle-sql-query                             │
│    - oracle-table-schema                          │
│    - oracle-table-constraints                     │
│    - oracle-table-relationships                   │
│    - oracle-database-info                         │
│  Status    : READY                                │
└───────────────────────────────────────────────────┘
```

---

## Example Session

```
Select UI style:
  [1] Structured - rules, spinners (default)
  [2] Minimal    - rule-separated turns
  [3] Panels     - bordered panel per message
Choice [1]: 1
Structured

──── Oracle Assistant ────────────────────────────────
Ask a database question. Type exit to quit.

You › what is the average salary by department?

● Thinking…

Assistant ›

| Department        | Avg Salary |
|-------------------|------------|
| Administration    |   4,400.00 |
| Executive         |  19,333.33 |
| Finance           |   8,600.00 |
| Human Resources   |   6,500.00 |
| IT                |   5,760.00 |
| Marketing         |   9,500.00 |
| Purchasing        |   4,150.00 |
| Sales             |   8,955.88 |
| Shipping          |   3,475.56 |

─────────────────────────────────────────────────────

You › exit
```

---

## Example Prompts

Try these against the running agent:

```
show me all employees in department 60
what is the average salary by department?
who are the managers?
describe the structure of the jobs table
map the foreign key relationships in the schema
what oracle version is this?
show me job history for employee 101
```

---

## How Skills Compare: This Agent vs Claude Code

Both use skills as modular prompt packages that give the agent focused
expertise. The mechanisms are parallel:

**Claude Code skills** live in `.claude/skills/<name>/SKILL.md` and are
invoked with `/skill-name` in the chat. Claude Code advertises available
skills, loads them on demand, and injects them into context.

**This agent's skills** are C# classes registered with `AgentSkillsProvider`.
The system prompt includes skill names and descriptions so the model knows
what's available. Because the model already has the `IChatClient` and
`AITool` list in hand, it selects the right behavior from context.

The key difference: Claude Code skills run inside Claude Code's harness.
This agent runs in your own C# application — you own the agent, the
deployment, and the runtime.

---

## Running the Agent

```bash
cd src/OracleSqlclAgent
dotnet run
```

Make sure the Oracle Docker container is running and `hr_local` is saved in
SQLcl before starting.

---

## What's Next

To point the agent at a different database, save a new named connection in
SQLcl and update the `connection_name` reference in the skill instructions.
No code changes needed for the agent itself.

To switch from Anthropic to a local model:

```bash
dotnet user-secrets set "AI:Provider" "Ollama"
dotnet user-secrets set "AI:Ollama:Model" "gemma4:latest"
dotnet user-secrets set "AI:Ollama:NumCtx" "32768"
```

The full source — Docker setup, `.mcp.json`, Claude skills, and this C#
agent — is available at:
[github.com/workcontrolgit/oracle-sqlcl-ai-skills](https://github.com/workcontrolgit/oracle-sqlcl-ai-skills)
