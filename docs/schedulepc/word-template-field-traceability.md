# Schedule PC Word Template — Field Traceability Matrix

**Purpose:** Maps every field in the generated Word evaluation document
(`Schedule_PC_Position_Evaluation_Template_Updated.docx`) back to its data
source — Oracle backend (`TEMP_PD_SCHED_PC` / `TEMP_PD_SCHED_PC_DUTIES`) or
LLM-generated output — so testers/QA can cross-reference report values against
the source system or the model response without reading code.

**Code entry point:** [`OpenXmlDocumentStrategy.FillDocument`](../../src/SchedulePCMcp/Infrastructure/DocumentGeneration/OpenXmlDocumentStrategy.cs)
fills the template's SDT (content control) fields by ordinal position, in document order.

## Legend

| Source | Meaning |
|---|---|
| **Backend** | Read directly from Oracle `TEMP_PD_SCHED_PC` (or `_DUTIES`) via [`OraclePositionDescriptionRepository`](../../src/SchedulePCMcp/Infrastructure/Repositories/OraclePositionDescriptionRepository.cs) — not evaluative, not touched by the LLM. |
| **LLM** | Produced by the scoring model from the prompt in [`ScoringOrchestrator.GenerateEvaluationPrompt`](../../src/SchedulePCMcp/Application/Services/ScoringOrchestrator.cs) and parsed by `ParseLlmResponse`. |
| **Derived** | Computed in code from Backend and/or LLM values (formatting, lookups) — no independent source of its own. |
| **Static** | Hard-coded literal in `OpenXmlDocumentStrategy` (not sourced from data). |

## Section 1: Position Overview

