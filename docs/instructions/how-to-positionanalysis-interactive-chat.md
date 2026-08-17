# How to Run Position Analysis in Interactive Chat Mode

Run Position Analysis Schedule PC evaluations from a console using either a numbered
menu or plain English — staging, scoring, status, document generation, and export, all
driven by the same conversational interface.

`PositionAnalysis.Cli` is the executable users run. It launches `PositionAnalysis.Mcp` as a
child process over stdio and talks to it through MCP tool calls — you never run
`PositionAnalysis.Mcp` directly.

For unattended/scheduled scoring runs instead, see
[how-to-positionanalysis-unattended-workers.md](how-to-positionanalysis-unattended-workers.md).

## Running the CLI

From source:

```powershell
cd src\PositionAnalysis.Cli
dotnet run
```

From a published/deployed build, run `PositionAnalysis.Cli.exe` directly. The CLI resolves
`PositionAnalysis.Mcp` next to itself automatically — no manual MCP server startup required.

Configure the AI provider in `src/PositionAnalysis.Cli/appsettings.json` (`AI:Provider` =
`AzureOpenAI`, `Ollama`, or `Claude`) before running interactively.

> **Note:** `AI:AzureOpenAI` (Cli, for the chat assistant) and `AiProvider:AzureOpenAI`
> (`PositionAnalysis.Mcp`, for PD scoring) are separate config sections in separate
> `appsettings.json` files. If you rotate the Azure OpenAI key/endpoint, update both.

## Startup menu

When the CLI starts it displays a numbered menu:

```
 Report Status          Evaluate               Generate Word
 1  All series          5  All pending          9  All
 2  By series           6  By series           10  By series
 3  By org code         7  By org code         11  By org code
 4  By PD number        8  By PD number        12  By PD number

 Export Excel           Manage                  Help
13  All                17  Stage PDs            Type a number or ask a
14  By series          18  Clear staged PDs     question in plain English.
15  By org code        19  Reset failed → staged
16  By PD number       20  Run unattended       ?  — show menu
                       21  Unattended status    Q  — quit
                       22  Re-score by PD
                       23  Re-score by series
                       24  Rebucket ratings (all)
                       25  Rebucket ratings by series
```

Type a number to activate that option. For options that require parameters (series codes,
org codes, PD numbers) the CLI prompts for them immediately. Type `?` at any time to
re-display the menu.

## Confirmation prompt

Any operation that modifies data (evaluate, generate, export, stage, clear, re-score, etc.)
first shows a confirmation panel with the number of records affected and an estimated cost
(where applicable), then asks **1 — Yes / 2 — No** before proceeding. Cancelling returns
you to the prompt with no changes made.

```
╔═══════════════════════════════╗
║  ⚠  Confirmation Required     ║
║  Records to process:   142    ║
║  Estimated cost:  $0.77 USD   ║
╚═══════════════════════════════╝
  1 — Yes, proceed
  2 — No, cancel
```

## Cancelling a running operation

While a long operation is running, press **Esc** to cancel. The CLI sends a cancellation
signal to the MCP server, waits for any in-flight LLM calls to finish (up to 60 seconds),
then returns to the prompt. Progress already saved to the database is preserved.

## Natural language

You can also type any question or instruction in plain English instead of using the menu
numbers. Examples:

| Intent | Example phrases |
|--------|-----------------|
| Stage PDs | `stage pds`, `add new pds to staging` |
| Clear staging table | `clear all staged pds` |
| Processing status | `status`, `status for series 0301`, `status pd D01880` |
| Evaluate (score) PDs | `process series 0301, 0560`, `process pd D01880`, `evaluate all` |
| Re-score PDs | `re-score pd 200028`, `re-score series 00301` |
| Rebucket ratings | `rebucket ratings`, `rebucket ratings for series 0301` |
| Reset failed PDs | `reset failed pds to staged` |
| Generate Word documents | `generate documents`, `generate docs for series 0301` |
| Export to Excel | `export results`, `export results for pd D01880` |
| Run unattended scoring | `run unattended scoring` |
| Unattended queue status | `unattended queue status` |

