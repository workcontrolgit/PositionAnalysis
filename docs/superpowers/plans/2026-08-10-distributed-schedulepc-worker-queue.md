# Distributed Schedule PC Worker Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow any number of scheduled SchedulePC workers to claim and score different Oracle `SCHEDULE_PC_EVAL` rows safely, with abandoned-work recovery.

**Architecture:** Oracle is the shared work queue. A worker atomically claims one `PENDING` row using `FOR UPDATE SKIP LOCKED`, records a worker-owned fifteen-minute lease, scores it, and persists the result only if it still owns the claim. `SchedulePC --process-all` starts the MCP child, invokes the existing tool, polls status to completion, and exits with a scheduler-friendly code.

**Tech Stack:** .NET 8 SchedulePCMcp, .NET 10 SchedulePC, Oracle Managed Data Access, Oracle SQL, xUnit, Serilog.

---

## File Structure

- Create: `src/SchedulePCMcp/Database/ADD_SCHEDULE_PC_WORK_QUEUE.sql` — idempotent Oracle schema upgrade for claim metadata and lookup index.
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/IRepositories.cs` — queue-claim and queue-summary contracts.
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/OracleSchedulePCEvalRepository.cs` — transactional claim/recover/owned-completion SQL.
- Modify: `src/SchedulePCMcp/Application/Services/ScoringOrchestrator.cs` — claim-and-score loop for `ScoreAllAsync` while retaining desktop series processing.
- Create: `src/SchedulePCMcp/Domain/Entities/QueueStatus.cs` — immutable aggregate used for scheduler polling and exit codes.
- Modify: `src/SchedulePCMcp/MCP/Tools/ProcessAllPdsToolHandler.cs` — preserve desktop asynchronous invocation but rely on the claim loop.
- Modify: `src/schedulepc/Program.cs` — parse `--process-all`, call MCP, poll status, emit stable logs, and return scheduler exit codes.
- Create: `src/SchedulePCMcp.Tests/ScoringOrchestratorQueueTests.cs` — queue-loop and lease-recovery behavior tests using fakes.
- Modify: `src/SchedulePCMcp.Tests/ExportOrchestratorTests.cs` and `src/SchedulePCMcp.Tests/StagingOrchestratorTests.cs` — add unused queue methods to existing repository fakes.
- Create: `docs/how-to-run-schedulepc-workers.md` — deployment, Task Scheduler, scale-out, and recovery operations.

### Task 1: Add the Oracle Queue Schema Upgrade

**Files:**
- Create: `src/SchedulePCMcp/Database/ADD_SCHEDULE_PC_WORK_QUEUE.sql`

- [ ] **Step 1: Create the migration script**

```sql
BEGIN
    EXECUTE IMMEDIATE 'ALTER TABLE schedule_pc_eval ADD (
        worker_id VARCHAR2(128),
        claimed_at TIMESTAMP,
        lease_expires_at TIMESTAMP
    )';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -1430 THEN
            RAISE;
        END IF;
END;
/

BEGIN
    EXECUTE IMMEDIATE 'CREATE INDEX ix_schedule_pc_eval_queue
        ON schedule_pc_eval (status, lease_expires_at, pd_seq_num)';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -955 THEN
            RAISE;
        END IF;
END;
/
```

- [ ] **Step 2: Validate the script syntax against DEV1 before application release**

Run:

```powershell
& 'C:\Users\NguyenFD\.vscode\extensions\oracle.sql-developer-26.2.0-win32-x64\dbtools\sqlcl\bin\sql.exe' -name ACRS '@src/SchedulePCMcp/Database/ADD_SCHEDULE_PC_WORK_QUEUE.sql'
```

Expected: both columns and `IX_SCHEDULE_PC_EVAL_QUEUE` exist; rerunning the script succeeds without duplicate-object errors.

### Task 2: Define Queue Contracts and Failing Orchestrator Tests

