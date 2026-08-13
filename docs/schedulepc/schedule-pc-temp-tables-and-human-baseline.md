# TEMP Schedule P/C Tables and Human Baseline

## Purpose

`ACRS.TEMP_PD_SCHED_PC` holds the position-description headers used for Schedule P/C analysis. `ACRS.TEMP_PD_SCHED_PC_DUTIES` holds the corresponding duty records. The header-table field `SCHEDULE_PC_IND` records the human Schedule P/C determination used as the quality-control baseline for AI evaluations.

This document reflects an Oracle SQLcl MCP query of the `ACRS` schema on 2026-08-11.

## Table Relationship

Join the tables by `PD_SEQ_NUM`:

```sql
FROM temp_pd_sched_pc header
JOIN temp_pd_sched_pc_duties duty
  ON duty.pd_seq_num = header.pd_seq_num
```

One header can have multiple duty rows. The 90 human-designated Schedule P/C headers currently all have at least one matching duty record.

| Population | Count |
|---|---:|
| Headers where `SCHEDULE_PC_IND = 'Y'` | 90 |
| Human-designated headers with one or more duty records | 90 |
| Human-designated headers with no duty records | 0 |

## `TEMP_PD_SCHED_PC`

This is the Schedule P/C header table. `SCHEDULE_PC_IND` is the source field that selects the human-designated baseline.

| Column | Oracle type | Purpose in Schedule P/C review |
|---|---|---|
| `PD_SEQ_NUM` | `NUMBER` | Header identifier; joins to duty rows. |
| `PD_NBR` | `VARCHAR2(6)` | Position description number. |
| `PD_EFFECTIVE_DATE` | `VARCHAR2(100)` | Effective-date source value. |
| `PD_ORIGIN_ORG_CODE` | `VARCHAR2(6)` | Originating organization code. |
| `ORG_DESC` | `VARCHAR2(20)` | Organization description. |
| `BUREAU_CODE` | `VARCHAR2(20)` | Bureau code. |
| `BUREAU_DESC` | `VARCHAR2(20)` | Bureau description. |
| `GVT_PAY_PLAN` | `VARCHAR2(2)` | Pay plan, such as `GS`. |
| `GRD_CODE` | `VARCHAR2(3)` | Grade code. |
| `GVT_OCC_SERIES` | `VARCHAR2(5)` | Five-character occupational series. |
| `PD_POSITION_TITLE_TEXT` | `VARCHAR2(100)` | Position title. |
| `PD_MANAGER_LEVEL` | `VARCHAR2(2)` | Manager-level source value. |
| `POSITION_SENSITIVITY` | `VARCHAR2(2)` | Position sensitivity code. |
| `POSITION_SENSITIVITY_DESC` | `VARCHAR2(100)` | Position sensitivity description. |
| `SECURITY_CLEARANCE` | `VARCHAR2(20)` | Security-clearance code. |
| `SECURITY_CLEARANCE_DESC` | `VARCHAR2(100)` | Security-clearance description. |
| `GVT_COMP_LEVEL` | `VARCHAR2(4)` | Government compensation-level source value. |
| `GM_PUBLIC_TRUST` | `VARCHAR2(3)` | Public-trust source value. |
| `POSITION_OCCUPIED_CODE` | `VARCHAR2(1)` | Service-category source value. |
| `POSITION_OCCUPIED_DESC` | `VARCHAR2(100)` | Service-category description. |
| `PD_INTRO` | `CLOB` | Position-description introduction text. |
| `SCHEDULE_PC_IND` | `VARCHAR2(1)` | Human Schedule P/C indicator; `Y` selects the QC baseline. |

## `TEMP_PD_SCHED_PC_DUTIES`

This is the duty-detail table. It supports the human and AI determination with the position's substantive duty text.

| Column | Oracle type | Purpose in Schedule P/C review |
|---|---|---|
| `PDD_SEQ_NUM` | `NUMBER` | Duty-row identifier. |
| `PD_SEQ_NUM` | `NUMBER` | Header identifier that joins to `TEMP_PD_SCHED_PC.PD_SEQ_NUM`. |
| `PDD_PERCENT_TIME_SPENT` | `NUMBER` | Percent of time assigned to the duty; may be null. |
| `PDD_MAJOR_DUTIES_TEXT` | `CLOB` | Major-duty narrative used as primary evaluation evidence. |

## Human Schedule P/C Selection

The source flag is applied directly to the header table:

```sql
SELECT pd_nbr,
       pd_seq_num,
       gvt_pay_plan,
       gvt_occ_series,
       grd_code
FROM temp_pd_sched_pc
WHERE schedule_pc_ind = 'Y'
ORDER BY gvt_occ_series, grd_code, pd_nbr;
```

The query returns 90 rows. Each row is a PD that human reviewers have flagged as Schedule P/C. This population is the affirmative baseline for evaluating AI results:

- AI result `Yes` and human flag `Y`: agreement.
- AI result `No` and human flag `Y`: discrepancy requiring QC review.
- The `SCHEDULE_PC_IND` source flag remains the human determination; it is not replaced by the AI result.

