# TEMP Schedule PC Source Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace stale Position Description sources with `TEMP_PD_SCHED_PC` and `TEMP_PD_SCHED_PC_DUTIES` throughout SchedulePCMcp while staging and scoring only GS-13 through GS-15 headers that contain at least one nonblank duty.

**Architecture:** Keep `PositionDescription` and its consumers unchanged. Replace only the SQL inside `OraclePositionDescriptionRepository` and the deployed `stage_schedule_pc_eval` procedure. The repository maps TEMP header/duty columns into the existing domain shape and intentionally sets `MajorDuty.IsCritical` to `false`; the procedure uses `EXISTS` to exclude dutyless headers in the set-based stage insert. A `StagingResult` carries both the staged count and the eligible-scope headers excluded for missing duties back through the MCP tool.

**Tech Stack:** .NET 8, Oracle Managed Data Access, Oracle 19c, SQLcl, xUnit.

---

## Source Contract

| Domain behavior | TEMP source |
|---|---|
| Position identity | `TEMP_PD_SCHED_PC.PD_SEQ_NUM`, `PD_NBR` |
| Header fields | `PD_EFFECTIVE_DATE`, `PD_ORIGIN_ORG_CODE`, `ORG_DESC`, `GVT_PAY_PLAN`, `GRD_CODE`, `GVT_OCC_SERIES`, `PD_POSITION_TITLE_TEXT`, `PD_MANAGER_LEVEL`, `POSITION_SENSITIVITY`, `GM_PUBLIC_TRUST`, `POSITION_OCCUPIED_CODE`, `PD_INTRO` |
| Duty fields | `TEMP_PD_SCHED_PC_DUTIES.PDD_SEQ_NUM`, `PDD_PERCENT_TIME_SPENT`, `PDD_MAJOR_DUTIES_TEXT` |
| Service Category | `POSITION_OCCUPIED_CODE`, the renamed equivalent of `GVT_POSN_OCCUPIED` |
| Critical duty | No source column; map to `false` |

The current TEMP source contains 24,460 headers and 52,296 duties. It has 10,340 headers without a duty row. Do not stage or score those incomplete headers.

### Task 1: Add TEMP Source Mapping Tests

**Files:**
- Create: `src/SchedulePCMcp.Tests/TempSchedulePcSourceContractTests.cs`

- [ ] **Step 1: Write a failing TEMP source contract test**

Add a test that refers to the not-yet-created `TempSchedulePcSourceContract` type and verifies the approved mappings:

```csharp
[Fact]
public void RequiredTempColumns_DescribeTheApprovedSourceContract()
{
  Assert.Equal("TEMP_PD_SCHED_PC", TempSchedulePcSourceContract.HeaderTable);
  Assert.Equal("TEMP_PD_SCHED_PC_DUTIES", TempSchedulePcSourceContract.DutyTable);
  Assert.Equal("POSITION_OCCUPIED_CODE", TempSchedulePcSourceContract.ServiceCategoryColumn);
  Assert.False(TempSchedulePcSourceContract.HasCriticalDutyIndicator);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test src\SchedulePCMcp.Tests\SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~TempSchedulePcSourceContractTests" --artifacts-path "D:\copilot-validation\temp-source-red"
```

Expected: compilation failure because `TempSchedulePcSourceContract` does not exist.

- [ ] **Step 3: Add a small source-contract type used by the tests**

Create an internal `TempSchedulePcSourceContract` class in the test file with the source-table and mapping constants. Keep it test-only; production SQL remains explicit and parameterized.

```csharp
internal static class TempSchedulePcSourceContract
{
    public const string HeaderTable = "TEMP_PD_SCHED_PC";
    public const string DutyTable = "TEMP_PD_SCHED_PC_DUTIES";
    public const string ServiceCategoryColumn = "POSITION_OCCUPIED_CODE";
    public const bool HasCriticalDutyIndicator = false;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run the same command from Step 2.

Expected: one passing test that documents the source contract and its deliberate lack of a critical-duty column.

- [ ] **Step 5: Commit the test contract**

```powershell
git add src/SchedulePCMcp.Tests/TempSchedulePcSourceContractTests.cs
git commit -m "test: document TEMP Schedule PC source contract"
```

### Task 2: Cut Over Repository Header and Duty Queries

**Files:**
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/OraclePositionDescriptionRepository.cs`
- Modify: `src/SchedulePCMcp/Domain/Entities/PositionDescription.cs` only if `EffectiveDate` must be consumed later; otherwise leave the domain model unchanged.
- Test: `src/SchedulePCMcp.Tests/TempSchedulePcSourceContractTests.cs`