**Files:**
- Create: `src/SchedulePCMcp/Domain/Entities/QueueStatus.cs`
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/IRepositories.cs`
- Create: `src/SchedulePCMcp.Tests/ScoringOrchestratorQueueTests.cs`
- Modify: `src/SchedulePCMcp.Tests/ExportOrchestratorTests.cs`
- Modify: `src/SchedulePCMcp.Tests/StagingOrchestratorTests.cs`

- [ ] **Step 1: Write the failing queue-loop test**

Create a repository fake that returns two deterministic claims and then `null`; track worker IDs passed to every claim and owned completion. Assert `ScoreAllAsync` first calls recovery, claims until empty, and completes each claimed PD with one stable worker ID.

```csharp
[Fact]
public async Task ScoreAllAsync_RecoversExpiredClaimsAndScoresEachClaimedPdOnce()
{
    var queue = new ClaimingEvaluationRepository(
        CreatePending("PD-1"), CreatePending("PD-2"));
    var orchestrator = CreateOrchestrator(queue, new SuccessfulAiClient());

    await orchestrator.ScoreAllAsync();

    Assert.Equal(1, queue.RecoverExpiredClaimsCallCount);
    Assert.Equal(new[] { "PD-1", "PD-2" }, queue.CompletedPdNbrs);
    Assert.Single(queue.WorkerIds);
}
```

Add to `ISchedulePCEvalRepository`:

```csharp
Task<int> RecoverExpiredClaimsAsync();
Task<EvaluationResult?> ClaimNextPendingAsync(string workerId, TimeSpan leaseDuration);
Task<bool> CompleteClaimAsync(EvaluationResult result, string workerId);
Task<QueueStatus> GetQueueStatusAsync();
```

Create the queue status type:

```csharp
namespace SchedulePCMcp.Domain.Entities;

public sealed record QueueStatus(int Pending, int InProgress, int Complete, int Failed)
{
    public bool IsDrained => Pending == 0 && InProgress == 0;
}
```

- [ ] **Step 2: Run the new test to verify it fails**

Run:

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~ScoringOrchestratorQueueTests"
```

Expected: FAIL because `ScoreAllAsync` still enumerates series and the queue contract is not implemented.

- [ ] **Step 3: Update existing test fakes to compile with the new interface**

Add these unused implementations to both existing `ISchedulePCEvalRepository` fakes:

```csharp
public Task<int> RecoverExpiredClaimsAsync() => throw new NotSupportedException();
public Task<EvaluationResult?> ClaimNextPendingAsync(string workerId, TimeSpan leaseDuration) => throw new NotSupportedException();
public Task<bool> CompleteClaimAsync(EvaluationResult result, string workerId) => throw new NotSupportedException();
public Task<QueueStatus> GetQueueStatusAsync() => throw new NotSupportedException();
```

### Task 3: Implement Oracle Claim, Recovery, and Owned Completion

**Files:**
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/OracleSchedulePCEvalRepository.cs`
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/IRepositories.cs`

- [ ] **Step 1: Implement expired-claim recovery**

Add `RecoverExpiredClaimsAsync` with this SQL:

```sql
UPDATE schedule_pc_eval
SET status = 'PENDING',
    worker_id = NULL,
    claimed_at = NULL,
    lease_expires_at = NULL
WHERE status = 'IN_PROGRESS'
  AND lease_expires_at <= SYSTIMESTAMP
```

Return the affected row count and log it at warning level when non-zero.

- [ ] **Step 2: Implement transactional single-row claiming**

Open an Oracle connection and transaction. Select one row with:

```sql
SELECT pd_nbr, series, TO_NUMBER(grade) AS grade
FROM schedule_pc_eval
WHERE status = 'PENDING'
ORDER BY pd_seq_num
FETCH FIRST 1 ROW ONLY
FOR UPDATE SKIP LOCKED
```

If no row is available, return `null`. Otherwise update the selected row in the same transaction:

```sql
UPDATE schedule_pc_eval
SET status = 'IN_PROGRESS',
    worker_id = :workerId,
    claimed_at = SYSTIMESTAMP,
    lease_expires_at = SYSTIMESTAMP + NUMTODSINTERVAL(:leaseSeconds, 'SECOND')
WHERE pd_nbr = :pdNbr
  AND series = :series
  AND status = 'PENDING'
```

Require exactly one affected row, commit, and return an `EvaluationResult` containing the claimed identity. Roll back and throw for any unexpected update count.

- [ ] **Step 3: Implement owned completion and queue summary**

`CompleteClaimAsync` must update only the owner’s in-progress claim:

