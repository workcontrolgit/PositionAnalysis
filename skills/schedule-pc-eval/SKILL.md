---
name: schedule-pc-eval
description: Evaluate position descriptions from Oracle against Schedule Policy/Career criteria. Oracle-first: queries MAX_PD_VW + PD_DUTIES directly, processes one PD at a time, tracks run state in SCHEDULE_PC_EVAL. Supports parallel PCs via series-level assignment.
---

# /schedule-pc-eval

**Print this banner first, before any other output:**

```
╔══════════════════════════════════════════════════════════════════╗
║          SCHEDULE POLICY/CAREER POSITION EVALUATOR              ║
╠══════════════════════════════════════════════════════════════════╣
║  Evaluates position descriptions against Schedule PC criteria   ║
║  Authority: EO "Implementing Schedule Policy/Career" Jun 2026   ║
╠══════════════════════════════════════════════════════════════════╣
║  Source: Oracle (MAX_PD_VW · PD_DUTIES)                        ║
║  Tracker: SCHEDULE_PC_EVAL                                      ║
║  Sub-skills: -score · -output · -export                        ║
╚══════════════════════════════════════════════════════════════════╝
```

---

## KEYBOARD SHORTCUTS

All prompts support keyboard shortcuts:
- **Yes/No prompts:** `1` = yes · `2` = no
- **Multi-choice prompts:** type the option number
- **`A`**: select all (series selection, etc.)
- **Comma-separated list**: select multiple (e.g. `1,3,5`)
- **Full word still accepted:** `yes`, `no`, `all`, `retry`, etc.

---

## CONFIGURATION

Resolve `{OUTPUT_ROOT}` as the repository root — two directories above this file
(`{OUTPUT_ROOT}/skills/schedule-pc-eval/SKILL.md`). If it cannot be resolved, ask the user.

| Item | Value |
|------|-------|
| Word template | `{OUTPUT_ROOT}/templates/Schedule_PC_Position_Evaluation_Template_Updated.docx` |
| Fill script | `{OUTPUT_ROOT}/scripts/Fill-EvalTemplate-Batch-v2.ps1` |
| Excel script | `{OUTPUT_ROOT}/scripts/Export-EvalToExcel.ps1` |
| JSON output | `{OUTPUT_ROOT}/reports/schedule-pc/data-json/` |
| Word output | `{OUTPUT_ROOT}/reports/schedule-pc/form-word/` |
| Excel output | `{OUTPUT_ROOT}/reports/schedule-pc/tracker-excel/` |

---

## REQUIRED MCP TOOLS

Fetch before executing:
`ToolSearch` with `query: "select:mcp__sqlcl__connect,mcp__sqlcl__sql_run,mcp__sqlcl__disconnect"`

---

## STEP 1 · Connect to Oracle

Call `mcp__sqlcl__connect` with `connection_name: "hr_local"`, `model: "claude-sonnet-4-6"`.

On failure → print error and stop. Do NOT call `schema_information`.

Keep this connection open for all orchestrator bookkeeping (Steps 2–7). The connection is persistent — do NOT disconnect between steps.

---

## STEP 2 · Parameters

Ask in one prompt:

> **Filter:** Which positions to evaluate?
> - `[1]` **all** *(default)* — all qualifying positions (GS/ES/SL/ST/AD, grade 13–15 or ES/SL/ST)
> - `[2]` **pd:** D01880,D01460 — specific PD numbers
> - `[3]` **series:** 0301,0905 — occupational series
> - `[4]` **org:** EXEC-POL,OGC-LIT — org codes
> - `[5]` **grade:** 15 or **grade:** 13-15 — grade or range
> - `[0]` **other** — custom filter expression
>
> **Scorer:** Which engine?
> - `[1]` **claude** *(default)* — Claude agents
> - `[2]` **ollama** — Ollama `gemma4:latest` at `http://localhost:11434`
> - `[3]` **ollama:{model}** — custom model
> - `[4]` **ollama:{model}@{endpoint}** — custom model and endpoint

**Filter → WHERE clause additions:**

| Input | WHERE addition |
|-------|---------------|
| `all` | *(none)* |
| `pd:` list | `AND v.PD_NBR IN ('D01880','D01460')` |
| `series:` list | `AND v.GVT_OCC_SERIES IN ('0301','0905')` |
| `org:` list | `AND v.PD_ORIGIN_ORG_CODE IN ('EXEC-POL','OGC-LIT')` |
| `grade:` single | `AND v.GRD_CODE = '15'` |
| `grade:` range | `AND TO_NUMBER(v.GRD_CODE) BETWEEN 13 AND 15` |