## Staging behaviour

`Stage PDs` (menu item 17) is **add-only** — it inserts PDs from the Oracle source table
(`temp_pd_sched_pc`) that do not already exist in `SCHEDULE_PC_EVAL`. Existing rows and
their scores are never overwritten. This means you can safely stage new series or newly
added PDs without disturbing in-progress or completed evaluations.

To wipe the staging table entirely, use **18 — Clear staged PDs**.

## Rebucketing ratings

**24 — Rebucket ratings (all)** and **25 — Rebucket ratings by series** re-derive the
HIGH/MEDIUM/LOW rating for every already-scored PD using the current thresholds in
`PositionAnalysis.Mcp/appsettings.json`:

```json
"RatingThresholds": {
  "HighMinCriteriaTriggered": 3,
  "MediumMinCriteriaTriggered": 1
}
```

| Criteria triggered | Rating |
|--------------------|--------|
| ≥ 3 | HIGH |
| 1–2 | MEDIUM |
| 0 | LOW |

No LLM call is made — the stored criteria trigger flags are already in the database.
Use this after changing the threshold values without needing to re-run the expensive
LLM scoring. Restart the MCP server after editing `appsettings.json` for the new
thresholds to take effect.

## MCP tools reference

| Tool | Description |
|------|-------------|
| `stage_pds` | Add new PDs from Oracle source into `SCHEDULE_PC_EVAL` (add-only, skips existing) |
| `stage_pds_clear` | Delete all records from `SCHEDULE_PC_EVAL` |
| `reset_failed_to_staged` | Reset all FAILED rows back to PENDING for retry |
| `process_batch_all` | Score all pending PDs globally in parallel |
| `process_batch_by_series` | Score all pending PDs in the specified series |
| `process_batch_by_orgs` | Score all pending PDs matching the given org codes |
| `process_batch_by_pds` | Score the specified PD numbers |
| `run_unattended_scoring` | Start asynchronous scoring for all staged PENDING PDs |
| `get_unattended_queue_status` | Get aggregate unattended worker queue status |
| `get_processing_status` | Processing progress for ALL series |
| `get_processing_status_by_series` | Processing progress for specific series |
| `get_processing_status_by_orgs` | Processing progress for specific org codes |
| `get_processing_status_by_pd` | Processing status for specific PD numbers |
| `rescore_by_pds` | Force a fresh LLM re-score of specific PD numbers |
| `rescore_by_series` | Force a fresh LLM re-score of all PDs in the specified series |
| `generate_documents_all` | Generate Word evaluation documents for all completed evaluations |
| `generate_documents_by_series` | Generate Word docs filtered to the given series |
| `generate_documents_by_orgs` | Generate Word docs filtered to the given org codes |
| `generate_documents_by_pd` | Generate Word docs for the given PD numbers |
| `export_results_all` | Export all evaluation results to Excel |
| `export_results_by_series` | Export results filtered to the given series |
| `export_results_by_orgs` | Export results filtered to the given org codes |
| `export_results_by_pd` | Export results for the given PD numbers |
| `cancel_current_batch` | Cancel the currently running batch operation |
| `rebucket_ratings` | Re-derive HIGH/MEDIUM/LOW ratings from stored criteria counts using current thresholds — no LLM cost |

All destructive or long-running tools require confirmation before executing (see above).

## Logs

Serilog writes a rolling daily log next to the MCP executable:

```
PositionAnalysis.Mcp\logs\schedulepcmcp-YYYYMMDD.log
```

Each scored PD logs a cost line:

```
[INF] PD 12345 LLM cost: 1,842 prompt + 312 completion = 2,154 tokens, ~$0.0054
```

## Updating the LLM pricing table

Token cost estimates are loaded from `llm-pricing.json` next to the MCP exe. Edit it
directly to update prices — no redeployment needed:

```
PositionAnalysis.Mcp\llm-pricing.json
```
