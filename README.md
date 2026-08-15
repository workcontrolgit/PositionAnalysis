# Position Analysis

AI-assisted evaluation of federal Position Descriptions (PDs) against Schedule
Policy/Career (Schedule P/C) criteria under Executive Order 13957. Position Analysis stages
PDs from Oracle, scores them with an LLM against four Schedule P/C criteria, derives a
HIGH/MEDIUM/LOW rating from the number of criteria triggered, and generates Word evaluation
forms and Excel exports for HR review.

## Architecture

```
PositionAnalysis.Cli  →  PositionAnalysis.Mcp  →  Oracle (SCHEDULE_PC_EVAL, MAX_PD_VW)
  (interactive chat            (MCP server,              →  Azure OpenAI (scoring)
   or --unattended)          spawned as a child
                               process over stdio)
```

- **`PositionAnalysis.Cli`** — the executable users run, either interactively (natural
  language chat) or unattended (`--unattended`, for scheduled scoring runs). It always
  launches `PositionAnalysis.Mcp` as a child process and talks to it via MCP tool calls; you
  never run `PositionAnalysis.Mcp` directly.
- **`PositionAnalysis.Mcp`** — the MCP server. Owns all Oracle access, LLM scoring calls,
  Word document generation, and Excel export.

## Getting Started

See the instruction docs for step-by-step usage:

- [How to Run Position Analysis in Interactive Chat Mode](docs/instructions/how-to-positionanalysis-interactive-chat.md)
  — staging, scoring, status, document generation, and export, all via natural language.
- [How to Run Position Analysis Scoring Workers (Unattended)](docs/instructions/how-to-positionanalysis-unattended-workers.md)
  — publishing a self-contained build and scheduling unattended scoring runs across one or
  more Windows servers.
- [How to Stage/Clear the Schedule P/C Eval Report](docs/instructions/how-to-positionanalysis-stage-clear-report.md)

Background on the scoring/rating methodology is in `docs/schedulepc/`:

- [Scoring Criteria](docs/schedulepc/schedule-pc-scoring-criteria.md)
- [Rating and AI-Score Color Coding](docs/schedulepc/schedule-pc-rating-and-ai-score-color-coding.md)
- [Temp Tables and Human Baseline](docs/schedulepc/schedule-pc-temp-tables-and-human-baseline.md)
- [Word Template Field Traceability](docs/schedulepc/word-template-field-traceability.md)
- [Cost Estimate](docs/schedulepc/schedule-pc-cost-estimate.md)

## Prerequisites

- .NET 10 SDK
- Oracle database access (schema `ACRS`, table `SCHEDULE_PC_EVAL`)
- Azure OpenAI API key and endpoint

## Building

```powershell
dotnet build src/PositionAnalysis.slnx
```

To run the interactive chat client from source:

```powershell
cd src/PositionAnalysis.Cli
dotnet run
```

To publish a self-contained build for a server (see the unattended-workers doc for the full
deployment procedure):

```powershell
dotnet publish src/PositionAnalysis.Cli/PositionAnalysis.Cli.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -o D:\deploy\PositionAnalysis
```

## Configuration

Each project has its own `appsettings.json` since they run as separate processes:

- [`src/PositionAnalysis.Cli/appsettings.json`](src/PositionAnalysis.Cli/appsettings.json) —
  AI provider for the interactive chat client (`AI:*`), plus its own logging.
- [`src/PositionAnalysis.Mcp/appsettings.json`](src/PositionAnalysis.Mcp/appsettings.json) —
  Oracle connection, the scoring LLM provider (`AiProvider:*`), storage, document generation,
  and Excel export settings.

> **Keep Azure OpenAI credentials in sync manually.** `AI:AzureOpenAI` (Cli) and
> `AiProvider:AzureOpenAI` (Mcp) are separate config sections used by separate processes for
> separate purposes (interactive chat parsing vs. actual PD scoring) — they are **not** merged
> automatically. If you rotate the Azure OpenAI API key or endpoint, update both files (or both
> sets of environment variables) or the two processes will drift out of sync.

### Setting secrets (development)

Use `dotnet user-secrets` — run these once per project, not per-machine:

**PositionAnalysis.Mcp** (Oracle + scoring LLM):

```powershell
cd src/PositionAnalysis.Mcp

# Oracle — ODP.NET format (NOT SQLcl/SQL*Plus "user/pass@//host" syntax)
dotnet user-secrets set "Oracle:ConnectionString" "User Id=<user>;Password=<pass>;Data Source=//<host>:<port>/<service>"

# Azure OpenAI scoring LLM
dotnet user-secrets set "AiProvider:AzureOpenAI:Endpoint"       "https://<resource>.cognitiveservices.azure.com/"
dotnet user-secrets set "AiProvider:AzureOpenAI:DeploymentName" "<deployment>"
dotnet user-secrets set "AiProvider:AzureOpenAI:ApiKey"         "<key>"
```

**PositionAnalysis.Cli** (interactive-chat LLM):

```powershell
cd src/PositionAnalysis.Cli

dotnet user-secrets set "AI:AzureOpenAI:Endpoint"       "https://<resource>.cognitiveservices.azure.com/"
dotnet user-secrets set "AI:AzureOpenAI:DeploymentName" "<deployment>"
dotnet user-secrets set "AI:AzureOpenAI:ApiKey"         "<key>"
```

> **Oracle connection string format:** `Oracle.ManagedDataAccess.Core` (ODP.NET) requires the
> key-value format above. The SQLcl/SQL*Plus shorthand `user/pass@//host:port/service` is **not**
> accepted and will throw `ORA-50007: Connection string is not well-formed`.

### Temperature (`AiProvider:AzureOpenAI:Temperature`)

Temperature controls how deterministic the LLM's scoring responses are.

**For PD evaluation, set temperature to `0` or `0.2`.** Lower values produce more consistent,
reproducible ratings — critical for fair and auditable Schedule P/C scoring. Higher values
introduce randomness that can cause the same PD to receive different ratings across runs.

`appsettings.json` defaults to `0.2`. If your Azure OpenAI deployment only accepts the model
default (`1.0`) — which some newer models enforce — override it via user secret:

```powershell
# Override to model default (use when the model rejects non-default temperature)
dotnet user-secrets set "AiProvider:AzureOpenAI:Temperature" "1"
```

When the temperature is set to `1.0`, the application omits the parameter from the API request
entirely (sending it explicitly would still trigger a `BadRequest` on restricted models).
For all other values the parameter is sent as configured.

Secrets should be supplied via user secrets (dev) or machine-level environment variables
(server), never committed to `appsettings.json` — see the unattended-workers doc for the
`Set-PositionAnalysisEnv.ps1` script used on servers.

## Project Structure

```
src/
  PositionAnalysis.slnx
  PositionAnalysis.Cli/            Interactive/unattended console client
  PositionAnalysis.Mcp/            MCP server: Oracle, scoring, documents, export
  PositionAnalysis.Cli.Tests/
  PositionAnalysis.Mcp.Tests/
docs/
  instructions/                    End-user how-to guides
  schedulepc/                      Scoring/rating methodology reference docs
scripts/                           Server deployment PowerShell scripts
```
