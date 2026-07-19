# Talk to Oracle with an AI Agent — Part 4: C# Console Agent with Microsoft Agent Framework (MAF)

Parts 1–3 showed how Claude Code talks to Oracle through the SQLcl MCP server
using built-in skills. This part builds a standalone C# console application
that does the same thing — no Claude Code required.

You type a plain-English question. The agent queries your local Oracle HR
database through the SQLcl MCP server and answers you — with results rendered
as formatted tables right in the terminal.

**Series:** [Talk to Oracle with Claude AI](https://medium.com/scrum-and-coke/talk-to-oracle-with-claude-ai-series-preface-19b31fdb782e)

← [Part 3: Prompt Demos](#)

---

## What We're Building

A .NET 10 console app (`OracleSqlclAgent`) that:

1. Starts the SQLcl binary as an MCP subprocess
2. Discovers the database tools it exposes
3. Accepts natural language questions in a Spectre.Console TUI
4. Uses `Microsoft.Agents.AI`'s `AIAgent` to handle the agentic loop — tool calls, result feeding, and follow-up turns all managed automatically
5. Renders the response (tables, bold text, code blocks) using Markdig + Spectre.Console

The agent supports three AI backends via configuration: **Azure Claude** (via
Azure AI Foundry), **Azure OpenAI**, or **local Ollama** — switchable without
code changes.

---

## Prerequisites

- Docker Desktop with the Oracle HR container running (from Part 1)
- `hr_local` saved connection in SQLcl (from Part 1)
- .NET 10 SDK
- An Azure AI Foundry endpoint (Claude or Azure OpenAI) **or** a local Ollama installation

---

## Create the Project

```bash
mkdir OracleSqlclAgent
cd OracleSqlclAgent
dotnet new console --framework net10.0
```

Install the packages:

```bash
dotnet add package Microsoft.Agents.AI
dotnet add package Microsoft.Extensions.AI
dotnet add package Microsoft.Extensions.AI.OpenAI
dotnet add package Azure.AI.OpenAI
dotnet add package elbruno.Extensions.AI.Claude
dotnet add package ModelContextProtocol
dotnet add package OllamaSharp
dotnet add package Spectre.Console
dotnet add package Markdig
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.Configuration.UserSecrets
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet add package Serilog
dotnet add package Serilog.Sinks.File
```

**Key packages:**

| Package | Purpose |
|---|---|
| `Microsoft.Agents.AI` | `AIAgent` abstraction — manages the agentic tool loop |
| `Microsoft.Extensions.AI` | `IChatClient` common interface for all AI backends |
| `Azure.AI.OpenAI` | Azure OpenAI client |
| `elbruno.Extensions.AI.Claude` | Claude via Azure AI Foundry as `IChatClient` |
| `ModelContextProtocol` | MCP client — starts SQLcl as a subprocess |
| `OllamaSharp` | Local Ollama backend |

---

## Configuration

Create `appsettings.json`:

```json
{
  "AI": {
    "Provider": "AzureOpenAI",
    "AzureOpenAI": {
      "Endpoint": "https://<resource>.cognitiveservices.azure.com/",
      "DeploymentName": "gpt-4o-mini",
      "ApiKey": ""
    },
    "Claude": {
      "Endpoint": "https://<resource>.services.ai.azure.com/anthropic/v1/messages",
      "DeploymentName": "claude-opus-4-6",
      "ApiKey": ""
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
```

**To use Azure OpenAI (default):**

```bash
dotnet user-secrets set "AI:Provider" "AzureOpenAI"
dotnet user-secrets set "AI:AzureOpenAI:Endpoint" "https://<resource>.cognitiveservices.azure.com/"
dotnet user-secrets set "AI:AzureOpenAI:DeploymentName" "gpt-4o-mini"
dotnet user-secrets set "AI:AzureOpenAI:ApiKey" "<your-api-key>"
```

**To use Claude via Azure AI Foundry:**

```bash
dotnet user-secrets set "AI:Provider" "Claude"
dotnet user-secrets set "AI:Claude:Endpoint" "https://<resource>.services.ai.azure.com/anthropic/v1/messages"
dotnet user-secrets set "AI:Claude:DeploymentName" "claude-opus-4-6"
dotnet user-secrets set "AI:Claude:ApiKey" "<your-api-key>"
```

**To use local Ollama:**

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

`Microsoft.Extensions.AI` defines `IChatClient` as a common interface for all
AI backends. The `BuildChatClient` helper resolves the right backend from config:

```csharp
using Azure;
using Azure.AI.OpenAI;
using elbruno.Extensions.AI.Claude;
using Microsoft.Extensions.AI;
using OllamaSharp;

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
        var endpoint   = config["AI:AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("Missing AI:AzureOpenAI:Endpoint");
        var deployment = config["AI:AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";
        var apiKey     = config["AI:AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("Missing AI:AzureOpenAI:ApiKey");

        return new AzureOpenAIClient(
                new Uri(endpoint),
                new AzureKeyCredential(apiKey))
            .GetChatClient(deployment)
            .AsIChatClient();
    }

    // Default: Claude via Azure AI Foundry
    var claudeEndpoint   = config["AI:Claude:Endpoint"]
        ?? throw new InvalidOperationException("Missing AI:Claude:Endpoint");
    var claudeDeployment = config["AI:Claude:DeploymentName"] ?? "claude-opus-4-6";
    var claudeApiKey     = config["AI:Claude:ApiKey"]
        ?? throw new InvalidOperationException("Missing AI:Claude:ApiKey");

    return new AzureClaudeClient(
        endpoint:  new Uri(claudeEndpoint),
        modelId:   claudeDeployment,
        apiKey:    claudeApiKey);
}
```

The rest of the agent code never references a specific AI vendor — it only
calls `IChatClient`.

> 💡 **Ollama context window:** For local models, `num_ctx` is injected via
> `IChatClient` middleware so every request automatically carries the configured
> context window size. This is configured in user secrets with
> `AI:Ollama:NumCtx`.

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
        "Run any SQL query against Oracle — SELECT, aggregations, data analysis, custom queries.";

    protected override string Instructions => """
        ## Oracle SQL Query

        Use this skill when the user asks to query data, count records, calculate averages,
        or run any custom SQL against the Oracle HR schema.

        Steps:
        1. Call `connect` with connection_name = "hr_local"
        2. Build the appropriate SQL based on the user's request
        3. Call `sql_run` with the SQL
        4. Format the results in plain text — no markdown tables
        5. Call `disconnect` when done
        """;
}

public sealed class OracleTableSchemaSkill : AgentClassSkill
{
    public override string Name => "oracle-table-schema";
    public override string Description =>
        "Describe Oracle table structure — columns, data types, nullability.";

    protected override string Instructions => """
        ## Oracle Table Schema

        Use when the user asks about a table's structure or columns.

        Steps:
        1. Call `connect` with connection_name = "hr_local"
        2. Run:
           SELECT column_name, data_type, data_length, nullable
           FROM user_tab_columns
           WHERE table_name = UPPER('<table>')
           ORDER BY column_id
        3. Present each column with its type, length, and nullability (Y = optional, N = required)
        4. Call `disconnect` when done
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

## Build the MAF Agent

`Microsoft.Agents.AI` provides the `AIAgent` class, which wraps an
`IChatClient` and handles the full agentic tool loop automatically — no
manual function-call plumbing required.

Construct the agent after building the `IChatClient` and loading the MCP tools:

```csharp
using Microsoft.Agents.AI;

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
```

`chatClient.AsAIAgent(...)` is an extension method from `Microsoft.Agents.AI`
that wraps the `IChatClient` with session management and the tool-call loop.

---

## The Agentic Loop

With `Microsoft.Agents.AI`, the tool loop is fully managed by the framework.
`OracleAgent` opens a session once and calls `agent.RunAsync()` per turn:

```csharp
public sealed class OracleAgent(AIAgent agent, UiStyle style = UiStyle.Structured)
{
    private AgentSession? _session;

    private async Task<string> RunAgentAsync(string input, CancellationToken ct)
    {
        // Session is created once and reused across turns
        _session ??= await agent.CreateSessionAsync(ct);

        var response = await Spin("Thinking…", _ =>
            agent.RunAsync(input, _session, null, ct));

        return response.Text ?? string.Empty;
    }
}
```

Internally, `agent.RunAsync()` handles the full cycle for each user turn:

1. Sends the user message to the model with tool definitions attached
2. If the model requests tool calls, executes them (MCP tools: `connect`, `sql_run`, etc.)
3. Feeds the tool results back to the model
4. Repeats until the model returns a final text response

No manual `FunctionCallContent` parsing, no `ChatRole.Tool` message construction,
no loop management — the framework owns all of that.

The session (`AgentSession`) carries conversation history across turns, so
context accumulates naturally as you ask follow-up questions.

---

## Terminal UI with Spectre.Console

The agent uses Spectre.Console for a polished terminal experience:

```csharp
public enum UiStyle { Structured, Minimal, Panels }
```

A Spectre spinner shows while the model is thinking:

```csharp
private static Task<T> Spin<T>(string status, Func<StatusContext, Task<T>> action) =>
    AnsiConsole.Status()
        .Spinner(Spectre.Console.Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("blue"))
        .StartAsync($"[blue]{status}[/]", action);
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
┌───────────────────────────────────────────────────────┐
│  OracleSqlclAgent (Microsoft Agent Framework)         │
│  Provider  : AzureOpenAI                              │
│  Model     : gpt-4o-mini                              │
│  Tools (7)  :                                         │
│    - connections_list                                 │
│    - connect                                          │
│    - disconnect                                       │
│    - sql_run                                          │
│    - sqlcl_run                                        │
│    - schema_information                               │
│    - request_status                                   │
│  Skills (5) :                                         │
│    - oracle-sql-query                                 │
│    - oracle-table-schema                              │
│    - oracle-table-constraints                         │
│    - oracle-table-relationships                       │
│    - oracle-database-info                             │
│  Status    : READY                                    │
└───────────────────────────────────────────────────────┘
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
what's available. Because the model already has the `AIAgent` and MCP tools
in hand, it selects the right behavior from context.

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

## Extending the Agent

To point the agent at a different database, save a new named connection in
SQLcl and update the `connection_name` reference in the skill instructions.
No code changes needed for the agent itself.

To switch AI backends, update `AI:Provider` in user secrets:

```bash
# Switch to local Ollama
dotnet user-secrets set "AI:Provider" "Ollama"
dotnet user-secrets set "AI:Ollama:Model" "gemma4:latest"
dotnet user-secrets set "AI:Ollama:NumCtx" "32768"

# Switch to Claude via Azure AI Foundry
dotnet user-secrets set "AI:Provider" "Claude"
dotnet user-secrets set "AI:Claude:Endpoint" "https://<resource>.services.ai.azure.com/anthropic/v1/messages"
dotnet user-secrets set "AI:Claude:DeploymentName" "claude-opus-4-6"
dotnet user-secrets set "AI:Claude:ApiKey" "<your-key>"
```

---

## Wrapping Up

This series showed how to build a full AI-to-Oracle pipeline from the ground up:

1. **[Part 1](part-1-setup.md)** — Install SQLcl via the Oracle SQL Developer VS Code extension and spin up a local Oracle HR schema with Docker
2. **[Part 2](part-2-mcp-skills.md)** — Wire Claude Code to the SQLcl MCP server and load Oracle skills for plain-English queries
3. **[Part 3](part-3-prompt-demos.md)** — Run real prompts against the live HR schema: schema exploration, constraint inspection, FK maps, and CSV export
4. **[Part 4](part-4-maf-agent.md)** — Build a standalone C# agent with `Microsoft.Agents.AI` that does everything Claude Code does — deployable anywhere, no IDE required

The SQLcl MCP server is the stable common layer across all four parts. Swap
the Docker connection for your dev, staging, or prod instance and the same
tools, skills, and agent work unchanged. No config rewrites, no code changes —
just update the saved connection in SQLcl.

The full source — Docker setup, `.mcp.json`, Claude skills, and this C#
agent — is available at:
[github.com/workcontrolgit/oracle-sqlcl-ai-skills](https://github.com/workcontrolgit/oracle-sqlcl-ai-skills)
