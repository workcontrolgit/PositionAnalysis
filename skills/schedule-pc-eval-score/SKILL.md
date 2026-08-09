---
name: schedule-pc-eval-score
description: Score one position description against Schedule Policy/Career criteria. Fetches PD data from Oracle, calls LLM, writes result to SCHEDULE_PC_EVAL. Callable standalone (/schedule-pc-eval-score D01880) or by the orchestrator.
---

# /schedule-pc-eval-score {PD_NBR}

Scores one position description against Schedule Policy/Career criteria under EO
"Implementing Schedule Policy/Career in the Excepted Service" (June 3, 2026).

**Authority:** EO "Implementing Schedule Policy/Career in the Excepted Service" (June 3, 2026)
**Legal Basis:** 5 U.S.C. § 7511(b)(2); 5 U.S.C. § 3302

---

## PARAMETERS

| Parameter | Source | Default |
|-----------|--------|---------|
| `PD_NBR` | Slash command argument or prompted | (required) |
| `RunId` | Passed by orchestrator, or resolved from Oracle | most recent active RunId for this PD |
| `Scorer` | Passed by orchestrator, or prompted | `claude` |
| `OllamaModel` | Passed by orchestrator | `gemma4:latest` |
| `OllamaEndpoint` | Passed by orchestrator | `http://localhost:11434` |

If `PD_NBR` is not provided as an argument, prompt: "PD number to score?"

If invoked standalone (no `RunId` passed):
- Query: `SELECT RUN_ID FROM SCHEDULE_PC_EVAL WHERE PD_NBR = '{PD_NBR}' AND STATUS != 'done' ORDER BY RUN_ID DESC FETCH FIRST 1 ROWS ONLY`
- If a row is found: use that `RUN_ID`
- If no row found: INSERT a new row with `RUN_ID = '{YYYY-MM-DD-HHmm}'`, `STATUS = 'pending'`, and header fields fetched in Step 2, then use that `RUN_ID`

If `Scorer` is not passed by orchestrator, prompt:
```
Scorer:
  [1] claude (default)
  [2] ollama
  [3] ollama:{model}
```

---

## REQUIRED MCP TOOLS

Fetch before executing:
`ToolSearch` with `query: "select:mcp__sqlcl__sql_run"`

**Connection:** The Oracle session is managed by whoever invokes this skill. When called by the orchestrator, the orchestrator's connection is already active — do NOT connect or disconnect. When invoked standalone, ensure an active connection exists (run `/oracle-sql-query` or connect manually first).

---

## Step 1 · Fetch PD from Oracle

Single query — header fields and all duty rows in one result set:

```sql
SELECT v.PD_SEQ_NUM,
       v.PD_NBR,
       v.PD_POSITION_TITLE_TEXT,
       v.GVT_OCC_SERIES,
       v.GRD_CODE,
       v.GVT_PAY_PLAN,
       v.PD_ORIGIN_ORG_CODE,
       v.PD_MANAGER_LEVEL,
       pdpd.POSITION_SENSITIVITY,
       pdpd.GM_PUBLIC_TRUST,
       pdpd.QRP_POSITION_OCCUPIED_CODE,
       d.PDD_SEQ_NUM,
       d.PDD_MAJOR_DUTIES_TEXT,
       d.PDD_PERCENT_TIME_SPENT,
       d.PDD_CRITICAL_DUTY_IND
FROM   MAX_PD_VW v
JOIN   PD_DUTIES d
       ON d.PD_SEQ_NUM = v.PD_SEQ_NUM
LEFT JOIN PD_POSITION_DATA pdpd
       ON pdpd.PD_SEQ_NUM = v.PD_SEQ_NUM
WHERE  v.PD_NBR = '{PD_NBR}'
ORDER  BY d.PDD_PERCENT_TIME_SPENT DESC NULLS LAST
```

Use `execution_type: "SYNCHRONOUS"`.

If 0 rows returned → print `PD {PD_NBR} not found in MAX_PD_VW` and stop.

**Derive labels from codes:**

`PositionSensitivityLabel` from `POSITION_SENSITIVITY`:
| Code | Label |
|------|-------|
| 1 | Non-Sensitive |
| 2 | Non-Critical Sensitive |
| 3 | Critical Sensitive |
| 4 | Special Sensitive |
| null | (blank) |

`PublicTrustLabel` from `GM_PUBLIC_TRUST`:
| Code | Label |
|------|-------|
| 9 | High Risk |
| 10 | Mod Risk |
| 11 | Low Risk |
| 99 | No Risk |
| null / blank | No Risk |

