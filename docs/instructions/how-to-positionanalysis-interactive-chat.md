# How to Run Position Analysis in Interactive Chat Mode

Run Position Analysis Schedule PC evaluations conversationally from a console — staging,
processing, status, document generation, and export, all driven by natural language.

`PositionAnalysis.Cli` is the executable users run. It launches `PositionAnalysis.Mcp` as a
child process over stdio and talks to it through MCP tool calls — you never run
`PositionAnalysis.Mcp` directly.

For unattended/scheduled scoring runs instead, see
[how-to-positionanalysis-unattended-workers.md](how-to-positionanalysis-unattended-workers.md).

## Running the CLI

From source, run the project via `dotnet`:

```powershell
cd src\PositionAnalysis.Cli
dotnet run
```

Or, against a published build, run `PositionAnalysis.Cli.exe` directly. The CLI resolves
`PositionAnalysis.Mcp` next to itself (published `PositionAnalysis.Mcp\PositionAnalysis.Mcp.exe`),
falling back to the built DLL or source project when running from a dev checkout — no manual
MCP server startup is required either way.

Configure the AI provider in `src/PositionAnalysis.Cli/appsettings.json` (`AI:Provider` =
`AzureOpenAI`, `Ollama`, or `Claude`) before running interactively.

> `AI:AzureOpenAI` (Cli, used here for chat parsing) and `AiProvider:AzureOpenAI`
> (`PositionAnalysis.Mcp`, used for actual PD scoring) are separate config sections in
> separate `appsettings.json` files — they are not synced automatically. If you rotate the
> Azure OpenAI key/endpoint, update both.

## Interactive chat mode

Running the CLI with no arguments starts a conversational loop. Type natural language; there
are no fixed slash commands. Type `exit` to quit. Examples:

| Intent | Example phrases |
|--------|------------------|
| Stage PDs | `stage pds`, `stage series 0301`, `stage grade 13-15`, `stage org EXEC-POL` |
| Processing status | `status`, `processing status series 0301`, `status pd D01880` |
| Process (score) PDs | `process series 0301, 0560`, `process pd D01880`, `process all pds` |
| Rescore flagged only | `rescore flagged`, `rescore flagged by series 00201`, `rescore flagged by pd D01035` |
| Rescore one PD | `rescore_pd 200028` |
| Needs-rescore count | `needs rescore count`, `how many need rescore`, `get_needs_rescore_count` |
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
| `process_batch_by_series` | Score all pending PDs in the specified series in parallel (10-way concurrency) with live progress notifications |
| `run_unattended_scoring` | Start asynchronous scoring for all staged PENDING PDs across every occupational series |
| `get_queue_status` | Get aggregate Schedule PC worker queue status |
| `get_processing_status` | Get processing progress for ALL occupational series with no filtering |
| `get_processing_status_by_series` | Get processing progress filtered to specific occupational series codes |
| `get_processing_status_by_orgs` | Get processing progress filtered to specific bureau/org codes |
| `get_processing_status_by_pd` | Get processing status filtered to specific PD numbers |
| `get_needs_rescore_count` | Get the count of PDs flagged `needs_rescore = 'Y'`, optionally filtered by series or PD numbers |
| `rescore_pd` | Force a fresh LLM rescore of a single PD by its PD number, overwriting any existing result regardless of current status |
| `rescore_pds_by_series` | Force a fresh LLM rescore of all PDs in the specified occupational series, overwriting existing results regardless of status |
| `rescore_flagged_pds` | Force a fresh LLM rescore of every PD flagged `needs_rescore = 'Y'` |
| `rescore_flagged_pds_by_series` | Force a fresh LLM rescore of PDs flagged `needs_rescore = 'Y'` within the specified occupational series |
| `rescore_flagged_pds_by_pd` | Force a fresh LLM rescore of the given PD numbers, but only those flagged `needs_rescore = 'Y'` |
| `retry_failed_pds` | Reset all FAILED evaluation rows back to PENDING so they will be re-scored on the next `process_batch_by_series` or `run_unattended_scoring` call |
| `generate_documents_all` | Generate Word evaluation documents for ALL completed evaluations with no filtering |
| `generate_documents_by_series` | Generate Word evaluation documents filtered to completed evaluations in the given occupational series |
| `generate_documents_by_orgs` | Generate Word evaluation documents filtered to completed evaluations matching the given bureau/org codes |
| `generate_documents_by_pd` | Generate Word evaluation documents filtered to the given PD numbers |
| `export_results_all` | Export ALL evaluation results to Excel with no filtering |
| `export_results_by_series` | Export evaluation results to Excel filtered to the given occupational series |
| `export_results_by_orgs` | Export evaluation results to Excel filtered to the given bureau/org codes |
| `export_results_by_pd` | Export evaluation results to Excel filtered to the given PD numbers |
| `clear_schedule_pc_eval` | Delete all records from `SCHEDULE_PC_EVAL` |

## Logs

Serilog writes a rolling daily log next to the running executable:

```
PositionAnalysis.Mcp\logs\schedulepcmcp-.log
```

Each scored PD logs a cost line:

```
[INF] PD 12345 LLM cost: 1,842 prompt + 312 completion = 2,154 tokens, ~$0.0054
```

## Updating the LLM Pricing Table

Token cost estimates are loaded from `llm-pricing.json` next to the exe. Edit it directly to
update prices — no redeployment needed:

```
PositionAnalysis.Mcp\llm-pricing.json
```
