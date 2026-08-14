# How to Run Position Analysis Scoring Workers (Unattended)

Run the Position Analysis Schedule PC scoring queue unattended (`--process-all`) on a
schedule, across one or more machines, using Windows Task Scheduler.

`PositionAnalysis.Cli` is the executable users run. It launches `PositionAnalysis.Mcp` as a
child process over stdio and talks to it through MCP tool calls — you never run
`PositionAnalysis.Mcp` directly.

For interactive/console chat mode instead, see
[how-to-positionanalysis-interactive-chat.md](how-to-positionanalysis-interactive-chat.md).

## What `--process-all` does

`PositionAnalysis.Cli.exe --process-all` starts the `PositionAnalysis.Mcp` child process,
triggers the `process_all_pds` MCP tool, then polls `get_queue_status` until the queue is
drained. It scores only — it does not stage PDs, generate documents, or export results.

## Prerequisites

- Windows Server with PowerShell 5.1+
- .NET 10 runtime **or** use the self-contained publish (recommended — no runtime install needed)
- Oracle DB access from the server
- Azure OpenAI API key and endpoint

---

## Step 1 — Deploy the Oracle Schema Changes (one-time, per database)

Run this against your shared Oracle schema **before** starting any workers.  
It adds the worker-queue columns and index to `SCHEDULE_PC_EVAL`. The script is idempotent — safe to re-run.

```sql
-- From SQLcl, connected as the HR schema user:
@src/PositionAnalysis.Mcp/Database/ADD_SCHEDULE_PC_WORK_QUEUE.sql
```

Columns added:

| Column | Purpose |
|--------|---------|
| `WORKER_ID` | Identifies which server claimed a PD |
| `CLAIMED_AT` | When the claim was taken |
| `LEASE_EXPIRES_AT` | Claim expiry (15 min) — used to recover orphaned PDs if a server crashes |

> **You only need to do this once**, even when adding a second server.

---

## Step 2 — Publish the Application

Run this from the repo root on your build machine:

```powershell
dotnet publish src/PositionAnalysis.Cli/PositionAnalysis.Cli.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -o D:\deploy\PositionAnalysis
```

Copy the entire `D:\deploy\PositionAnalysis` folder to the same path on each Windows Server
(the `PositionAnalysis.Mcp` build must sit in a `PositionAnalysis.Mcp` folder next to the Cli
executable, or be resolvable via the source-tree fallback).

> **Self-contained** means no .NET runtime installation is needed on the server.

---

## Step 3 — Set Environment Variables (run on each server, as Administrator)

Secrets are supplied via system-level environment variables so they never live in config files.

Open PowerShell **as Administrator** and run:

```powershell
.\scripts\Set-PositionAnalysisEnv.ps1 `
    -OracleConnectionString "hr/YourPassword@//dbserver:1521/XEPDB1" `
    -AzureOpenAiApiKey "your-azure-openai-key"
```

Optional overrides (defaults match `appsettings.json`):

```powershell
.\scripts\Set-PositionAnalysisEnv.ps1 `
    -OracleConnectionString "hr/YourPassword@//dbserver:1521/XEPDB1" `
    -AzureOpenAiApiKey "your-azure-openai-key" `
    -AzureOpenAiEndpoint "https://your-endpoint.openai.azure.us/" `
    -AzureOpenAiDeploymentName "gpt-4o"
```

Variables set at the **Machine** level (visible to Task Scheduler running as SYSTEM):

| Variable | Setting |
|----------|---------|
| `Oracle__ConnectionString` | Oracle connection string |
| `AiProvider__AzureOpenAI__ApiKey` | Azure OpenAI API key |
| `AiProvider__AzureOpenAI__Endpoint` | Azure OpenAI endpoint URL |
| `AiProvider__AzureOpenAI__DeploymentName` | Model deployment name |

> Changes take effect immediately for new processes. Running tasks must be restarted.

---

## Step 4 — Register the Scheduled Task (run on each server, as Administrator)

Open PowerShell **as Administrator** and run:

```powershell
.\scripts\Register-ScheduledTask.ps1 `
    -ExePath "D:\deploy\PositionAnalysis\PositionAnalysis.Cli.exe"
```

This creates a daily task under `\PositionAnalysis\PositionAnalysis-ProcessAll` that runs at
**5:00 PM** as SYSTEM. Set the task's Start In directory to the deployment root.

> Do not run staging, document generation, or export on worker machines — those are
> interactive-only operations (see [how-to-positionanalysis-interactive-chat.md](how-to-positionanalysis-interactive-chat.md)).

Optional overrides:

```powershell
.\scripts\Register-ScheduledTask.ps1 `
    -ExePath "D:\deploy\PositionAnalysis\PositionAnalysis.Cli.exe" `
    -RunAt "18:00" `
    -TaskName "PositionAnalysis-NightlyScore"
```

To test immediately after registering:

```powershell
Start-ScheduledTask -TaskPath "\PositionAnalysis\" -TaskName "PositionAnalysis-ProcessAll"
```

To check last run status:

```powershell
Get-ScheduledTaskInfo -TaskPath "\PositionAnalysis\" -TaskName "PositionAnalysis-ProcessAll"
```

---

## Running on Two Servers

No additional setup is needed beyond repeating Steps 3–4 on the second server. Both servers point to the same Oracle database and compete for work via the claim queue.

How it works:
- Each worker claims one `PENDING` PD at a time using `FOR UPDATE SKIP LOCKED` — no PD is ever
  scored twice by workers that are alive and holding a valid lease
- Each claim has a **15-minute lease** — if a server crashes, the next run on any server
  automatically recovers orphaned PDs (inspect `IN_PROGRESS` rows and `lease_expires_at`
  timestamps to confirm)
- Both servers score in parallel, draining the queue faster

> Start with one worker per machine. Add workers only after confirming Azure OpenAI
> **requests-per-minute quota** and reviewing Serilog logs — two servers double the API
> call rate.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Queue drained with no failed PDs |
| `1` | Queue drained with one or more failed PDs, or the run itself failed |
| `2` | Cancelled before normal terminal completion |

The Task Scheduler task is configured to **retry twice** (10-minute interval) on non-zero exit.

## Logs

Serilog writes a rolling daily log to:

```
D:\deploy\PositionAnalysis\logs\schedulepcmcp-.log
```

Each scored PD logs a cost line:

```
[INF] PD 12345 LLM cost: 1,842 prompt + 312 completion = 2,154 tokens, ~$0.0054
```

---

## Updating the LLM Pricing Table

Token cost estimates are loaded from `llm-pricing.json` next to the exe. Edit it directly on the server to update prices — no redeployment needed:

```
D:\deploy\PositionAnalysis\llm-pricing.json
```