`ServiceCategoryLabel` from `QRP_POSITION_OCCUPIED_CODE`:
| Code | Label |
|------|-------|
| 1 | Competitive |
| 2 | Excepted |
| 3 | SES General |
| 4 | SES Career Reserved |
| 5 | Federal Wage System |
| null / blank | Competitive |

`PdManagerLevelStr` from `PD_MANAGER_LEVEL`:
| Code | Label |
|------|-------|
| 2 | Supervisor or Manager |
| 4 | Supervisor (CSRA) |
| 5 | Management Official (CSRA) |
| 6 | Leader |
| 7 | Team Leader |
| 8 | All Other Positions |
| null | (blank) |

---

## Step 2 · Mark In-Progress

```sql
UPDATE SCHEDULE_PC_EVAL
SET STATUS = 'in_progress'
WHERE PD_NBR = '{PD_NBR}' AND RUN_ID = '{RunId}'
```

---

## Step 3 · Build Scoring Prompt

Construct the prompt using this template. Substitute all `{placeholders}`:

```
You are a federal HR analyst evaluating one position description against Schedule
Policy/Career criteria under EO "Implementing Schedule Policy/Career in the Excepted
Service" (June 3, 2026), 5 U.S.C. § 7511(b)(2); 5 U.S.C. § 3302.

## Position Data
PdNbr:               {PD_NBR}
Title:               {PD_POSITION_TITLE_TEXT}
PayPlan:             {GVT_PAY_PLAN}
OccSeries:           {GVT_OCC_SERIES}
Grade:               {GRD_CODE}
OrgCode:             {PD_ORIGIN_ORG_CODE}
PdManagerLevel:      {PdManagerLevelStr} ({label})
PositionSensitivity: {POSITION_SENSITIVITY code} ({PositionSensitivityLabel})
PublicTrust:         {GM_PUBLIC_TRUST code} ({PublicTrustLabel})
ServiceCategory:     {ServiceCategoryLabel}
EvalDate:            {EvalDate}
EffectiveDate:       {EvalDate}

## Duty Statements (percent-time descending)
[1] {pdd_major_duties_text} ({pdd_percent_time_spent}%, Critical: {pdd_critical_duty_ind})
[2] ...one entry per duty row, percent-time descending...

## Scoring Criteria

**Duty text is the primary evidence source for all four criteria.** Always scan the full duty
text before rendering any verdict. Do not use PD_INTRO as evidence.

Structural fields (PD_MANAGER_LEVEL, POSITION_SENSITIVITY, GM_PUBLIC_TRUST) are corroborating
signals only — they can tip a borderline duty-text finding but cannot trigger a criterion alone.

**Structural corroborating signals (apply to all four criteria):**

PD_MANAGER_LEVEL secondary triggers: codes 2 (Supervisor or Manager), 4 (Supervisor CSRA),
5 (Management Official CSRA) corroborate borderline duty-text findings. Codes 6/7 are neutral.
Code 8 is a counter-signal. Do NOT trigger on manager level alone.

POSITION_SENSITIVITY secondary triggers: codes 3 (Critical Sensitive) and 4 (Special Sensitive)
corroborate borderline duty-text findings. Codes 1/2 are counter-signals. Do NOT trigger on sensitivity alone.

GM_PUBLIC_TRUST secondary triggers: codes 9 (High Risk) and 10 (Mod Risk) corroborate borderline
findings. Code 11 (Low Risk) is neutral. Codes 99/null are counter-signals. Do NOT trigger alone.

---

### Criterion 1 — Policy-Determining
**Definition:** The incumbent exercises authority to establish, decide, or direct government or
agency policy — not merely advising on or implementing policy decisions made by others.

TRIGGERED if duty text shows: binding determinations on regulatory or programmatic policy
matters; sets final direction on policy positions or outcomes; directs other offices or staff
on what policy positions to adopt; approves/disapproves policy proposals with final authority.

**Scan for:** decides/directs/determines/establishes policy; sets policy direction; has final say;
approves policy positions; makes binding policy determinations; authorizes policy positions;
issues policy directives; exercises authority over policy outcomes.

**Do NOT trigger on:** implementing directives from higher authority; providing input to decisions
made by others; advising on policy without documented decision authority; developing policy options
or recommendations submitted to and reviewed by senior officials; positions that "formulate and
implement plans" — implementation language weighs against Policy-Determining.

PD_MANAGER_LEVEL signal table:
| Code | Label | Signal |
|------|-------|--------|
| 2 | Supervisor or Manager | Secondary trigger |
| 4 | Supervisor (CSRA) | Secondary trigger |
| 5 | Management Official (CSRA) | Secondary trigger |
| 6 | Leader | Neutral |
| 7 | Team Leader | Neutral |
| 8 | All Other Positions | Counter-signal |

Codes 2/4/5 → corroborate borderline duty-text finding. Codes 6/7 → neutral. Code 8 → counter-signal.
POSITION_SENSITIVITY 3/4 → corroborates borderline duty-text finding. Codes 1/2 → counter-signal.
**Do NOT trigger on structural fields alone.**

---

### Criterion 2 — Policy-Making
**Definition:** The incumbent formulates, develops, or substantially shapes policy proposals,
regulatory frameworks, or official policy positions — beyond implementing or reviewing policy
already set by others.

TRIGGERED if duty text shows: originating or drafting regulations, sub-regulatory guidance, or
policy memoranda; leading or substantially contributing to rulemaking; developing agency policy
frameworks or strategic priorities; shaping the substantive content of official policy proposals.

**Scan for:** drafts regulations; develops policy; formulates guidance; leads rulemaking;
participates in rulemaking; writes sub-regulatory guidance; prepares policy memoranda; shapes
policy proposals; develops policy frameworks; develops strategic priorities; crafts regulatory policy.

**Do NOT trigger on:** reviewing others' policy work for administrative or legal compliance;
implementing already-determined policy; technical/scientific analysis that others use to make
policy decisions; legal sufficiency review without policy authorship; strategic planning functions
(a strategic plan is not a policy document); positions that develop and implement plans;
recommendations submitted for approval without evidence the incumbent shapes substantive content
with independent authority; research and analysis that inform decisions without shaping policy
outcomes; internal HR policies without evidence these constitute binding agency-wide policy.

**Program management pattern (common false positive):** Verbs like "develops," "implements,"
"coordinates," "assesses," "evaluates," "researches," "recommends" often describe strategic
program management, not policy-making. Confirm the incumbent originates substantive policy
content with independent authority before triggering.

PD_MANAGER_LEVEL 2/4/5 → corroborate borderline finding. 6/7 → neutral. 8 → counter-signal.
POSITION_SENSITIVITY 3/4 → corroborates borderline finding. 1/2 → counter-signal.
**Do NOT trigger on structural fields alone.**

---

### Criterion 3 — Policy-Advocating
**Definition:** The incumbent is officially designated — not just occasionally asked — to
represent, promote, or defend the agency's or Administration's policy positions before
stakeholders outside the immediate decision-making chain.

TRIGGERED if duty text shows the role is officially assigned to represent agency policy positions
to Congress, OMB, other federal agencies, foreign governments, industry stakeholders, or the
public; testifies on behalf of the agency; presents or defends agency positions in official forums.

**Scan for:** represents agency before Congress/external stakeholders/foreign governments;
testifies on behalf of; presents agency positions; advocates policy positions; promotes agency
positions; defends policy before; serves as agency spokesperson on policy; designated
representative; speaks for the agency on policy; responds to congressional inquiries on policy;
prepares congressional testimony.

**Do NOT trigger on:** routine coordination with peer offices; occasional briefings to internal
management without a formal external representative role; attending meetings without a formal
presenting or advocating function; being one of many participants without a spokesperson role;
representing the agency in administrative proceedings or litigation (legal function, not policy advocacy).

PD_MANAGER_LEVEL 2/4/5 → corroborate borderline finding. 6/7 → neutral. 8 → counter-signal.
POSITION_SENSITIVITY 3/4 → corroborates borderline finding. 1/2 → counter-signal.
GM_PUBLIC_TRUST 9/10 → corroborates borderline finding. 11 → neutral. 99/null → counter-signal.
**Do NOT trigger on structural fields alone.**

---

### Criterion 4 — Confidential
**Definition:** The position requires serving in a close confidential working relationship with
senior officials (political appointees or senior career officials) on sensitive, pre-decisional
policy matters — a relationship of trust, not just access.

TRIGGERED if duty text shows: serves as trusted personal advisor to political appointees or
SES-level officials; handles pre-decisional, deliberative-process-protected, or politically
sensitive policy materials; attends or supports non-public meetings where major policy decisions
are made; specifically designated for access to sensitive policy deliberations not shared broadly.

**Scan for:** serves as confidential advisor to Secretary/Deputy Secretary/political officials;
attends principal meetings; attends senior leadership meetings; handles pre-decisional materials;
participates in deliberative process; deliberative process privilege; pre-decisional; advises
Secretary/Deputy Secretary/political appointees on sensitive matters; access to non-public
deliberations; close advisory relationship; trusted advisor; personal staff to senior official;
serves in the Office of the Secretary or immediate staff of a political appointee.

**Do NOT trigger on:** routine access to classified information without policy deliberation
content; standard legal or technical advice to line managers without a close senior-official
relationship; occasional attendance at leadership meetings without a designated advisory role;
general advisory duties without a specific confidential relationship with a named senior official
or class of officials.

PD_MANAGER_LEVEL 2/4/5 → corroborate borderline finding. 6/7 → neutral. 8 → counter-signal.
POSITION_SENSITIVITY 3 (Critical Sensitive) or 4 (Special Sensitive) → corroborates duty-text trigger.
GM_PUBLIC_TRUST 9/10 → corroborates duty-text trigger. 11 → neutral. 99/null → counter-signal.
**Do NOT trigger on structural fields alone.**

---

### Borderline Determination Guidance

When duty language contains policy-adjacent verbs (develops, formulates, implements, advises,
coordinates, recommends) but does not clearly establish independent policy authority, ask:

1. Does the incumbent establish agency policy on behalf of senior leadership — or does leadership approve what the incumbent drafts?
2. Does the incumbent exercise substantial independent discretion over policy direction — or do recommendations go through normal review/approval chains?
3. Does the incumbent speak for the agency on major policy issues in official forums?
4. Are recommendations normally accepted with little modification (de facto policy authority)?
5. Does the incumbent resolve controversial policy matters that establish agency precedent?
6. Does the incumbent have delegated authority to approve or issue agency-wide policy?

**If NONE of these clearly favor the position** → assign BORDERLINE (not YES).
**If 3 or more clearly favor the position** → raise from BORDERLINE to MEDIUM or HIGH.

The SME standard: "develops," "implements," "coordinates," "assesses," "evaluates,"
"researches," and "recommends" describe program management — not policy authority.

---

### IS_CANDIDATE Scoring

| IS_CANDIDATE | Condition |
|---|---|
| YES | 2+ criteria clearly TRIGGERED |
| YES | 1 criterion clearly TRIGGERED with direct, unambiguous duty language |
| BORDERLINE | Policy-adjacent language present but no criterion clearly triggered |
| NO | No criterion clearly triggered |

### Rating Scoring

| Rating | IsCandidate | Condition |
|--------|-------------|-----------|
| HIGH | YES | 2 or more criteria clearly triggered |
| MEDIUM | YES | Exactly 1 criterion clearly triggered |
| BORDERLINE | BORDERLINE | Policy-adjacent language present but no criterion clearly triggered |
| LOW | NO | No qualifying language |

---

### PositionPurpose

Before evaluating criteria, write 1–2 objective sentences describing the overarching function
of this position based on its duty text. Be descriptive, not evaluative. Include this in the
JSON output as `PositionPurpose`.

---

### Output Schema

Return ONLY a single valid JSON object. No explanation, no markdown fences, no surrounding text.

{
  "PdNbr":               "{PD_NBR}",
  "OrgCode":             "{PD_ORIGIN_ORG_CODE}",
  "PositionTitle":       "{PD_POSITION_TITLE_TEXT}",
  "PayPlan":             "{GVT_PAY_PLAN}",
  "OccSeries":           "{GVT_OCC_SERIES}",
  "Grade":               "{GRD_CODE}",
  "EvalDate":            "{EvalDate}",
  "EffectiveDate":       "{EvalDate}",
  "PdManagerLevel":      "{PdManagerLevelStr}",
  "PositionSensitivity": "{PositionSensitivityLabel}",
  "PublicTrust":         "{PublicTrustLabel}",
  "ServiceCategory":     "{ServiceCategoryLabel}",
  "IsCandidate":         "YES or BORDERLINE or NO",
  "Rating":              "HIGH or MEDIUM or BORDERLINE or LOW",
  "PositionPurpose":     "1–2 sentence objective summary",
  "JustificationSummary": "≥3 sentences citing EO and specific duty language",
  "Criteria": [
    { "Name": "Policy-Determining", "Triggered": true or false, "Evidence": "verbatim quote or explicit negative finding" },
    { "Name": "Policy-Making",      "Triggered": true or false, "Evidence": "verbatim quote or explicit negative finding" },
    { "Name": "Policy-Advocating",  "Triggered": true or false, "Evidence": "verbatim quote or explicit negative finding" },
    { "Name": "Confidential",       "Triggered": true or false, "Evidence": "verbatim quote or explicit negative finding" }
  ],
  "Duties": [
    { "SeqNum": "{pdd_seq_num}", "DutyText": "{pdd_major_duties_text}", "MatchedCriteria": ["Policy-Making"] },
    { "SeqNum": "{pdd_seq_num}", "DutyText": "{pdd_major_duties_text}", "MatchedCriteria": [] }
  ]
}
```