```sql
UPDATE schedule_pc_eval
SET status = :status,
    rating = :rating,
    is_candidate = :isCandidate,
    justification_summary = :justification,
    result_json = :resultJson,
    scored_at = SYSTIMESTAMP,
    error_msg = :errorMsg,
    worker_id = NULL,
    claimed_at = NULL,
    lease_expires_at = NULL
WHERE pd_nbr = :pdNbr
  AND series = :series
  AND status = 'IN_PROGRESS'
  AND worker_id = :workerId
```

Return `true` only when one row is updated. `GetQueueStatusAsync` returns counts from a single aggregate query using the existing status meanings: `PENDING`, `IN_PROGRESS`, terminal completed values, and `FAILED`/`GENERATION_FAILED`.

- [ ] **Step 4: Run the focused test to verify the repository contract compiles**

Run:

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~ScoringOrchestratorQueueTests"
```

Expected: still FAIL because `ScoringOrchestrator.ScoreAllAsync` has not switched to claims.

### Task 4: Replace Process-All Enumeration With a Claim-and-Score Loop

**Files:**
- Modify: `src/SchedulePCMcp/Application/Services/ScoringOrchestrator.cs`
- Modify: `src/SchedulePCMcp.Tests/ScoringOrchestratorQueueTests.cs`

- [ ] **Step 1: Add the failing ownership-loss test**

Make the fake return `false` from `CompleteClaimAsync` and assert the orchestrator logs the lost claim but does not call the non-owned `UpdateAsync` path.

```csharp
[Fact]
public async Task ScoreAllAsync_DoesNotOverwriteAClaimOwnedByAnotherWorker()
{
    var queue = new ClaimingEvaluationRepository(CreatePending("PD-1"))
    {
        CompleteClaimResult = false
    };

    await CreateOrchestrator(queue, new SuccessfulAiClient()).ScoreAllAsync();

    Assert.Equal(1, queue.CompleteClaimCallCount);
    Assert.Equal(0, queue.UpdateAsyncCallCount);
}
```

- [ ] **Step 2: Run the ownership test to verify it fails**

Run:

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~ScoringOrchestratorQueueTests"
```

Expected: FAIL because `ScoreAllAsync` still calls `ScoreBySeriesAsync` and never completes an owned claim.

- [ ] **Step 3: Implement the worker loop**

Replace `ScoreAllAsync` with the following control flow while retaining `ScoreBySeriesAsync` and `ScoreAsync` for desktop/manual tools:

```csharp
public async Task ScoreAllAsync()
{
    const int leaseMinutes = 15;
    var workerId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    var recovered = await _evalRepository.RecoverExpiredClaimsAsync();
    _logger.LogInformation("Worker {WorkerId} recovered {Recovered} expired claims", workerId, recovered);

    while (true)
    {
        var claim = await _evalRepository.ClaimNextPendingAsync(workerId, TimeSpan.FromMinutes(leaseMinutes));
        if (claim is null)
            return;

        await ScoreClaimAsync(claim, workerId);
    }
}
```

`ScoreClaimAsync` fetches the full `PositionDescription`, invokes the existing AI/prompt/parser logic, and calls `CompleteClaimAsync` for both success and failure. If `CompleteClaimAsync` returns `false`, log the lost claim and do not invoke `UpdateAsync`.

- [ ] **Step 4: Run the queue tests to verify they pass**