---

## STEP 3 · Run Setup

### Check for existing run

```sql
SELECT RUN_ID, COUNT(*) AS TOTAL,
       SUM(CASE WHEN STATUS = 'done'    THEN 1 ELSE 0 END) AS DONE,
       SUM(CASE WHEN STATUS = 'pending' THEN 1 ELSE 0 END) AS PENDING,
       SUM(CASE WHEN STATUS = 'failed'  THEN 1 ELSE 0 END) AS FAILED
FROM   SCHEDULE_PC_EVAL
WHERE  RUN_ID LIKE '{YYYY-MM-DD}%'
GROUP  BY RUN_ID
ORDER  BY RUN_ID DESC
FETCH  FIRST 1 ROWS ONLY
```

If a run is found from today → prompt:
```
Previous run found: {RunId} ({TOTAL} PDs — {DONE} done, {PENDING} pending, {FAILED} failed)
  [1] resume — continue from pending/failed PDs
  [2] new    — start fresh run (new RunId, re-inserts all qualifying PDs)
```

If `new` or no run found → set `RunId = '{YYYY-MM-DD-HHmm}'` and proceed to bulk INSERT.

### Bulk INSERT qualifying PDs

```sql
INSERT INTO SCHEDULE_PC_EVAL
    (PD_SEQ_NUM, PD_NBR, RUN_ID, TITLE, SERIES, GRADE, PAY_PLAN, ORG_CODE,
     SERVICE_CATEGORY, POSITION_SENSITIVITY, PUBLIC_TRUST, PD_MANAGER_LEVEL,
     EVAL_DATE, EFFECTIVE_DATE, STATUS)
SELECT v.PD_SEQ_NUM,
       v.PD_NBR,
       '{RunId}',
       v.PD_POSITION_TITLE_TEXT,
       v.GVT_OCC_SERIES,
       v.GRD_CODE,
       v.GVT_PAY_PLAN,
       v.PD_ORIGIN_ORG_CODE,
       CASE pdpd.QRP_POSITION_OCCUPIED_CODE
           WHEN '1' THEN 'Competitive'
           WHEN '2' THEN 'Excepted'
           WHEN '3' THEN 'SES General'
           WHEN '4' THEN 'SES Career Reserved'
           WHEN '5' THEN 'Federal Wage System'
           ELSE 'Competitive'
       END,
       CASE pdpd.POSITION_SENSITIVITY
           WHEN '1' THEN 'Non-Sensitive'
           WHEN '2' THEN 'Non-Critical Sensitive'
           WHEN '3' THEN 'Critical Sensitive'
           WHEN '4' THEN 'Special Sensitive'
           ELSE ''
       END,
       CASE pdpd.GM_PUBLIC_TRUST
           WHEN '9'  THEN 'High Risk'
           WHEN '10' THEN 'Mod Risk'
           WHEN '11' THEN 'Low Risk'
           WHEN '99' THEN 'No Risk'
           ELSE 'No Risk'
       END,
       v.PD_MANAGER_LEVEL,
       TRUNC(SYSDATE),
       TRUNC(SYSDATE),
       'pending'
FROM   MAX_PD_VW v
LEFT JOIN PD_POSITION_DATA pdpd ON pdpd.PD_SEQ_NUM = v.PD_SEQ_NUM
WHERE  v.PD_ARCHIVE_IND != 'Y'
AND    v.GVT_PAY_PLAN IN ('GS', 'ES', 'SL', 'ST', 'AD')
AND    (REGEXP_LIKE(v.GRD_CODE, '^1[3-5]$') OR v.GVT_PAY_PLAN IN ('ES', 'SL', 'ST'))
{FilterWhereClause}
```

Use `execution_type: "ASYNCHRONOUS"`.

Print: `{N} PDs queued for RunId {RunId}`

---

## STEP 4 · Series Selection

```sql
SELECT SERIES, COUNT(*) AS PD_COUNT
FROM   SCHEDULE_PC_EVAL
WHERE  RUN_ID = '{RunId}'
  AND  STATUS IN ('pending', 'failed')
GROUP  BY SERIES
ORDER  BY SERIES
```