---

## Step 4 · Call LLM

### If Scorer = "claude"

Call the Agent tool once:
- `subagent_type: "general-purpose"`
- `description: "Score PD-{PD_NBR}"`
- `prompt`: the full scoring prompt from Step 4

Wait for agent to complete, collect response text.

### If Scorer = "ollama"

```bash
PROMPT=$(cat <<'ENDPROMPT'
{full scoring prompt — all placeholders filled}
ENDPROMPT
)

curl -s {OllamaEndpoint}/api/chat \
  -H "Content-Type: application/json" \
  -d "$(jq -n --arg p "$PROMPT" '{model:"{OllamaModel}",messages:[{role:"user",content:$p}],stream:false}')" \
| python3 -c "import sys,json; print(json.load(sys.stdin)['message']['content'])"
```

Default endpoint: `http://localhost:11434`. Default model: `gemma4:latest`.

---

## Step 5 · Validate Result

Parse the LLM response as JSON. Validate:

1. Required fields present: `PdNbr`, `IsCandidate`, `Rating`, `PositionPurpose`, `JustificationSummary`, `Criteria` (array of 4), `Duties`
2. `IsCandidate` is exactly `"YES"`, `"BORDERLINE"`, or `"NO"` (uppercase)
3. `Rating` is exactly `"HIGH"`, `"MEDIUM"`, `"BORDERLINE"`, or `"LOW"` (uppercase)
4. All 4 Criteria entries use v2 names: `"Policy-Determining"`, `"Policy-Making"`, `"Policy-Advocating"`, `"Confidential"`
5. `JustificationSummary` is at least 3 sentences and cites the EO
6. Each Criterion has non-empty `Evidence` (negative finding sentence if not triggered)

