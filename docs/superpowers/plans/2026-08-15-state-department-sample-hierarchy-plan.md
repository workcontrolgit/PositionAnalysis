# State Department Sample Hierarchy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Populate all `TEMP_PD_SCHED_PC` headers with deterministic synthetic State Department Bureau and six-digit child Organization values.

**Architecture:** Use a single guarded Oracle `MERGE` keyed by `PD_SEQ_NUM`. The source expression assigns each header to one of five synthetic Bureau records using `MOD(PD_SEQ_NUM, 5)`, and derives a six-digit child organization code from the selected two-digit Bureau prefix plus a nonzero four-digit suffix. Existing nonblank values are preserved.

**Tech Stack:** Oracle Database 21c, SQLcl MCP, `TEMP_PD_SCHED_PC`.

---

## File Structure

- Modify: Oracle table `HR.TEMP_PD_SCHED_PC` -- stores generated Bureau and Organization sample values.
- Create: no application files -- the change is data-only.

### Task 1: Verify the update scope

- [ ] **Step 1: Check how many headers need each field populated**

Run through the SQLcl MCP connection `hr_local`:

```sql
SELECT COUNT(*) AS total_rows,
       SUM(CASE WHEN bureau_code IS NULL OR TRIM(bureau_code) IS NULL THEN 1 ELSE 0 END) AS missing_bureau_code,
       SUM(CASE WHEN bureau_desc IS NULL OR TRIM(bureau_desc) IS NULL THEN 1 ELSE 0 END) AS missing_bureau_desc,
       SUM(CASE WHEN pd_origin_org_code IS NULL OR TRIM(pd_origin_org_code) IS NULL
                     OR NOT REGEXP_LIKE(TRIM(pd_origin_org_code), '^\d{6}$')
                THEN 1 ELSE 0 END) AS invalid_org_code,
       SUM(CASE WHEN org_desc IS NULL OR TRIM(org_desc) IS NULL THEN 1 ELSE 0 END) AS missing_org_desc
FROM temp_pd_sched_pc;
```

Expected: `TOTAL_ROWS = 5005`; the source data is eligible for the guarded sample-data update.

### Task 2: Apply the deterministic sample hierarchy

- [ ] **Step 1: Run the guarded sample-data MERGE**

Run through the SQLcl MCP connection `hr_local`:

```sql
MERGE INTO temp_pd_sched_pc target
USING (
    SELECT pd_seq_num,
           CASE MOD(pd_seq_num, 5)
               WHEN 0 THEN '100000'
               WHEN 1 THEN '200000'
               WHEN 2 THEN '300000'
               WHEN 3 THEN '400000'
               WHEN 4 THEN '500000'
           END AS bureau_code,
           CASE MOD(pd_seq_num, 5)
               WHEN 0 THEN 'Bureau of African Affairs'
               WHEN 1 THEN 'Bureau of East Asian and Pacific Affairs'
               WHEN 2 THEN 'Bureau of European and Eurasian Affairs'
               WHEN 3 THEN 'Bureau of Near Eastern Affairs'
               WHEN 4 THEN 'Bureau of Western Hemisphere Affairs'
           END AS bureau_desc,
           CASE MOD(pd_seq_num, 5)
               WHEN 0 THEN '10'
               WHEN 1 THEN '20'
               WHEN 2 THEN '30'
               WHEN 3 THEN '40'
               WHEN 4 THEN '50'
           END || LPAD(TO_CHAR(MOD(pd_seq_num - 1, 9999) + 1), 4, '0') AS org_code,
           'Sample Office ' || LPAD(TO_CHAR(MOD(pd_seq_num - 1, 9999) + 1), 4, '0') AS org_desc
    FROM temp_pd_sched_pc
) source
ON (target.pd_seq_num = source.pd_seq_num)
WHEN MATCHED THEN UPDATE SET
    target.bureau_code = CASE
        WHEN target.bureau_code IS NULL OR TRIM(target.bureau_code) IS NULL
        THEN source.bureau_code ELSE target.bureau_code END,
    target.bureau_desc = CASE
        WHEN target.bureau_desc IS NULL OR TRIM(target.bureau_desc) IS NULL
        THEN source.bureau_desc ELSE target.bureau_desc END,
    target.pd_origin_org_code = CASE
        WHEN target.pd_origin_org_code IS NULL
          OR TRIM(target.pd_origin_org_code) IS NULL
          OR NOT REGEXP_LIKE(TRIM(target.pd_origin_org_code), '^\d{6}$')
        THEN source.org_code ELSE target.pd_origin_org_code END,
    target.org_desc = CASE
        WHEN target.org_desc IS NULL OR TRIM(target.org_desc) IS NULL
        THEN source.org_desc ELSE target.org_desc END
WHERE target.bureau_code IS NULL
   OR TRIM(target.bureau_code) IS NULL
   OR target.bureau_desc IS NULL
   OR TRIM(target.bureau_desc) IS NULL
   OR target.pd_origin_org_code IS NULL
   OR TRIM(target.pd_origin_org_code) IS NULL
   OR NOT REGEXP_LIKE(TRIM(target.pd_origin_org_code), '^\d{6}$')
   OR target.org_desc IS NULL
   OR TRIM(target.org_desc) IS NULL;
```

Expected: 5,005 rows merged. Autocommit is enabled for the saved local connection.

### Task 3: Validate hierarchy integrity

- [ ] **Step 1: Verify population, code format, and parent-child prefixes**

Run through the SQLcl MCP connection `hr_local`:

```sql
SELECT COUNT(*) AS total_rows,
       COUNT(bureau_code) AS bureau_code_populated,
       COUNT(bureau_desc) AS bureau_desc_populated,
       COUNT(pd_origin_org_code) AS org_code_populated,
       COUNT(org_desc) AS org_desc_populated,
       SUM(CASE WHEN REGEXP_LIKE(pd_origin_org_code, '^\d{6}$') THEN 1 ELSE 0 END) AS six_digit_org_codes,
       SUM(CASE WHEN SUBSTR(pd_origin_org_code, 1, 2) = SUBSTR(bureau_code, 1, 2) THEN 1 ELSE 0 END) AS matching_parent_prefixes,
       COUNT(DISTINCT bureau_code) AS assigned_bureaus
FROM temp_pd_sched_pc;
```

Expected: all population and integrity counts equal `5,005`; `ASSIGNED_BUREAUS = 5`.

- [ ] **Step 2: Verify the generated sample distribution**

```sql
SELECT bureau_code,
       bureau_desc,
       COUNT(*) AS header_count,
       MIN(pd_origin_org_code) AS first_org_code,
       MAX(pd_origin_org_code) AS last_org_code
FROM temp_pd_sched_pc
GROUP BY bureau_code, bureau_desc
ORDER BY bureau_code;
```

Expected: exactly five deterministic Bureau rows, each with a nonzero `HEADER_COUNT`.

### Task 4: Regenerate affected Word documents

- [ ] **Step 1: Run the MCP document-generation command**

```text
generate_documents_all
```

Expected: regenerated documents show a Bureau, a six-digit Organization Code,
Position Sensitivity, and Public Trust for each completed evaluation.

## Plan Self-Review

- Spec coverage: Tasks 1-3 cover all five Bureaus, six-digit child Organization values, blank-only guards, deterministic assignment, and full-table validation. Task 4 refreshes rendered output.
- Placeholder scan: no incomplete steps or unspecified commands remain.
- Consistency: the same `MOD(PD_SEQ_NUM, 5)` mapping and two-digit parent prefix are used throughout.