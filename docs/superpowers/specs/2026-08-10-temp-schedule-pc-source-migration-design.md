# TEMP Schedule PC Source Migration Design

## Purpose

Replace the stale Position Description source set (`MAX_PD_VW`, `PD_DUTIES`, and `PD_POSITION_DATA`) used by SchedulePCMcp with the current TEMP Schedule PC header and duty tables.

## Source Tables

`TEMP_PD_SCHED_PC` is the header source and `TEMP_PD_SCHED_PC_DUTIES` is the duty source. Join the tables on `PD_SEQ_NUM`.

| Application field | TEMP source |
|---|---|
| PD sequence number | `TEMP_PD_SCHED_PC.PD_SEQ_NUM` |
| PD number | `TEMP_PD_SCHED_PC.PD_NBR` |
| Effective date | `TEMP_PD_SCHED_PC.PD_EFFECTIVE_DATE` |
| Organization code | `TEMP_PD_SCHED_PC.PD_ORIGIN_ORG_CODE` |
| Organization name | `TEMP_PD_SCHED_PC.ORG_DESC` |
| Pay plan | `TEMP_PD_SCHED_PC.GVT_PAY_PLAN` |
| Grade | `TEMP_PD_SCHED_PC.GRD_CODE` |
| Occupational series | `TEMP_PD_SCHED_PC.GVT_OCC_SERIES` |
| Position title | `TEMP_PD_SCHED_PC.PD_POSITION_TITLE_TEXT` |
| Manager level | `TEMP_PD_SCHED_PC.PD_MANAGER_LEVEL` |
| Position sensitivity | `TEMP_PD_SCHED_PC.POSITION_SENSITIVITY` |
| Public trust | `TEMP_PD_SCHED_PC.GM_PUBLIC_TRUST` |
| Service category | `TEMP_PD_SCHED_PC.POSITION_OCCUPIED_CODE` |
| Introductory text | `TEMP_PD_SCHED_PC.PD_INTRO` |
| Duty sequence | `TEMP_PD_SCHED_PC_DUTIES.PDD_SEQ_NUM` |
| Duty percent time | `TEMP_PD_SCHED_PC_DUTIES.PDD_PERCENT_TIME_SPENT` |
| Duty text | `TEMP_PD_SCHED_PC_DUTIES.PDD_MAJOR_DUTIES_TEXT` |

`POSITION_OCCUPIED_CODE` is the current renamed equivalent of `GVT_POSN_OCCUPIED`.

## Eligibility

Bulk staging and repository filtering retain the fixed GS-13 through GS-15 scope. A TEMP header is eligible only when it has at least one related duty whose text is not null.

Headers with no eligible duty must not be staged or scored. The current source inspection found 10,340 such headers; the staging procedure must report their exclusion in logs or returned counts.

## Deliberate Omission

`TEMP_PD_SCHED_PC_DUTIES` does not contain `PDD_CRITICAL_DUTY_IND`. Map `MajorDuty.IsCritical` to `false` for all TEMP duties. Do not infer critical status from duty content or other structural fields because duty text is the primary evaluation signal and the structural fields are secondary corroboration only.

## Cutover Boundaries

The migration changes only the source-side data access:

- `OraclePositionDescriptionRepository` reads TEMP headers and duties.
- `stage_schedule_pc_eval` stages from the TEMP header table and excludes dutyless headers.
- Existing domain types, scoring, document generation, reporting, and export continue to consume `PositionDescription` and `EvaluationResult` unchanged.

## Validation

Automated coverage must verify TEMP field mapping, the dutyless-header exclusion, the `IsCritical = false` fallback, and GS-13 through GS-15 filtering. Deployment validation must compare the staged count to a direct TEMP-source eligibility count and confirm every staged `PD_SEQ_NUM` comes from the TEMP header source.