## Human-Designated Schedule P/C PDs

All records below have `SCHEDULE_PC_IND = 'Y'` and `GRD_CODE = '15'`.

| PD Number | PD Sequence | Classification |
|---|---:|---|
| D06116 | 347281 | GS-00130-15 |
| S00733 | 347349 | GS-00130-15 |
| S00779 | 347298 | GS-00130-15 |
| S00805 | 347320 | GS-00130-15 |
| S01011 | 347345 | GS-00130-15 |
| S02604 | 347287 | GS-00130-15 |
| S03857 | 347796 | GS-00130-15 |
| S07060 | 347334 | GS-00130-15 |
| S07225 | 347292 | GS-00130-15 |
| S07967 | 347257 | GS-00130-15 |
| S08282 | 347868 | GS-00130-15 |
| S08297 | 347808 | GS-00130-15 |
| S08314 | 347282 | GS-00130-15 |
| S08387 | 347278 | GS-00130-15 |
| S08393 | 347332 | GS-00130-15 |
| S08731 | 347280 | GS-00130-15 |
| S09154 | 347290 | GS-00130-15 |
| S09243 | 347254 | GS-00130-15 |
| S09295 | 347248 | GS-00130-15 |
| S09375 | 347316 | GS-00130-15 |
| S09720 | 347325 | GS-00130-15 |
| S09733 | 347805 | GS-00130-15 |
| S09862 | 347285 | GS-00130-15 |
| S09987 | 347312 | GS-00130-15 |
| S10597 | 347358 | GS-00130-15 |
| S10609 | 347283 | GS-00130-15 |
| S10724 | 347265 | GS-00130-15 |
| S11581 | 347862 | GS-00130-15 |
| S11675 | 347297 | GS-00130-15 |
| S12070 | 347294 | GS-00130-15 |
| S12531 | 347274 | GS-00130-15 |
| S13133 | 347288 | GS-00130-15 |
| S13605 | 347293 | GS-00130-15 |
| S13739 | 347858 | GS-00130-15 |
| S13981 | 347859 | GS-00130-15 |
| S14150 | 347251 | GS-00130-15 |
| S14991 | 347303 | GS-00130-15 |
| S15043 | 347306 | GS-00130-15 |
| S15332 | 347289 | GS-00130-15 |
| S15672 | 347799 | GS-00130-15 |
| S15718 | 347284 | GS-00130-15 |
| S15773 | 347318 | GS-00130-15 |
| S15865 | 347875 | GS-00130-15 |
| S16008 | 347259 | GS-00130-15 |
| S16029 | 347276 | GS-00130-15 |
| S16095 | 347322 | GS-00130-15 |
| S16118 | 347860 | GS-00130-15 |
| S16123 | 347342 | GS-00130-15 |
| S16482 | 347270 | GS-00130-15 |
| S16588 | 347307 | GS-00130-15 |
| S16794 | 347302 | GS-00130-15 |
| S16875 | 347277 | GS-00130-15 |
| S17045 | 347319 | GS-00130-15 |
| S17046 | 347340 | GS-00130-15 |
| S17212 | 347321 | GS-00130-15 |
| S17239 | 347803 | GS-00130-15 |
| S17270 | 347275 | GS-00130-15 |
| S17625 | 347863 | GS-00130-15 |
| S17699 | 347311 | GS-00130-15 |
| S17939 | 347261 | GS-00130-15 |
| S18964 | 347328 | GS-00130-15 |
| S19729 | 347873 | GS-00130-15 |
| S19913 | 347323 | GS-00130-15 |
| S20385 | 347866 | GS-00130-15 |
| S98136 | 347801 | GS-00130-15 |
| S14077 | 347877 | GS-00201-15 |
| S08846 | 347260 | GS-00301-15 |
| S10043 | 347794 | GS-00301-15 |
| S14378 | 347326 | GS-00301-15 |
| S02833 | 347347 | GS-00341-15 |
| S14736 | 347264 | GS-00343-15 |
| S16960 | 347341 | GS-00343-15 |
| S01948 | 347263 | GS-00501-15 |
| S04318 | 347806 | GS-00501-15 |
| S06326 | 347249 | GS-00501-15 |
| S06451 | 347267 | GS-00501-15 |
| S11873 | 347269 | GS-00501-15 |
| S15251 | 347348 | GS-00501-15 |
| S15317 | 347871 | GS-00501-15 |
| S78720 | 347351 | GS-00501-15 |
| S09089 | 347272 | GS-00560-15 |
| S15071 | 347315 | GS-00560-15 |
| S15072 | 347313 | GS-00560-15 |
| X00355 | 347335 | GS-00560-15 |
| S15686 | 347869 | GS-00685-15 |
| D13144 | 347414 | GS-01001-15 |
| S11853 | 347336 | GS-01035-15 |
| S17397 | 347331 | GS-01035-15 |
| D07798 | 347301 | GS-01109-15 |
| S08562 | 347262 | GS-01109-15 |