---
name: schedule-pc-eval-export
description: Export Schedule PC evaluation results to Excel. Reads run status from SCHEDULE_PC_EVAL, lets user pick a RunId, then runs Export-EvalToExcel.ps1. Callable on demand to check project status at any time.
---

# /schedule-pc-eval-export

Exports Schedule PC evaluation results to Excel from Oracle. Call at any time during or after a run to get a snapshot of current progress.

---

## PARAMETERS

| Parameter | Source | Default |
|-----------|--------|---------|
| `RunId` | Prompted | most recent RunId |
| `OUTPUT_ROOT` | Resolved | two directories above this SKILL.md |

---

## REQUIRED MCP TOOLS

Fetch before executing:
`ToolSearch` with `query: "select:mcp__sqlcl__sql_run"`

**Connection:** Use the already-active Oracle session. Do NOT connect or disconnect.

---

## Step 1 · Show Available Runs

```sql
SELECT RUN_ID,
       COUNT(*)                                                    AS TOTAL,
       SUM(CASE WHEN STATUS = 'done'      THEN 1 ELSE 0 END)     AS DONE,
       SUM(CASE WHEN STATUS = 'pending'   THEN 1 ELSE 0 END)     AS PENDING,
       SUM(CASE WHEN STATUS = 'in_progress' THEN 1 ELSE 0 END)   AS IN_PROGRESS,
       SUM(CASE WHEN STATUS = 'failed'    THEN 1 ELSE 0 END)     AS FAILED,
       SUM(CASE WHEN STATUS = 'skipped'   THEN 1 ELSE 0 END)     AS SKIPPED,
       SUM(CASE WHEN IS_CANDIDATE = 'YES'        AND STATUS = 'done' THEN 1 ELSE 0 END) AS YES_COUNT,
       SUM(CASE WHEN IS_CANDIDATE = 'BORDERLINE' AND STATUS = 'done' THEN 1 ELSE 0 END) AS BORDERLINE_COUNT,
       SUM(CASE WHEN IS_CANDIDATE = 'NO'         AND STATUS = 'done' THEN 1 ELSE 0 END) AS NO_COUNT
FROM   SCHEDULE_PC_EVAL
GROUP  BY RUN_ID
ORDER  BY RUN_ID DESC
```

Use `execution_type: "SYNCHRONOUS"`.

Print:
```
Available runs:
  [1] 2026-08-09-0900 — 1,240 total · 850 done · 390 pending · 2 failed  (YES: 42 · BORDERLINE: 98 · NO: 710)
  [2] 2026-08-08-1430 — 14,000 total · 14,000 done · 0 pending           (YES: 612 · BORDERLINE: 1,204 · NO: 12,184)
  [A] all runs
```

Prompt: "Which run to export? (Enter number or A for all — default [1])"

---

## Step 2 · Run Excel Export Script

```powershell
powershell.exe -File "{OUTPUT_ROOT}/scripts/Export-EvalToExcel.ps1" `
               -DataFolder   "{OUTPUT_ROOT}/reports/schedule-pc/data-json" `
               -OutputFolder "{OUTPUT_ROOT}/reports/schedule-pc/tracker-excel"
```

Print script output. If script exits with error → print `Excel export failed: {error}` and stop.

---

## Step 5 · Print Summary

```
Export complete
────────────────────────────────────────────────────
Run: {RunId}
Total:      {TOTAL}
Done:       {DONE}
Pending:    {PENDING}
Failed:     {FAILED}
────────────────────────────────────────────────────
YES:        {YES_COUNT}
BORDERLINE: {BORDERLINE_COUNT}
NO:         {NO_COUNT}
────────────────────────────────────────────────────
Excel: {filepath}
```
