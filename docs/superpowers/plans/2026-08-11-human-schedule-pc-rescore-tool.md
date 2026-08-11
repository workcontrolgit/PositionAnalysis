# Human Schedule P/C Rescore Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an MCP tool that scores only the current 90 human-designated Schedule P/C PDs from `TEMP_PD_SCHED_PC`.

**Architecture:** A repository query returns the human-designated PD numbers from the TEMP source in deterministic order. A scoped MCP tool guards against a count other than 90 and calls existing `IScoringOrchestrator.ScoreAsync` for each returned PD without touching the general pending queue.

**Tech Stack:** .NET 8, C#, xUnit, Oracle Managed Data Access, SQLcl MCP.

---

### Task 1: Add the human-baseline source query

**Files:**
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/IRepositories.cs`
- Modify: `src/SchedulePCMcp/Infrastructure/Repositories/OraclePositionDescriptionRepository.cs`
- Modify: `src/SchedulePCMcp.Tests/TempSchedulePcSourceContractTests.cs`

- [ ] **Step 1: Write the failing source-contract test**

```csharp
[Fact]
public void GetHumanSchedulePcPdNumbersAsync_UsesHumanFlaggedTempHeadersInPdNumberOrder()
{
    var source = File.ReadAllText(GetRepositorySourcePath());

    Assert.Contains("GetHumanSchedulePcPdNumbersAsync", source);
    Assert.Contains("FROM temp_pd_sched_pc header", source, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("header.schedule_pc_ind = 'Y'", source, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("ORDER BY header.pd_nbr", source, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter GetHumanSchedulePcPdNumbersAsync_UsesHumanFlaggedTempHeadersInPdNumberOrder -p:BuildProjectReferences=false
```

Expected: FAIL because the method does not exist.

- [ ] **Step 3: Add the interface and Oracle implementation**

Add `Task<List<string>> GetHumanSchedulePcPdNumbersAsync();` to `IPositionDescriptionRepository`.

```csharp
public async Task<List<string>> GetHumanSchedulePcPdNumbersAsync()
{
    const string sql = @"
        SELECT header.pd_nbr
        FROM temp_pd_sched_pc header
        WHERE header.schedule_pc_ind = 'Y'
        ORDER BY header.pd_nbr";

    using var connection = new OracleConnection(_settings.ConnectionString);
    await connection.OpenAsync();
    using var command = new OracleCommand(sql, connection)
    {
        CommandTimeout = _settings.CommandTimeout
    };

    var pdNumbers = new List<string>();
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        pdNumbers.Add(reader.GetString(0));

    return pdNumbers;
}
```

- [ ] **Step 4: Verify GREEN**

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter GetHumanSchedulePcPdNumbersAsync_UsesHumanFlaggedTempHeadersInPdNumberOrder -p:BuildProjectReferences=false
```

Expected: PASS.

### Task 2: Implement the fixed-90 MCP batch handler

**Files:**
- Create: `src/SchedulePCMcp/MCP/Tools/RescoreHumanSchedulePcPdsToolHandler.cs`
- Create: `src/SchedulePCMcp.Tests/RescoreHumanSchedulePcPdsToolHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests**

Use fakes for `IPositionDescriptionRepository` and `IScoringOrchestrator`. Test that 90 returned PDs are passed to `ScoreAsync` in order, a non-90 source count returns an error without scoring, and an exception for one PD is reported while later PDs continue.

```csharp
Assert.Equal("rescore_human_schedule_pc_pds", handler.Name);
Assert.Equal(90, responseJson.RootElement.GetProperty("selectedCount").GetInt32());
Assert.Equal(90, responseJson.RootElement.GetProperty("scoredCount").GetInt32());
Assert.Equal(0, responseJson.RootElement.GetProperty("failedCount").GetInt32());

Assert.Empty(orchestrator.ScoredPdNumbers);
Assert.Contains("expected 90", responseJson.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

Assert.Equal(89, responseJson.RootElement.GetProperty("scoredCount").GetInt32());
Assert.Equal(1, responseJson.RootElement.GetProperty("failedCount").GetInt32());
Assert.Equal("PD0002", responseJson.RootElement.GetProperty("failedPdNumbers")[0].GetString());
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter FullyQualifiedName~RescoreHumanSchedulePcPdsToolHandlerTests -p:BuildProjectReferences=false
```

Expected: compilation failure because the handler does not exist.

- [ ] **Step 3: Add the handler**

Implement `RescoreHumanSchedulePcPdsToolHandler` as an `IMcpToolHandler` using constructor-injected `IPositionDescriptionRepository`, `IScoringOrchestrator`, and `ILogger<RescoreHumanSchedulePcPdsToolHandler>`.

```csharp
public string Name => "rescore_human_schedule_pc_pds";

public object InputSchema => new
{
    type = "object",
    properties = new { }
};
```

`InvokeAsync` must retrieve `GetHumanSchedulePcPdNumbersAsync()`. When the count is not 90, it returns `error`, `selectedCount`, and `expectedCount` before calling `ScoreAsync`. For 90 PDs, it calls `ScoreAsync` sequentially, logs and captures escaped exceptions, and returns `status`, `selectedCount`, `scoredCount`, `failedCount`, and `failedPdNumbers`.

- [ ] **Step 4: Verify GREEN**

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter FullyQualifiedName~RescoreHumanSchedulePcPdsToolHandlerTests -p:BuildProjectReferences=false
```

Expected: PASS with 3 tests.

### Task 3: Register and verify the feature

**Files:**
- Modify: `src/SchedulePCMcp/Program.cs`

- [ ] **Step 1: Register the scoped MCP tool**

Add this line beside the existing rescore handler registrations:

```csharp
services.AddScoped<IMcpToolHandler, RescoreHumanSchedulePcPdsToolHandler>();
```

- [ ] **Step 2: Run focused tests and build**

```powershell
dotnet test src/SchedulePCMcp.Tests/SchedulePCMcp.Tests.csproj --filter "FullyQualifiedName~RescoreHumanSchedulePcPdsToolHandlerTests|GetHumanSchedulePcPdNumbersAsync_UsesHumanFlaggedTempHeadersInPdNumberOrder" -p:BuildProjectReferences=false
dotnet build src/SchedulePCMcp/SchedulePCMcp.csproj --no-restore
```

Expected: focused tests PASS and production build succeeds.

- [ ] **Step 3: Verify the live source population through Oracle MCP**

```sql
SELECT COUNT(*) AS human_schedule_pc_pd_count
FROM temp_pd_sched_pc
WHERE schedule_pc_ind = 'Y';
```

Expected: `90`. Do not call the new scoring tool in validation because it triggers paid model requests and overwrites existing results.