- [ ] **Step 1: Add failing SQL-text tests for the repository source switch**

Extend `TempSchedulePcSourceContractTests.cs` with a helper that walks up from `AppContext.BaseDirectory` until it finds the repository root, then reads the repository source text. This works whether `dotnet test` writes artifacts into the project or `D:\copilot-validation`:

```csharp
private static string GetRepositoryFile(string relativePath)
{
  foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
  {
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
      var candidate = Path.Combine(directory.FullName, relativePath);
      if (File.Exists(candidate))
        return candidate;

      directory = directory.Parent;
    }
  }

  throw new FileNotFoundException($"Repository file was not found: {relativePath}");
}
```

Add this test:

```csharp
[Fact]
public void Repository_UsesTempHeaderAndDutySources()
{
  var source = File.ReadAllText(GetRepositoryFile(
    "src/SchedulePCMcp/Infrastructure/Repositories/OraclePositionDescriptionRepository.cs"));

    Assert.Contains("temp_pd_sched_pc", source, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("temp_pd_sched_pc_duties", source, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("max_pd_vw", source, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("pd_position_data", source, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("pdd_critical_duty_ind", source, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the targeted test to verify it fails**

Run:

```powershell
dotnet test src\SchedulePCMcp.Tests\SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~TempSchedulePcSourceContractTests" --artifacts-path "D:\copilot-validation\temp-repository-red"
```

Expected: fail because the repository still references `MAX_PD_VW`, `PD_DUTIES`, and `PD_POSITION_DATA`.

- [ ] **Step 3: Replace all PD-number list queries with TEMP header queries**

In `OraclePositionDescriptionRepository.cs`, make every list query read from `temp_pd_sched_pc` and exclude dutyless headers with this predicate:

```sql
AND EXISTS (
    SELECT 1
    FROM temp_pd_sched_pc_duties duty
    WHERE duty.pd_seq_num = header.pd_seq_num
      AND duty.pdd_major_duties_text IS NOT NULL
)
```

Use these SQL shapes:

```sql
SELECT DISTINCT header.pd_nbr
FROM temp_pd_sched_pc header
WHERE header.gvt_occ_series = :series
  AND EXISTS (
      SELECT 1
      FROM temp_pd_sched_pc_duties duty
      WHERE duty.pd_seq_num = header.pd_seq_num
        AND duty.pdd_major_duties_text IS NOT NULL
  )
ORDER BY header.pd_nbr
```

For grade predicates retain the safe conversion already used by `GetByFilterAsync`:

```sql
CASE
    WHEN REGEXP_LIKE(TRIM(header.grd_code), '^[[:digit:]]+$')
    THEN TO_NUMBER(TRIM(header.grd_code))
END
```

Apply that conversion to `GetByGradeRangeAsync` as well; do not retain its direct `TO_NUMBER(grd_code)` call.

- [ ] **Step 4: Replace the single-header query with TEMP columns**

Use this header query in `GetByPdNbrInternalAsync`:

```sql
SELECT header.pd_seq_num,
       header.pd_nbr,
       header.pd_position_title_text,
       header.gvt_occ_series,
       header.grd_code,
       header.pd_origin_org_code,
       header.org_desc,
       header.pd_intro,
       header.gvt_pay_plan,
       header.pd_manager_level,
       header.position_sensitivity,
       header.gm_public_trust,
       header.position_occupied_code
FROM temp_pd_sched_pc header
WHERE header.pd_nbr = :pdNbr
  AND EXISTS (
      SELECT 1
      FROM temp_pd_sched_pc_duties duty
      WHERE duty.pd_seq_num = header.pd_seq_num
        AND duty.pdd_major_duties_text IS NOT NULL
  )
