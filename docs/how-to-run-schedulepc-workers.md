# How to Run Schedule PC Scoring Workers

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

## What `--process-all` does

`PositionAnalysis.Cli.exe --process-all` starts the `PositionAnalysis.Mcp` child process,
triggers the `process_all_pds` MCP tool, then polls `get_queue_status` until the queue is
drained. It scores only — it does not stage PDs, generate documents, or export results.

Exit codes:

| Code | Meaning |
|------|---------|
| `0` | Queue drained with no failed PDs |
| `1` | Queue drained with one or more failed PDs, or the run itself failed |
| `2` | Cancelled before normal terminal completion |

## Deployment and Task Scheduler rules

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

## Scale-out

Any number of workers can run concurrently against the same `schedule_pc_eval` queue.
Each worker claims one `PENDING` row at a time with `FOR UPDATE SKIP LOCKED`, so rows
are never scored twice by workers that are alive and holding a valid lease.

## Recovery

If a worker crashes or is killed mid-claim, its claimed rows remain `IN_PROGRESS` with a
`lease_expires_at` timestamp. Any worker's next `--process-all` run calls
`RecoverExpiredClaimsAsync` first, resetting only rows whose lease has expired back to
`PENDING` before claiming new work. No manual intervention is required unless the lease
window itself needs to be shortened for faster recovery.
