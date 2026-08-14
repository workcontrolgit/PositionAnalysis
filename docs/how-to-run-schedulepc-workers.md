# How to Run Schedule PC Scoring Workers (Unattended)

Run the Schedule PC scoring queue unattended (`--process-all`) or interactively (chat mode),
on a schedule, across one or more machines.

`PositionAnalysis.Cli` is the executable users run. It launches `PositionAnalysis.Mcp` as a
child process over stdio and talks to it through MCP tool calls — you never run
`PositionAnalysis.Mcp` directly.

## Running the CLI

From source, run either project via `dotnet`:

```powershell
cd src\PositionAnalysis.Cli
dotnet run
```

Or, against a published build, run `PositionAnalysis.Cli.exe` directly. The CLI resolves
`PositionAnalysis.Mcp` next to itself (published `PositionAnalysis.Mcp\PositionAnalysis.Mcp.exe`),
falling back to the built DLL or source project when running from a dev checkout — no manual
MCP server startup is required either way.

Configure the AI provider in `src/PositionAnalysis.Cli/appsettings.json` (`AI:Provider` =
`AzureOpenAI`, `Ollama`, or `Claude`) before running interactively; `--process-all` also needs
a working provider because rescoring calls the LLM.

## Interactive chat mode

Running the CLI with no arguments starts a conversational loop. Type natural language; there
are no fixed slash commands. Type `exit` to quit. Examples:

| Intent | Example phrases |
|--------|------------------|
| Stage PDs | `stage pds`, `stage series 0301`, `stage grade 13-15`, `stage org EXEC-POL` |
| Staging report | `staging report`, `show staged report` |
| Processing status | `status`, `processing status series 0301`, `status pd D01880` |
| Process (score) PDs | `process series 0301, 0560`, `process pd D01880`, `process all pds` |
| Rescore everything | `rescore all` |
| Rescore flagged only | `rescore flagged`, `rescore flagged by series 00201`, `rescore flagged by pd D01035` |
| Rescore one PD | `rescore_pd 200028` |
| Rescore the human-scored baseline set | `rescore human` |
| Retry failed PDs | `retry failed` |
| Generate Word documents | `generate documents`, `generate documents series 0301`, `generate documents org EXEC-POL` |
| Export results to Excel | `export results`, `export results series 0301`, `export results pd D01880` |
| Clear the eval table | `clear schedule_pc_eval` |
| List available MCP tools | `tools`, `what tools do you have` |

Series codes are matched as 5-digit tokens; PD numbers are 6+ digit or letter-prefixed tokens
(e.g. `D00240`). Exports only ever include PDs with a completed evaluation (not pending, in
progress, or failed).

## MCP tools reference

The chat loop translates natural language into calls against these `PositionAnalysis.Mcp`
tools. All can also be invoked directly by an MCP client.

| Tool | Description |
|------|-------------|
| `stage_pds` | Stage position descriptions from Oracle into `SCHEDULE_PC_EVAL` for evaluation |
| `get_staging_report` | Get total staged PD count by occupational series (pre-scoring snapshot) |
| `process_pds_by_series` | Start asynchronous scoring for staged PDs in one or more series |
| `process_all_pds` | Start asynchronous scoring for all staged PENDING PDs across every occupational series |
| `get_queue_status` | Get aggregate Schedule PC worker queue status |
| `get_processing_status` | Get processing progress for ALL occupational series with no filtering |
| `get_processing_status_by_series` | Get processing progress filtered to specific occupational series codes |
| `get_processing_status_by_orgs` | Get processing progress filtered to specific bureau/org codes |
| `get_processing_status_by_pd` | Get processing status filtered to specific PD numbers |
| `get_needs_rescore_count` | Get the count of PDs flagged `needs_rescore = 'Y'`, optionally filtered by series or PD numbers |
| `rescore_pd` | Force a fresh LLM rescore of a single PD by its PD number, overwriting any existing result regardless of current status |
| `rescore_pds_by_series` | Force a fresh LLM rescore of all PDs in the specified occupational series, overwriting existing results regardless of status |
| `rescore_all_pds` | Force a fresh LLM rescore of every staged PD across all series, overwriting existing results regardless of status |
| `rescore_flagged_pds` | Force a fresh LLM rescore of every PD flagged `needs_rescore = 'Y'` |
| `rescore_flagged_pds_by_series` | Force a fresh LLM rescore of PDs flagged `needs_rescore = 'Y'` within the specified occupational series |
| `rescore_flagged_pds_by_pd` | Force a fresh LLM rescore of the given PD numbers, but only those flagged `needs_rescore = 'Y'` |
| `rescore_human_schedule_pc_pds` | Rescore exactly the 90 PDs that human reviewers flagged as Schedule P/C, without processing any other pending PDs |
| `retry_failed_pds` | Reset all FAILED evaluation rows back to PENDING so they will be re-scored on the next `process_pds_by_series` call |
| `generate_documents` | Generate Word evaluation documents for ALL completed evaluations with no filtering |
| `generate_documents_by_series` | Generate Word evaluation documents filtered to completed evaluations in the given occupational series |
| `generate_documents_by_orgs` | Generate Word evaluation documents filtered to completed evaluations matching the given bureau/org codes |
| `generate_documents_by_pd` | Generate Word evaluation documents filtered to the given PD numbers |
| `export_results` | Export ALL evaluation results to Excel with no filtering |
| `export_results_by_series` | Export evaluation results to Excel filtered to the given occupational series |
| `export_results_by_orgs` | Export evaluation results to Excel filtered to the given bureau/org codes |
| `export_results_by_pd` | Export evaluation results to Excel filtered to the given PD numbers |
| `clear_schedule_pc_eval` | Delete all records from `SCHEDULE_PC_EVAL` |

