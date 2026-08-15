# Schedule PC Chat Client

Interactive AI-powered chat client for Schedule Policy/Career position evaluation.

## Features

- **Natural Language Input**: Ask for evaluations in plain English
- **AI Parameter Parsing**: Uses Azure OpenAI to understand filter intent
- **Oracle Integration**: Queries Schedule PC data via MCP
- **Interactive CLI**: Conversational loop with command support
- **Flexible Filtering**: By series, PD number, org code, or grade

## Example Usage

```
Schedule PC> evaluate series 0301
Schedule PC> run by series 0301 and 0905  
Schedule PC> score pd D01880
Schedule PC> find grade 15 positions
Schedule PC> show all positions in org EXEC-POL
Schedule PC> query grade 13-15
```

## Setup

### Prerequisites

- .NET 8.0 or later
- Azure OpenAI credentials
- Oracle MCP server running (optional - works in AI-only mode if unavailable)

### Installation

1. **Install NuGet Packages**:
   ```powershell
   dotnet restore
   ```

2. **Configure Azure OpenAI**:
   Set environment variables:
   ```powershell
   $env:AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com/"
   $env:AZURE_OPENAI_API_KEY = "your-api-key"
   ```

3. **Build**:
   ```powershell
   dotnet build
   ```

4. **Run**:
   ```powershell
   dotnet run
   ```

## Unattended Scoring

Run the existing Schedule PC scoring queue without starting the interactive chat client:

```powershell
dotnet run -- --unattended
```

This starts the SchedulePCMcp child process, triggers scoring for all currently queued PDs, and polls the aggregate queue until no work remains. It does not stage PDs, generate documents, or export results.

The command exits with code `0` when the queue drains with no failed PDs, `1` when the queue drains with one or more failed PDs, or `2` when the worker is cancelled before normal terminal completion.

## Commands

| Command | Example | Purpose |
|---------|---------|---------|
| `series <code>` | `series 0301` | Query by occupational series |
| `pd <number>` | `pd D01880` | Query specific position |
| `grade <value>` | `grade 15` | Query by grade |
| `org <code>` | `org EXEC-POL` | Query by organization |
| Natural language | `evaluate series 0301` | Use AI to parse intent |
| `help` | `help` | Show command reference |
| `exit` / `quit` | `exit` | Exit application |

## Architecture

```
┌─────────────────────────────────────┐
│  SchedulePCChatClient (Main Loop)  │
│  - Reads user input                │
│  - Manages Oracle MCP connection   │
├─────────────────────────────────────┤
│  Azure OpenAI Parser               │
│  - Converts natural language       │
│  - Extracts filter parameters      │
├─────────────────────────────────────┤
│  WHERE Clause Builder              │
│  - Constructs SQL filters          │
│  - Handles series/pd/org/grade     │
├─────────────────────────────────────┤
│  Oracle MCP Client                 │
│  - Executes SQL queries            │
│  - Returns position data           │
└─────────────────────────────────────┘
```

## Oracle Query Template

The chat client executes queries against `MAX_PD_VW` with dynamic WHERE clauses:

```sql
SELECT v.PD_NBR, 
       v.PD_POSITION_TITLE_TEXT AS TITLE,
       v.GVT_OCC_SERIES AS SERIES,
       v.GRD_CODE AS GRADE,
       v.GVT_PAY_PLAN,
       v.PD_ORIGIN_ORG_CODE AS ORG_CODE
FROM MAX_PD_VW v
WHERE v.PD_ARCHIVE_IND != 'Y'
  AND v.GVT_PAY_PLAN IN ('GS', 'ES', 'SL', 'ST', 'AD')
  AND (REGEXP_LIKE(v.GRD_CODE, '^1[3-5]$') OR v.GVT_PAY_PLAN IN ('ES', 'SL', 'ST'))
{DYNAMIC_WHERE_CLAUSE}
```

## Fallback Mode

If Oracle MCP is unavailable, the client displays sample results and continues in AI-only mode.

## Environment Variables

| Variable | Default | Required |
|----------|---------|----------|
| `AZURE_OPENAI_ENDPOINT` | `https://your-resource.openai.azure.com/` | Yes |
| `AZURE_OPENAI_API_KEY` | `YOUR_API_KEY` | Yes |

## Next Steps

- Add scoring workflow integration (`schedule-pc-eval-score`)
- Store conversation history for audit trail
- Add batch evaluation mode
- Implement result export to Excel
- Add multi-language support

## Authority

EO "Implementing Schedule Policy/Career in the Excepted Service" (June 3, 2026)  
Legal Basis: 5 U.S.C. § 7511(b)(2); 5 U.S.C. § 3302
