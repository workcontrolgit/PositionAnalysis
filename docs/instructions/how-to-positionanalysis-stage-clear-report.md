# How To: Stage PDs, Clear PDs, and Show Staging Report (Position Analysis)

This guide shows how to use the SchedulePC chat client to:

- Stage PD records into `SCHEDULE_PC_EVAL`
- Clear all staged records
- Show the staging report

## 1) Start the chat client

From the repository root (`C:\apps\oracle`):

```powershell
dotnet run --project src/schedulepc/SchedulePC.csproj
```

You should see chat mode with a prompt like:

- `You ›`

## 2) Stage PD records

Type one of these commands in chat.

Stage by series:

```text
stage series 0110
```

Stage by grade range:

```text
stage grade 13-15
```

Stage by series + grade + org:

```text
stage series 0110 grade 13-15 org ABC123
```

Expected result includes:

- `Staging complete. Run: RUN_YYYYMMDD_HHMMSS`
- A staging summary table

## 3) Show staging report

In chat, run:

```text
show stage report
```

You can also use:

```text
staging report
```

Expected columns:

- SERIES
- STAGED
- IN_PROGRESS
- COMPLETE
- FAILED

## 4) Clear all staged PD records

In chat, run:

```text
remove all records
```

You can also use:

```text
clear schedule pc eval
```

Expected result:

- `SCHEDULE_PC_EVAL cleared. Deleted rows: <number>`

## 5) Typical workflow example

```text
remove all records
stage series 0110
show stage report
exit
```

## 6) Optional database verification

After running stage/clear, you can verify row count using Oracle SQLcl MCP or SQL:

```sql
SELECT COUNT(*) AS row_count FROM schedule_pc_eval;
```

## 7) Validate TEMP staging eligibility and source identity

`stage_pds` always stages only GS-13 through GS-15 records. The grade predicate is
guarded because `TEMP_PD_SCHED_PC.GRD_CODE` can contain non-numeric values. A
header is eligible only when it has at least one matching duty with non-null
`PDD_MAJOR_DUTIES_TEXT`.

Run these queries against the same database immediately before or after staging.

Eligible TEMP headers:

```sql
SELECT COUNT(*) AS expected_eligible_headers
FROM temp_pd_sched_pc pd
WHERE CASE
		  WHEN REGEXP_LIKE(TRIM(pd.grd_code), '^[[:digit:]]+$')
		  THEN TO_NUMBER(TRIM(pd.grd_code))
	  END BETWEEN 13 AND 15
  AND EXISTS (
	  SELECT 1
	  FROM temp_pd_sched_pc_duties duty
	  WHERE duty.pd_seq_num = pd.pd_seq_num
		AND duty.pdd_major_duties_text IS NOT NULL
  );
```

Excluded headers (headers in the fixed grade range without a qualifying duty):

```sql
SELECT COUNT(*) AS expected_excluded_without_duties
FROM temp_pd_sched_pc pd
WHERE CASE
		  WHEN REGEXP_LIKE(TRIM(pd.grd_code), '^[[:digit:]]+$')
		  THEN TO_NUMBER(TRIM(pd.grd_code))
	  END BETWEEN 13 AND 15
  AND NOT EXISTS (
	  SELECT 1
	  FROM temp_pd_sched_pc_duties duty
	  WHERE duty.pd_seq_num = pd.pd_seq_num
		AND duty.pdd_major_duties_text IS NOT NULL
  );
```

When staging is filtered, add both procedure predicates to **both** queries so
the expected counts match the request:

```sql
AND (:series IS NULL OR pd.gvt_occ_series = :series)
AND (:org_code IS NULL OR pd.pd_origin_org_code = :org_code)
```

Verify that each staged row has a TEMP header source and that no staged source
sequence number is missing:

```sql
SELECT
	COUNT(*) AS staged_rows,
	COUNT(DISTINCT eval.pd_seq_num) AS distinct_staged_sequence_numbers,
	COUNT(CASE WHEN pd.pd_seq_num IS NULL THEN 1 END) AS missing_temp_source_rows
FROM schedule_pc_eval eval
LEFT JOIN temp_pd_sched_pc pd
	ON pd.pd_seq_num = eval.pd_seq_num;
```

No staged record may be backed by a header without a qualifying duty. This must
return zero:

```sql
SELECT COUNT(*) AS dutyless_staged_rows
FROM schedule_pc_eval eval
WHERE NOT EXISTS (
	SELECT 1
	FROM temp_pd_sched_pc_duties duty
	WHERE duty.pd_seq_num = eval.pd_seq_num
	  AND duty.pdd_major_duties_text IS NOT NULL
);
```

The `stage_pds` MCP response includes `stagedCount` and
`excludedWithoutDutiesCount`. For an unfiltered stage, they must equal
`expected_eligible_headers` and `expected_excluded_without_duties`, respectively.
For a filtered stage, compare them to the counts produced by the same queries
with the series and organization predicates above.

## Troubleshooting

- If you get file lock errors (`MSB3021` or `MSB3027`), stop running `SchedulePC` or `SchedulePCMcp` processes and run again.
- If stage fails with database errors, confirm table/view schema compatibility and that Oracle container is running.