Run:

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~ScoringOrchestratorQueueTests"
```

Expected: PASS. The tests demonstrate recovery precedes claiming, no row is scored twice by one worker, and a lost claim is not overwritten.

### Task 5: Add the Unattended SchedulePC Entry Point

**Files:**
- Modify: `src/schedulepc/Program.cs`
- Create: `src/schedulepc/ProcessAllRunner.cs`
- Create: `src/schedulepc.Tests/SchedulePC.Tests.csproj`
- Create: `src/schedulepc.Tests/ProcessAllRunnerTests.cs`

- [ ] **Step 1: Write failing exit-code tests**

Extract polling into a `ProcessAllRunner` accepting an MCP-client interface, delay function, and logger. Test that it calls `process_all_pds`, polls until the aggregate status is drained, returns `0` with no failures, and returns `1` with final failures.

```csharp
[Fact]
public async Task RunAsync_ReturnsZeroWhenQueueDrainsWithoutFailures()
{
    var client = new RecordingMcpClient(
        QueueStatus(3, 0, 0, 0),
        QueueStatus(0, 0, 3, 0));

    var exitCode = await new ProcessAllRunner(client, NoDelayAsync, NullLogger<ProcessAllRunner>.Instance).RunAsync();

    Assert.Equal(0, exitCode);
    Assert.Equal(new[] { "process_all_pds", "get_queue_status", "get_queue_status" }, client.Calls);
}
```

- [ ] **Step 2: Run the new runner tests to verify they fail**

Run:

```powershell
dotnet test src/schedulepc.Tests/SchedulePC.Tests.csproj --filter "FullyQualifiedName~ProcessAllRunnerTests"
```

Expected: FAIL because `ProcessAllRunner` and the `get_queue_status` MCP tool do not exist.

- [ ] **Step 3: Add queue-status MCP support and runner implementation**

Add a `get_queue_status` tool that returns:

```csharp
return new
{
    pending = status.Pending,
    inProgress = status.InProgress,
    complete = status.Complete,
    failed = status.Failed,
    isDrained = status.IsDrained
};
```

In top-level `Program.cs`, branch before constructing `SchedulePCChatClient`:

```csharp
if (args.SequenceEqual(["--process-all"], StringComparer.OrdinalIgnoreCase))
{
    var runner = new ProcessAllRunner(mcpClient, Task.Delay, Log.Logger);
    Environment.ExitCode = await runner.RunAsync();
    return;
}
```

`ProcessAllRunner.RunAsync` calls `process_all_pds`, polls `get_queue_status` every 30 seconds while `isDrained` is false, logs each summary, and returns `1` when the drained response contains one or more failed rows.

- [ ] **Step 4: Run the runner tests to verify they pass**

Run:

```powershell
dotnet test src/schedulepc.Tests/SchedulePC.Tests.csproj --filter "FullyQualifiedName~ProcessAllRunnerTests"
```

Expected: PASS.

### Task 6: Document Deployment and Run Full Validation

**Files:**
- Create: `docs/how-to-run-schedulepc-workers.md`

- [ ] **Step 1: Document migration and Task Scheduler deployment**

Include these exact operational rules:

- Deploy the same published `SchedulePC` and `SchedulePCMcp` build to every worker machine.
- Run `ADD_SCHEDULE_PC_WORK_QUEUE.sql` once against the shared Oracle schema before enabling a second worker.
- Configure each task to run `SchedulePC.exe --process-all` under an identity with Oracle and Azure OpenAI access.
- Set the task’s Start In directory to the deployment root and do not run staging, document generation, or export on workers.
- Start with one worker per machine. Add workers only after confirming Azure OpenAI quota and reviewing Serilog logs.
- Inspect `IN_PROGRESS` rows and lease timestamps when a worker is interrupted; the next run recovers only expired claims.

- [ ] **Step 2: Run focused suites**

Run:

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~ScoringOrchestratorQueueTests"
dotnet test src/schedulepc.Tests/SchedulePC.Tests.csproj --filter "FullyQualifiedName~ProcessAllRunnerTests"
```

Expected: both commands pass.

- [ ] **Step 3: Run complete validation**

Run:

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj
dotnet test src/schedulepc.Tests/SchedulePC.Tests.csproj
dotnet build src/SchedulePCMcp/SchedulePCMcp.csproj --no-restore
dotnet build src/schedulepc/SchedulePC.csproj --no-restore
git diff --check
```

Expected: all tests and both builds pass; no whitespace errors.

- [ ] **Step 4: Validate two-worker behavior on Oracle**

Stage a deliberately small test set, start `SchedulePC.exe --process-all` on two machines, and verify:

```sql
SELECT pd_nbr, COUNT(*)
FROM schedule_pc_eval
WHERE status IN ('COMPLETE', 'FAILED')
GROUP BY pd_nbr
HAVING COUNT(*) > 1;
```

Expected: no rows. Also verify all test rows reach a terminal status and no unexpired `IN_PROGRESS` claim is owned by two workers.

## Plan Self-Review

- **Spec coverage:** Tasks 1-4 implement exclusive claims, lease recovery, ownership protection, and worker-loop behavior. Task 5 adds the scheduler entry point and completion polling. Task 6 covers deployment and single/multi-worker validation.
- **Placeholder scan:** No TBD/TODO markers or unspecified implementation steps remain.
- **Type consistency:** Repository methods use `EvaluationResult`, `QueueStatus`, worker ID, and lease duration consistently. The scheduled client consumes the `get_queue_status` tool defined in Task 5.