| SDT # | Field | Source | Origin |
|---|---|---|---|
| 0 | PD Number | Backend | `EvaluationResult.PdNbr` ← `TEMP_PD_SCHED_PC.PD_NBR` |
| 1 | Effective Date | Backend | `EvaluationResult.EvaluatedDate` (evaluation run date, **not** the PD's effective date — see Appendix A #35 for `PD_EFFECTIVE_DATE`) |
| 2 | Position Title | Backend | `TEMP_PD_SCHED_PC.PD_POSITION_TITLE_TEXT` |
| 3 | Schedule PC Rating | LLM | `EvaluationResult.Rating` + `CriteriaScores` triggered count (`"{Rating} -- {N} of 4 criteria met"`) |
| 4 | Bureau/Org Code | Derived | `(BUREAU_CODE) BUREAU_DESC / (PD_ORIGIN_ORG_CODE) ORG_DESC`, from `TEMP_PD_SCHED_PC.BUREAU_CODE`/`BUREAU_DESC` and `PD_ORIGIN_ORG_CODE`/`ORG_DESC` |
| 5 | Pay Plan / Series / Grade | Backend | `GVT_PAY_PLAN`-`GVT_OCC_SERIES`-`GRD_CODE` |
| 6 | Position Occupied Code | Static | Literal `"Competitive"` — not sourced from `POSITION_OCCUPIED_CODE` |
| 7 | Position Purpose | LLM (fallback: Backend) | `EvaluationResult.PositionPurpose` — LLM-generated 1-2 sentence summary of `PD_INTRO` + major duties. Falls back to raw `TEMP_PD_SCHED_PC.PD_INTRO` for results scored before `positionPurpose` was added to the prompt. |

## Section 2: Criteria Analysis (repeats per criterion, 4x)

Order: Policy-Determining, Policy-Making, Policy-Advocating, Confidential.

| SDT # (per criterion, i=0..3) | Field | Source | Origin |
|---|---|---|---|
| `i*3+8` | Checkbox — Yes (triggered) | LLM | `CriterionScore.Triggered == true` |
| `i*3+9` | Checkbox — No (not triggered) | LLM | `CriterionScore.Triggered == false` |
| `i*3+10` | Evidence | LLM | `CriterionScore.Evidence` — quoted/paraphrased duty language the model cited for this criterion |

SDTs 20–21 are reserved/unused spacer controls.

## Section 3: Final Recommendation & Sign-Off

| SDT # | Field | Source | Origin |
|---|---|---|---|
| 22 | Checkbox — Convert to Schedule P/C | LLM | `EvaluationResult.IsCandidate == true` |
| 23 | Checkbox — Retain in Current Schedule | LLM | `EvaluationResult.IsCandidate == false` |
| 24 | Schedule PC Rating | LLM | Same as SDT #3 |
| 25 | Justification Summary | LLM | `EvaluationResult.JustificationSummary` — one-paragraph overall rationale from the model |
| 26 | Evaluator Name & Date | *(not populated)* | Left as the template placeholder for manual human entry — the app does **not** write "AI Agent" here |
| 27 | Agency Head Approving Official | Static | Literal `"(Pending Human Review)"` |

## Appendix A: Position Description Data

| SDT # | Field | Source | Origin |
|---|---|---|---|
| 28 | PD Number | Backend | Same as SDT #0 |
| 29 | Position Title | Backend | Same as SDT #2 |
| 30 | Bureau | Derived | `(BUREAU_CODE) BUREAU_DESC` |
| 31 | Org Code | Derived | `(PD_ORIGIN_ORG_CODE) ORG_DESC` |
| 32 | Pay Plan | Backend | `GVT_PAY_PLAN` |
| 33 | Job Series | Backend | `GVT_OCC_SERIES` |
| 34 | Grade | Backend | `GRD_CODE` |
| 35 | Effective Date | Backend | `TEMP_PD_SCHED_PC.PD_EFFECTIVE_DATE`, formatted `yyyy-MM-dd` when parseable |
| 36 | PD Manager Level | Derived | `PD_MANAGER_LEVEL` code + label lookup (`2`=Supervisor or Manager, `4`=Supervisor (CSRA), `5`=Management Official (CSRA), `6`=Leader, `7`=Team Leader, `8`=All Other Positions) |
| 37 | Position Sensitivity | Derived | `POSITION_SENSITIVITY` code + label lookup (`1`=Non-Sensitive, `2`=Non-Critical Sensitive, `3`=Critical Sensitive, `4`=Special Sensitive) |
| 38 | Public Trust | Derived | `GM_PUBLIC_TRUST` code + label lookup (`9`=High Risk, `10`=Mod Risk, `11`=Low Risk, `99`=No Risk) |
| 39 | Service Category | Static | Literal `"Competitive"` — not sourced from `POSITION_OCCUPIED_CODE` |

## Appendix B: Duties and Justification

One row per duty, cloned from the template row at SDT #40/#41.

| Column | Source | Origin |
|---|---|---|
| Duty (Verbatim) | Backend | `TEMP_PD_SCHED_PC_DUTIES.PDD_MAJOR_DUTIES_TEXT`, prefixed `"Duty #{PDD_SEQ_NUM}: "` |
| Justification | LLM | For each triggered `CriterionScore`, attributed to this row only if `SupportingDutyNumbers` contains this duty's sequence number. Shows `"No direct support finding."` if no triggered criterion names this duty. Results scored before `supportingDutyNumbers` existed fall back to fuzzy evidence-text matching against the duty text (substring or ≥60% significant-word overlap) — see `DutySupportsCriterion` in `OpenXmlDocumentStrategy.cs`. |

## LLM Configuration

Settings live in [`appsettings.json`](../../src/SchedulePCMcp/appsettings.json) under `AiProvider`, bound to
[`AzureOpenAiSettings`/`OllamaSettings`](../../src/SchedulePCMcp/Infrastructure/Config/SettingsClasses.cs)
and consumed by [`AiClients.cs`](../../src/SchedulePCMcp/Infrastructure/AiClients/AiClients.cs).

| Setting | Purpose |
|---|---|
| `AiProvider.Type` | Selects which client is used (`AzureOpenAI` or `Ollama`). Currently `AzureOpenAI`. |
| `AzureOpenAI.Endpoint` | Azure OpenAI resource URL the scoring request is sent to. |
| `AzureOpenAI.DeploymentName` | Model deployment used for scoring (e.g. `gpt-5.1`). Determines the model's reasoning/quality and cost. |
| `AzureOpenAI.MaxCompletionTokens` | Caps the length of the model's JSON response; too low can truncate `criteria`/`positionPurpose` and cause JSON parse failures. |
| `AzureOpenAI.Temperature` | Sampling temperature (currently `0.2`) — lower values make triggered/not-triggered calls and evidence text more deterministic/repeatable across reruns of the same PD. |
| `Ollama.Endpoint` / `Ollama.Model` / `Ollama.NumCtx` / `Ollama.Temperature` | Equivalent settings for the local Ollama fallback provider, when `AiProvider.Type` is `Ollama`. |

**Prompt/response contract:** the scoring prompt (`ScoringOrchestrator.GenerateEvaluationPrompt`) sends the PD title,
series, grade, organization, intro text, and numbered major duties, and requires the model to return a single JSON
object with `score`, `rating`, `justification`, `isCandidate`, `positionPurpose`, and a `criteria` array
(`name`, `triggered`, `evidence`, `supportingDutyNumbers`). `ParseLlmResponse` reads that JSON into `EvaluationResult`;
the full raw response is retained in `EvaluationResult.RawLlmResponse` (persisted in `SCHEDULE_PC_EVAL.RESULT_JSON`)
for audit purposes.

## How to Use This for QA

1. **Backend/Derived/Static fields**: verify against `TEMP_PD_SCHED_PC` / `TEMP_PD_SCHED_PC_DUTIES` directly in Oracle — mismatches indicate a repository mapping or formatting bug, not an LLM issue.
2. **LLM fields**: compare against `RawLlmResponse` (or `SCHEDULE_PC_EVAL.RESULT_JSON`) for the same `PD_NBR` — mismatches between the document and the stored JSON indicate a document-generation bug; mismatches between the JSON and the source duty text indicate a model/prompt issue.
3. **PDs scored before recent fixes** (effective date, position purpose, per-duty attribution) will not have `EffectiveDate`, `PositionPurpose`, or `SupportingDutyNumbers` populated and will show fallback behavior as noted above — rescore to get the newer fields.