---

`PositionAnalysis.Cli.exe --process-all` starts the `PositionAnalysis.Mcp` child process,
triggers the `process_all_pds` MCP tool, then polls `get_queue_status` until the queue is
drained. It scores only — it does not stage PDs, generate documents, or export results.

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
    -o C:\deploy\PositionAnalysis
```

Copy the entire `C:\deploy\PositionAnalysis` folder to the same path on each Windows Server.

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
    -ExePath "C:\deploy\PositionAnalysis\PositionAnalysis.Cli.exe"
```

This creates a daily task under `\PositionAnalysis\PositionAnalysis-ProcessAll` that runs at **5:00 PM** as SYSTEM.

Optional overrides:

```powershell
.\scripts\Register-ScheduledTask.ps1 `
    -ExePath "C:\deploy\PositionAnalysis\PositionAnalysis.Cli.exe" `
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
- Each worker claims one `PENDING` PD at a time using `FOR UPDATE SKIP LOCKED` — no PD is ever scored twice
- Each claim has a **15-minute lease** — if a server crashes, the next run on any server automatically recovers orphaned PDs
- Both servers score in parallel, draining the queue faster

> Watch your Azure OpenAI **requests-per-minute quota** — two servers double the API call rate.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Queue drained, no failures |
| `1` | Queue drained with one or more failed PDs, or run failed |
| `2` | Cancelled before completion |

The Task Scheduler task is configured to **retry twice** (10-minute interval) on non-zero exit.

- Deploy the same published `PositionAnalysis.Cli` and `PositionAnalysis.Mcp` build to every
  worker machine (the Mcp build must sit in a `PositionAnalysis.Mcp` folder next to the Cli
  executable, or be resolvable via the source-tree fallback).
- Run `src/PositionAnalysis.Mcp/Database/ADD_SCHEDULE_PC_WORK_QUEUE.sql` once against the
  shared Oracle schema before enabling a second worker.
- Configure each scheduled task to run `PositionAnalysis.Cli.exe --process-all` under an
  identity with Oracle and Azure OpenAI access.
- Set the task's Start In directory to the deployment root, and do not run staging,
  document generation, or export on workers.
- Start with one worker per machine. Add workers only after confirming Azure OpenAI
  quota and reviewing Serilog logs (written under `PositionAnalysis.Mcp/logs/`).
- Inspect `IN_PROGRESS` rows and `lease_expires_at` timestamps when a worker is
  interrupted; the next run recovers only expired claims (default 15-minute lease).

## Logs

Serilog writes a rolling daily log to:

```
C:\deploy\PositionAnalysis\logs\schedulepcmcp-.log
```

Each scored PD logs a cost line:

```
[INF] PD 12345 LLM cost: 1,842 prompt + 312 completion = 2,154 tokens, ~$0.0054
```

---

## Updating the LLM Pricing Table

Token cost estimates are loaded from `llm-pricing.json` next to the exe. Edit it directly on the server to update prices — no redeployment needed:

```
C:\deploy\PositionAnalysis\llm-pricing.json
```