```

Keep the existing ordinal mapping in the `PositionDescription` initializer, changing only source semantics:

- ordinal 6 maps `ORG_DESC` to `OrganizationName`;
- ordinal 12 maps `POSITION_OCCUPIED_CODE` to `ServiceCategory`;
- `PD_POSITION_TITLE_TEXT` remains the title source.

- [ ] **Step 5: Replace the duty query and remove critical-duty inference**

Use this query:

```sql
SELECT pdd_seq_num,
       pdd_major_duties_text,
       pdd_percent_time_spent
FROM temp_pd_sched_pc_duties
WHERE pd_seq_num = :pdSeqNum
  AND pdd_major_duties_text IS NOT NULL
ORDER BY pdd_seq_num
```

Build each `MajorDuty` as follows:

```csharp
var duty = new MajorDuty
{
    SequenceNumber = dutiesReader.GetInt32(0),
    Text = dutiesReader.GetValue(1).ToString() ?? string.Empty,
    PercentTimeAllotted = dutiesReader.IsDBNull(2) ? 0m : dutiesReader.GetDecimal(2),
    IsCritical = false
};
```

Update XML comments and log messages to say `TEMP_PD_SCHED_PC` / `TEMP_PD_SCHED_PC_DUTIES`.

- [ ] **Step 6: Run focused tests to verify the repository source switch**

Run:

```powershell
dotnet test src\SchedulePCMcp.Tests\SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~TempSchedulePcSourceContractTests" --artifacts-path "D:\copilot-validation\temp-repository-green"
```

Expected: all TEMP source contract tests pass.

- [ ] **Step 7: Commit the repository cutover**

```powershell
git add src/SchedulePCMcp/Infrastructure/Repositories/OraclePositionDescriptionRepository.cs src/SchedulePCMcp.Tests/TempSchedulePcSourceContractTests.cs
git commit -m "feat: read Schedule PC positions from TEMP tables"
```

### Task 3: Cut Over Oracle Bulk Staging

**Files:**
- Modify: `src/SchedulePCMcp/Database/STAGE_SCHEDULE_PC_EVAL.sql`
- Create: `src/SchedulePCMcp/Domain/Entities/StagingResult.cs`
- Modify: `src/SchedulePCMcp/Application/Interfaces/IOrchestrators.cs`
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/IRepositories.cs`
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/OracleSchedulePCEvalRepository.cs`
- Modify: `src/SchedulePCMcp/Application/Services/StagingOrchestrator.cs`
- Modify: `src/SchedulePCMcp/MCP/Tools/StagePdsToolHandler.cs`
- Modify: `src/SchedulePCMcp.Tests/StagingOrchestratorTests.cs`
- Modify: `src/SchedulePCMcp.Tests/StagePdsToolHandlerTests.cs`
- Test: `src/SchedulePCMcp.Tests/TempSchedulePcSourceContractTests.cs`

- [ ] **Step 1: Add a failing procedure-source test**

Add this test:

```csharp
[Fact]
public void BulkStagingProcedure_UsesEligibleTempHeaders()
{
  var source = File.ReadAllText(GetRepositoryFile(
    "src/SchedulePCMcp/Database/STAGE_SCHEDULE_PC_EVAL.sql"));

    Assert.Contains("FROM temp_pd_sched_pc pd", source, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("temp_pd_sched_pc_duties", source, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("pdd_major_duties_text IS NOT NULL", source, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("FROM max_pd_vw", source, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the procedure-source test to verify it fails**

Run:

```powershell
dotnet test src\SchedulePCMcp.Tests\SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~BulkStagingProcedure" --artifacts-path "D:\copilot-validation\temp-procedure-red"
```

Expected: fail because the procedure currently selects from `max_pd_vw`.

- [ ] **Step 3: Add the result contract and its red tests**

Create this domain record:

```csharp
namespace SchedulePCMcp.Domain.Entities;

public sealed record StagingResult(int StagedCount, int ExcludedWithoutDutiesCount);
```

Change `IStagingOrchestrator.StageAsync` and `ISchedulePCEvalRepository.StageFromMaxPdAsync` to return `Task<StagingResult>`. Update the staging-orchestrator and stage-tool tests so they fail until the result flows from repository to tool. The `stage_pds` response must expose:

```json
{
  "stagedCount": 0,
  "excludedWithoutDutiesCount": 0,
  "status": "staged"
}
```

Keep `DeleteAllAsync` and its clear-before-stage ordering unchanged.

- [ ] **Step 4: Update the procedure source query and excluded count**

Replace the `FROM max_pd_vw pd` block with:

```sql
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
  )
  AND (p_series IS NULL OR pd.gvt_occ_series = p_series)
  AND (p_org_code IS NULL OR pd.pd_origin_org_code = p_org_code);
```

Add `p_excluded_count OUT PLS_INTEGER` to the procedure. Before the insert, calculate the count of headers that match the same safe GS-13 through GS-15, series, and organization filters but do not satisfy the duty `EXISTS` predicate. Assign that count to `p_excluded_count`.

Bind and map both output values in `OracleSchedulePCEvalRepository`, then log both results in `StagingOrchestrator`. Do not change the target insert list, target status literals, or clear-before-stage behavior.

- [ ] **Step 5: Run the procedure-source and result-flow tests to verify they pass**

Run the command from Step 2.

Expected: procedure-source tests and staging result-flow tests pass.

- [ ] **Step 6: Deploy and validate the procedure against DEV1**

Run:

```powershell
& "C:\Users\NguyenFD\.vscode\extensions\oracle.sql-developer-26.2.0-win32-x64\dbtools\sqlcl\bin\sql.exe" -name ACRS "@src/SchedulePCMcp/Database/STAGE_SCHEDULE_PC_EVAL.sql"
```

Then run:

```sql
SELECT status
FROM user_objects
WHERE object_type = 'PROCEDURE'
  AND object_name = 'STAGE_SCHEDULE_PC_EVAL';

SELECT line, position, text
FROM user_errors
WHERE name = 'STAGE_SCHEDULE_PC_EVAL'
ORDER BY sequence;
```

Expected: `VALID` and no error rows. Invoke `stage_pds` after deployment and confirm its returned `excludedWithoutDutiesCount` matches the filtered excluded-count validation query.

### Task 4: Validate Database Eligibility and End-to-End Staging

**Files:**
- Modify: `docs/how-to-stage-clear-report.md`
- Test: `src/SchedulePCMcp.Tests/StagePdsToolHandlerTests.cs`

- [ ] **Step 1: Add an eligibility-count validation query to the operational documentation**

Add this SQL to `docs/how-to-stage-clear-report.md`:

```sql
SELECT COUNT(*) AS eligible_headers
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

Document a companion exclusion query:

```sql
SELECT COUNT(*) AS excluded_headers_without_duties
FROM temp_pd_sched_pc pd
WHERE NOT EXISTS (
    SELECT 1
    FROM temp_pd_sched_pc_duties duty
    WHERE duty.pd_seq_num = pd.pd_seq_num
      AND duty.pdd_major_duties_text IS NOT NULL
);
```

- [ ] **Step 2: Verify grade scope remains fixed at 13 through 15**

Run:

```powershell
dotnet test src\SchedulePCMcp.Tests\SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~StagePdsToolHandlerTests" --artifacts-path "D:\copilot-validation\temp-stage-scope"
```

Expected: `StagePdsToolHandlerTests` passes and continues to force Grade 13 through Grade 15.

- [ ] **Step 3: Execute staging in a controlled database session**

Run `clear_schedule_pc_eval`, then run `stage_pds` with no series filter. Confirm the returned `stagedCount` matches `eligible_headers` from Step 1 and `excludedWithoutDutiesCount` matches the companion exclusion query after applying the same grade, series, and organization scope.

- [ ] **Step 4: Verify staged source identity**

Run:

```sql
SELECT COUNT(*) AS staged_rows,
       COUNT(DISTINCT evaluation.pd_seq_num) AS distinct_staged_sequence_numbers,
       COUNT(*) - COUNT(temp.pd_seq_num) AS missing_temp_source_rows
FROM schedule_pc_eval evaluation
LEFT JOIN temp_pd_sched_pc temp
  ON temp.pd_seq_num = evaluation.pd_seq_num;
```

Expected: `staged_rows = distinct_staged_sequence_numbers`, `missing_temp_source_rows = 0`, and `staged_rows` equals the eligibility count.

- [ ] **Step 5: Verify dutyless headers are excluded**

Run:

```sql
SELECT COUNT(*) AS dutyless_headers_staged
FROM schedule_pc_eval evaluation
JOIN temp_pd_sched_pc header
  ON header.pd_seq_num = evaluation.pd_seq_num
WHERE NOT EXISTS (
    SELECT 1
    FROM temp_pd_sched_pc_duties duty
    WHERE duty.pd_seq_num = header.pd_seq_num
      AND duty.pdd_major_duties_text IS NOT NULL
);
```

Expected: `0`.

- [ ] **Step 6: Run the full test suite and build**

Run:

```powershell
dotnet test src\SchedulePCMcp.Tests\SchedulePCMcp.Tests.csproj --artifacts-path "D:\copilot-validation\temp-source-full"
dotnet build src\SchedulePCMcp\SchedulePCMcp.csproj --artifacts-path "D:\copilot-validation\temp-source-build"
```

Expected: all tests pass. The pre-existing `NU1903` warning for `System.Text.Json` 8.0.4 may remain; no new warnings or errors are acceptable.

- [ ] **Step 7: Commit the documentation and verification work**

```powershell
git add docs/how-to-stage-clear-report.md src/SchedulePCMcp.Tests/StagePdsToolHandlerTests.cs
git commit -m "docs: document TEMP Schedule PC staging checks"
```

### Task 5: Remove Stale Source References

**Files:**
- Modify: `src/SchedulePCMcp/Application/Interfaces/IOrchestrators.cs`
- Modify: `src/SchedulePCMcp/Application/Services/StagingOrchestrator.cs`
- Modify: `src/SchedulePCMcp/Domain/Entities/PositionDescription.cs`
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/IRepositories.cs`
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/OraclePositionDescriptionRepository.cs`
- Modify: `src/SchedulePCMcp/Database/STAGE_SCHEDULE_PC_EVAL.sql`

- [ ] **Step 1: Replace stale source comments**

Change comments and XML documentation that claim the source is `MAX_PD_VW`, `PD_DUTIES`, or `PD_POSITION_DATA` to say `TEMP_PD_SCHED_PC` and `TEMP_PD_SCHED_PC_DUTIES`.

- [ ] **Step 2: Verify no stale source SQL remains in the MCP project**

Run:

```powershell
rg -n -i "max_pd_vw|pd_duties|pd_position_data|pdd_critical_duty_ind" src\SchedulePCMcp --glob "!bin/**" --glob "!obj/**" --glob "!logs/**"
```

Expected: no production-code or deployment-SQL matches. Historical documentation may retain the old table names only when explicitly describing the migration.

- [ ] **Step 3: Commit source-reference cleanup**

```powershell
git add src/SchedulePCMcp
git commit -m "chore: document TEMP Schedule PC sources"
```

## Final Acceptance Criteria

- All SchedulePCMcp source reads and bulk staging use only `TEMP_PD_SCHED_PC` and `TEMP_PD_SCHED_PC_DUTIES`.
- `POSITION_OCCUPIED_CODE` is the Service Category source.
- Stage/scoring eligibility requires GS-13 through GS-15 and at least one nonblank duty.
- Dutyless headers are never staged.
- `stage_pds` reports both `stagedCount` and `excludedWithoutDutiesCount`.
- `MajorDuty.IsCritical` is always `false`; critical status is not inferred.
- The deployed procedure is `VALID` in DEV1.
- The staged count equals the direct TEMP eligibility count and every staged sequence number exists in the TEMP header table.
- Focused source-contract tests and the full SchedulePCMcp suite pass without new warnings or errors.