Print numbered list:
```
Occupational series available for this run:
  [1]  0201 — Human Resources Management (34 PDs)
  [2]  0301 — Program Management (42 PDs)
  [3]  0343 — Management Analysis (87 PDs)
  ...
  [A]  All series ({TOTAL} PDs)

Which series to process? (number, comma-separated list, or A for all)
```

Set `SelectedSeries` = list of SERIES codes to process.

---

## STEP 5 · Confirm

Print:
```
Agency:   ACRS (default Oracle — hr_local)
RunId:    {RunId}
Filter:   {human-readable filter summary}
Series:   {SelectedSeries list or "all"}
Scorer:   {Scorer}
PDs:      {total count for selected series}

Proceed?
  [1] yes — start processing
  [2] no  — cancel (run stays in Oracle for later resume)
```

Wait for confirmation.

---

## STEP 6 · Run Evaluation

Launch the PowerShell orchestration script. It processes every pending/failed PD in Oracle without accumulating context in this session — each PD is scored in an isolated `claude -p` subprocess.

```powershell
powershell.exe -File "{OUTPUT_ROOT}/scripts/Invoke-SchedulePCEval.ps1" `
               -RunId      "{RunId}" `
               -Series     "{SelectedSeries comma-joined, or 'all'}" `
               -Scorer     "{Scorer}" `
               -OutputRoot "{OUTPUT_ROOT}"
```

Stream script output to the user in real time. The script handles:
- Fetching pending/failed PDs from Oracle for the selected series
- Marking each PD `in_progress` → scoring via `claude -p` or ollama → writing JSON → updating Oracle to `done` or `failed`
- Running `Fill-EvalTemplate-Batch-v2.ps1` once at the end to generate Word forms

If the script exits with a non-zero code, print the error and offer:
```
  [1] retry — re-run the script (resumes from pending/failed rows)
  [2] abort — stop here
```

---

## STEP 7 · Export and Summary

After all selected series complete:

Invoke Skill `schedule-pc-eval-export` (auto — no prompt for RunId, use current `{RunId}`).

Then print cross-series summary:

```sql
SELECT SERIES,
       COUNT(*)                                                  AS TOTAL,
       SUM(CASE WHEN STATUS = 'done'    THEN 1 ELSE 0 END)     AS DONE,
       SUM(CASE WHEN STATUS = 'skipped' THEN 1 ELSE 0 END)     AS SKIPPED,
       SUM(CASE WHEN STATUS = 'failed'  THEN 1 ELSE 0 END)     AS FAILED,
       SUM(CASE WHEN IS_CANDIDATE = 'YES'        THEN 1 ELSE 0 END) AS YES_COUNT,
       SUM(CASE WHEN IS_CANDIDATE = 'BORDERLINE' THEN 1 ELSE 0 END) AS BORDERLINE_COUNT,
       SUM(CASE WHEN IS_CANDIDATE = 'NO'         THEN 1 ELSE 0 END) AS NO_COUNT
FROM   SCHEDULE_PC_EVAL
WHERE  RUN_ID = '{RunId}'
  AND  SERIES IN ({SelectedSeries comma list})
GROUP  BY SERIES
ORDER  BY SERIES
```

```
Run complete: {RunId}
────────────────────────────────────────────────────────────────────
 Series  Total  Done  Skipped  Failed  YES  BORDERLINE  NO
 0201      34    34      0       0      3      8        23
 ...
────────────────────────────────────────────────────────────────────
 Total   {N}   {N}     {N}     {N}    {N}    {N}       {N}
```

---

## STEP 8 · Disconnect

Call `mcp__sqlcl__disconnect`.

---

## RUNTIME RULES

- **Do NOT call `schema_information`** — schema is known.
- **Oracle MCP tool:** correct name is `mcp__sqlcl__sql_run`.
- **Oracle connection is persistent** — connect once in STEP 1, do NOT disconnect until STEP 8.
- **PD scoring runs outside this session** — `Invoke-SchedulePCEval.ps1` calls `claude -p` per PD; no scoring context accumulates here.
- **Resume skips done/skipped PDs** — the PS script only processes `pending` and `failed` rows.
- **Parallel PC coordination:** multiple PCs can run this skill simultaneously on different series. The `in_progress` status prevents double-processing.