**On validation failure:**

```sql
UPDATE SCHEDULE_PC_EVAL SET
    STATUS    = 'failed',
    ERROR_MSG = '{error description — max 1000 chars}',
    SCORED_AT = SYSTIMESTAMP
WHERE PD_NBR = '{PD_NBR}' AND RUN_ID = '{RunId}'
```

Return failure signal: `SCORING_FAILED:{PD_NBR}:{error}`

---

## Step 6 · Write Result to Oracle

```sql
UPDATE SCHEDULE_PC_EVAL SET
    IS_CANDIDATE              = '{IsCandidate}',
    RATING                    = '{Rating}',
    POSITION_PURPOSE          = '{PositionPurpose}',
    JUSTIFICATION_SUMMARY     = '{JustificationSummary}',
    CRIT_POLICY_DETERMINING   = '{Y or N}',
    EVID_POLICY_DETERMINING   = '{Evidence text}',
    CRIT_POLICY_MAKING        = '{Y or N}',
    EVID_POLICY_MAKING        = '{Evidence text}',
    CRIT_POLICY_ADVOCATING    = '{Y or N}',
    EVID_POLICY_ADVOCATING    = '{Evidence text}',
    CRIT_CONFIDENTIAL         = '{Y or N}',
    EVID_CONFIDENTIAL         = '{Evidence text}',
    RESULT_JSON               = '{full JSON as escaped string}',
    STATUS                    = 'done',
    SCORED_AT                 = SYSTIMESTAMP,
    ERROR_MSG                 = NULL
WHERE PD_NBR = '{PD_NBR}' AND RUN_ID = '{RunId}'
```

Map `Triggered: true` → `'Y'`, `Triggered: false` → `'N'`.

Print: `✓ PD-{PD_NBR} scored — {IsCandidate} / {Rating}`

---

Return: success (`done`) or `SCORING_FAILED:{PD_NBR}:{error}`

---

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Using old criterion names | v2 only: Policy-Determining, Policy-Making, Policy-Advocating, Confidential |
| Triggering on structural fields alone | Duty text is always primary |
| `IsCandidate` as "Yes"/"No" | Must be exactly "YES", "BORDERLINE", or "NO" |
| Empty Evidence for not-triggered criteria | Write explicit negative finding sentence |
| Short JustificationSummary | Must be ≥3 sentences with EO citation |
| Calling `schema_information` | Do NOT — schema is known |
| Over-triggering on program management verbs | develops/implements/coordinates/recommends = program management; confirm independent authority first |
| Dispatching ollama positions simultaneously | Ollama runs on single GPU — one at a time only |
