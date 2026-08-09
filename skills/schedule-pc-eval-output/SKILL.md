---
name: schedule-pc-eval-output
description: Generate Word evaluation form for one scored PD. Reads RESULT_JSON from SCHEDULE_PC_EVAL, writes data-json file, runs Fill-EvalTemplate-Batch-v2.ps1. Callable standalone (/schedule-pc-eval-output D01880) or by the orchestrator.
---

# /schedule-pc-eval-output {PD_NBR}

Reads a scored result from Oracle and generates the Word evaluation form.

---

## PARAMETERS

| Parameter | Source | Default |
|-----------|--------|---------|
| `PD_NBR` | Slash command argument or prompted | (required) |
| `RunId` | Passed by orchestrator, or resolved from Oracle | most recent done RunId for this PD |
| `OUTPUT_ROOT` | Passed by orchestrator, or resolved | two directories above this SKILL.md |

If `PD_NBR` is not provided, prompt: "PD number to generate output for?"

If `RunId` not passed:
- Query: `SELECT RUN_ID FROM SCHEDULE_PC_EVAL WHERE PD_NBR = '{PD_NBR}' AND STATUS = 'done' ORDER BY SCORED_AT DESC FETCH FIRST 1 ROWS ONLY`
- If found: use that `RUN_ID`
- If not found: print `No scored result found for PD {PD_NBR}` and stop.

---

## REQUIRED MCP TOOLS

Fetch before executing:
`ToolSearch` with `query: "select:mcp__sqlcl__sql_run"`

**Connection:** The Oracle session is managed by whoever invokes this skill. Do NOT connect or disconnect — use the already-active session.

---

## Step 1 · Fetch Result from Oracle

```sql
SELECT RESULT_JSON, PD_NBR, TITLE, SERIES, GRADE, PAY_PLAN
FROM   SCHEDULE_PC_EVAL
WHERE  PD_NBR  = '{PD_NBR}'
  AND  RUN_ID  = '{RunId}'
  AND  STATUS IN ('done', 'skipped')
```

Use `execution_type: "SYNCHRONOUS"`.

If 0 rows → print `PD {PD_NBR} not found with status done/skipped for RunId {RunId}` and stop.

---

## Step 2 · Write JSON File

Write `{OUTPUT_ROOT}/reports/schedule-pc/data-json/PD-{PD_NBR}.json` using the `Write` tool.
Content: the `RESULT_JSON` value exactly as stored in Oracle.

Enforce before writing:
- `IsCandidate` is exactly `"YES"`, `"BORDERLINE"`, or `"NO"`
- `Rating` is exactly `"HIGH"`, `"MEDIUM"`, `"BORDERLINE"`, or `"LOW"`
- All 4 Criteria entries present with v2 names
- `JustificationSummary` is at least 3 sentences

If validation fails → print warning with field name, skip Word generation, return `OUTPUT_VALIDATION_FAILED:{PD_NBR}`.

---

## Step 3 · Run Word Fill Script

```powershell
powershell.exe -File "{OUTPUT_ROOT}/scripts/Fill-EvalTemplate-Batch-v2.ps1" `
               -DataFolder   "{OUTPUT_ROOT}/reports/schedule-pc/data-json" `
               -OutputFolder "{OUTPUT_ROOT}/reports/schedule-pc/form-word"
```

Do NOT pass `-PdNbr`. Do NOT use `Fill-EvalTemplate-Batch.ps1` (v1).

Print the script output. If script exits with non-zero code → return `WORD_SCRIPT_FAILED:{error}`.

On success → print: `✓ Word form written: PD-{PD_NBR}_{Title-Hyphenated}_{PayPlan}-{Series}-{Grade}.docx`

---

## Failure Signals

| Signal | Meaning |
|--------|---------|
| `OUTPUT_VALIDATION_FAILED:{PD_NBR}` | RESULT_JSON failed schema check |
| `WORD_SCRIPT_FAILED:{error}` | PowerShell script exited with error |
