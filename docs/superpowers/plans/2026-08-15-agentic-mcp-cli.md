# Agentic MCP CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hand-rolled `StdioMcpClient` and keyword-dispatch chat loop with the official `McpClient` SDK so the LLM drives all tool calls autonomously, both MCP servers are exposed as tools, live progress renders via `OnNotification`, and expensive batch operations gate on a configurable cost threshold.

**Architecture:** A `McpToolRegistry` wraps both `McpClient` instances (PositionAnalysis.Mcp + Oracle SQLcl), presents a unified `IReadOnlyList<AITool>` to `ChatOptions`, and dispatches `FunctionCallContent` back through `McpClientTool.CallAsync`. An `AgenticChatSession` replaces the 1,300-line `PositionAnalysisChatClient` with a ~150-line agentic loop. Progress notifications from either server render via `RegisterNotificationHandler`.

**Tech Stack:** .NET 10, ModelContextProtocol 1.4.1 (`McpClient`, `McpClientTool`, `StdioClientTransport`, `RegisterNotificationHandler`), Microsoft.Extensions.AI 10.6.0 (`IChatClient`, `FunctionCallContent`, `FunctionResultContent`, `ChatOptions`), Spectre.Console, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-15-agentic-mcp-cli-design.md`

## Global Constraints

- Target framework: `net10.0`; `LangVersion: latest`; `Nullable: enable`
- `ModelContextProtocol` version **1.4.1** — use `TextContentBlock` (not `TextContent`) for MCP result extraction
- `Microsoft.Extensions.AI` version **10.6.0** — use `FunctionCallContent`, `FunctionResultContent`, `ChatOptions`
- `McpClientTool.CallAsync` takes `Dictionary<string, object?>` — convert `IReadOnlyDictionary` callers with `new Dictionary<string, object?>(args)`
- `RegisterNotificationHandler` returns `IDisposable` — store and dispose with the client lifetime
- `NotificationMethods.ProgressNotification` and `ProgressNotificationParams` are in `ModelContextProtocol.Protocol`
- Do not add NuGet packages — all required packages are already in `PositionAnalysis.Cli.csproj`
- All test classes go in `src/PositionAnalysis.Mcp.Tests/`; there is no CLI test project
- Commit after every task

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `Mcp/Infrastructure/Config/SettingsClasses.cs` | Modify | Add `CostGateSettings` class |
| `Mcp/Application/Interfaces/ICostGateService.cs` | Create | Cost gate interface |
| `Mcp/Application/Services/CostGateService.cs` | Create | Cost gate implementation |
| `Mcp/Program.cs` | Modify | Register `CostGateSettings` + `ICostGateService` |
| `Mcp/appsettings.json` | Modify | Add `CostGate` section |
| `Mcp.Tests/CostGateServiceTests.cs` | Create | Unit tests for cost gate |
| `Mcp/MCP/Tools/ProcessBatchToolHandler.cs` | Modify | Add cost gate + `confirmed` param |
| `Mcp/MCP/Tools/ProcessBatchBySeriesToolHandler.cs` | Modify | Add cost gate + `confirmed` param |
| `Mcp/MCP/Tools/ProcessBatchByPdsToolHandler.cs` | Modify | Add cost gate + `confirmed` param |
| `Mcp/MCP/Tools/RunUnattendedScoringToolHandler.cs` | Modify | Add cost gate + `confirmed` param |
| `Mcp/MCP/Tools/RescorePdsBySeriesToolHandler.cs` | Modify | Add cost gate + `confirmed` param |
| `Cli/McpToolRegistry.cs` | Create | Aggregate tools from both MCP clients, dispatch calls |
| `Cli/AgenticChatSession.cs` | Create | Agentic LLM loop, progress handler |
| `Cli/Program.cs` | Modify | Replace StdioMcpClient startup, wire McpToolRegistry + AgenticChatSession |
| `Cli/ISchedulePcMcpClient.cs` | Delete | Replaced by McpClient |
| `Cli/ProcessAllRunner.cs` | Modify | Swap `ISchedulePcMcpClient` → `McpClient`, fix tool name bug |

---

## Task 1: CostGate infrastructure (MCP server)

**Files:**
- Modify: `src/PositionAnalysis.Mcp/Infrastructure/Config/SettingsClasses.cs`
- Create: `src/PositionAnalysis.Mcp/Application/Interfaces/ICostGateService.cs`
- Create: `src/PositionAnalysis.Mcp/Application/Services/CostGateService.cs`
- Modify: `src/PositionAnalysis.Mcp/Program.cs` (lines 75-82, where other settings are registered)
- Modify: `src/PositionAnalysis.Mcp/appsettings.json`
- Create: `src/PositionAnalysis.Mcp.Tests/CostGateServiceTests.cs`

**Interfaces:**
- Produces: `ICostGateService` with `decimal Estimate(int pdCount)`, `bool RequiresConfirmation(decimal cost)`, `decimal ThresholdUsd`

- [ ] **Step 1: Write failing tests**

```csharp
// src/PositionAnalysis.Mcp.Tests/CostGateServiceTests.cs
using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Services;
using PositionAnalysis.Mcp.Infrastructure.Config;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class CostGateServiceTests
{
    private static CostGateService MakeService(decimal threshold = 5.00m, decimal costPerPd = 0.05m)
    {
        var settings = Options.Create(new CostGateSettings
        {
            ThresholdUsd = threshold,
            EstimatedCostPerPdUsd = costPerPd
        });
        return new CostGateService(settings);
    }

    [Fact]
    public void Estimate_MultipliesPdCountByCostPerPd()
    {
        var svc = MakeService(costPerPd: 0.05m);
        Assert.Equal(5.00m, svc.Estimate(100));
    }

    [Fact]
    public void RequiresConfirmation_ReturnsFalse_WhenCostBelowThreshold()
    {
        var svc = MakeService(threshold: 5.00m);
        Assert.False(svc.RequiresConfirmation(4.99m));
    }

    [Fact]
    public void RequiresConfirmation_ReturnsFalse_WhenCostEqualsThreshold()
    {
        var svc = MakeService(threshold: 5.00m);
        Assert.False(svc.RequiresConfirmation(5.00m));
    }

    [Fact]
    public void RequiresConfirmation_ReturnsTrue_WhenCostExceedsThreshold()
    {
        var svc = MakeService(threshold: 5.00m);
        Assert.True(svc.RequiresConfirmation(5.01m));
    }

    [Fact]
    public void ThresholdUsd_ReturnsConfiguredValue()
    {
        var svc = MakeService(threshold: 10.00m);
        Assert.Equal(10.00m, svc.ThresholdUsd);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```
dotnet test src/PositionAnalysis.Mcp.Tests --filter CostGateServiceTests
```
Expected: build error — `CostGateService`, `CostGateSettings`, `ICostGateService` not found.

- [ ] **Step 3: Add `CostGateSettings` to SettingsClasses.cs**

Append at the bottom of `src/PositionAnalysis.Mcp/Infrastructure/Config/SettingsClasses.cs` (before the final `}`):

```csharp
/// <summary>
/// Controls whether expensive LLM batch operations require explicit user confirmation.
/// </summary>
public class CostGateSettings
{
    /// <summary>Estimated USD cost per PD scored. Used to compute total before gating.</summary>
    public decimal EstimatedCostPerPdUsd { get; set; } = 0.05m;

    /// <summary>
    /// Operations whose estimated cost exceeds this value require <c>confirmed: true</c>
    /// before running. Set to 0 to gate every call; set to a very large number to disable.
    /// </summary>
    public decimal ThresholdUsd { get; set; } = 5.00m;
}
```

- [ ] **Step 4: Create `ICostGateService`**

```csharp
// src/PositionAnalysis.Mcp/Application/Interfaces/ICostGateService.cs
namespace PositionAnalysis.Mcp.Application.Interfaces;

public interface ICostGateService
{
    /// <summary>Estimated total cost in USD for scoring <paramref name="pdCount"/> PDs.</summary>
    decimal Estimate(int pdCount);

    /// <summary>True when <paramref name="estimatedCost"/> exceeds the configured threshold.</summary>
    bool RequiresConfirmation(decimal estimatedCost);

    decimal ThresholdUsd { get; }
}
```

- [ ] **Step 5: Create `CostGateService`**

```csharp
// src/PositionAnalysis.Mcp/Application/Services/CostGateService.cs
using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Config;

namespace PositionAnalysis.Mcp.Application.Services;

public class CostGateService : ICostGateService
{
    private readonly CostGateSettings _settings;

    public CostGateService(IOptions<CostGateSettings> settings)
    {
        _settings = settings.Value;
    }

    public decimal ThresholdUsd => _settings.ThresholdUsd;

    public decimal Estimate(int pdCount) =>
        pdCount * _settings.EstimatedCostPerPdUsd;

    public bool RequiresConfirmation(decimal estimatedCost) =>
        estimatedCost > _settings.ThresholdUsd;
}
```

- [ ] **Step 6: Register in `Mcp/Program.cs`**

In `ConfigureServices`, after the existing `services.Configure<RatingThresholdSettings>(...)` line (around line 82), add:

```csharp
services.Configure<CostGateSettings>(context.Configuration.GetSection("CostGate"));
services.AddSingleton<ICostGateService, CostGateService>();
```

Also add the missing using at the top of the file if not already present:
```csharp
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Application.Services;
```

- [ ] **Step 7: Add `CostGate` section to `Mcp/appsettings.json`**

Add after the `"MCP"` block:

```json
"CostGate": {
  "ThresholdUsd": 5.00,
  "EstimatedCostPerPdUsd": 0.05
},
```

- [ ] **Step 8: Run tests — verify they pass**

```
dotnet test src/PositionAnalysis.Mcp.Tests --filter CostGateServiceTests
```
Expected: 5 tests pass.

- [ ] **Step 9: Build MCP to verify no compile errors**

```
dotnet build src/PositionAnalysis.Mcp
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Commit**

```
git add src/PositionAnalysis.Mcp/Infrastructure/Config/SettingsClasses.cs \
        src/PositionAnalysis.Mcp/Application/Interfaces/ICostGateService.cs \
        src/PositionAnalysis.Mcp/Application/Services/CostGateService.cs \
        src/PositionAnalysis.Mcp/Program.cs \
        src/PositionAnalysis.Mcp/appsettings.json \
        src/PositionAnalysis.Mcp.Tests/CostGateServiceTests.cs
git commit -m "Add CostGateService and settings for batch operation cost threshold"
```

---

## Task 2: Add cost gate to expensive tool handlers (MCP server)

**Files:**
- Modify: `src/PositionAnalysis.Mcp/MCP/Tools/ProcessBatchToolHandler.cs`
- Modify: `src/PositionAnalysis.Mcp/MCP/Tools/ProcessBatchBySeriesToolHandler.cs`
- Modify: `src/PositionAnalysis.Mcp/MCP/Tools/ProcessBatchByPdsToolHandler.cs`
- Modify: `src/PositionAnalysis.Mcp/MCP/Tools/RunUnattendedScoringToolHandler.cs`
- Modify: `src/PositionAnalysis.Mcp/MCP/Tools/RescorePdsBySeriesToolHandler.cs`

**Interfaces:**
- Consumes: `ICostGateService` (from Task 1)
- Produces: Each handler now returns `{ requiresConfirmation, pendingCount, estimatedCostUsd, thresholdUsd }` when cost exceeds threshold and `confirmed` is not `true`

The gate pattern is the same in every handler. Each handler:
1. Reads optional `confirmed` bool from `arguments`
2. Computes the PD count (varies per handler — see below)
3. If `!confirmed && _costGate.RequiresConfirmation(estimate)` → return confirmation payload
4. Otherwise proceed

- [ ] **Step 1: Update `ProcessBatchToolHandler`**

Replace the entire file:

```csharp
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public sealed class ProcessBatchToolHandler : IMcpStreamingToolHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ParallelBatchScorer _scorer;
    private readonly ICostGateService _costGate;
    private readonly ILogger<ProcessBatchToolHandler> _logger;

    public ProcessBatchToolHandler(
        IServiceScopeFactory scopeFactory,
        ParallelBatchScorer scorer,
        ICostGateService costGate,
        ILogger<ProcessBatchToolHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _scorer = scorer;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "process_batch_all";

    public string Description =>
        "Score ALL pending PDs globally in parallel (10-way concurrency) with live progress notifications. " +
        "Synchronous: the response arrives after all PDs complete. " +
        "When estimated cost exceeds the threshold, returns a requiresConfirmation payload — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution when the cost gate requires confirmation."
            }
        }
    };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
        => InvokeStreamingAsync(arguments, null, null, cancellationToken);

    public async Task<object> InvokeStreamingAsync(
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        List<string> pendingPdNbrs;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();
            var allRows = await evalRepo.GetAllAsync();
            pendingPdNbrs = allRows
                .Where(r => r.Rating == "PENDING")
                .Select(r => r.PdNbr)
                .ToList();
        }

        if (!confirmed)
        {
            var estimatedCost = _costGate.Estimate(pendingPdNbrs.Count);
            if (_costGate.RequiresConfirmation(estimatedCost))
            {
                _logger.LogInformation(
                    "process_batch_all: cost gate triggered for {Count} PDs (est. ${Cost:F2})",
                    pendingPdNbrs.Count, estimatedCost);
                return new
                {
                    requiresConfirmation = true,
                    pendingCount = pendingPdNbrs.Count,
                    estimatedCostUsd = estimatedCost,
                    thresholdUsd = _costGate.ThresholdUsd
                };
            }
        }

        _logger.LogInformation("process_batch_all: {Count} pending PDs found globally", pendingPdNbrs.Count);

        if (pendingPdNbrs.Count == 0)
            return new { total = 0, completed = 0, failed = 0, message = "No pending PDs found." };

        var result = await _scorer.RunAsync(pendingPdNbrs, reportProgressAsync, cancellationToken);
        return new { result.Total, result.Completed, result.Failed, result.Message };
    }
}
```

- [ ] **Step 2: Update `ProcessBatchBySeriesToolHandler`**

Replace the entire file:

```csharp
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public sealed class ProcessBatchBySeriesToolHandler : IMcpStreamingToolHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ParallelBatchScorer _scorer;
    private readonly ICostGateService _costGate;
    private readonly ILogger<ProcessBatchBySeriesToolHandler> _logger;

    public ProcessBatchBySeriesToolHandler(
        IServiceScopeFactory scopeFactory,
        ParallelBatchScorer scorer,
        ICostGateService costGate,
        ILogger<ProcessBatchBySeriesToolHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _scorer = scorer;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "process_batch_by_series";

    public string Description =>
        "Score all pending PDs in the specified series in parallel (10-way concurrency) with live progress notifications. " +
        "When estimated cost exceeds the threshold, returns a requiresConfirmation payload — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        required = new[] { "series" },
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Occupational series codes (5-digit strings, e.g. [\"00301\",\"00560\"])."
            },
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution when the cost gate requires confirmation."
            }
        }
    };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
        => InvokeStreamingAsync(arguments, null, null, cancellationToken);

    public async Task<object> InvokeStreamingAsync(
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var seriesCodes = ParseSeries(arguments);
        if (seriesCodes.Count == 0)
        {
            _logger.LogWarning("process_batch_by_series called with no series codes");
            return new { total = 0, completed = 0, failed = 0, message = "No series codes provided." };
        }

        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        var pendingPdNbrs = new List<string>();
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();
            foreach (var code in seriesCodes)
            {
                try
                {
                    var rows = await evalRepo.GetBySeriesAsync(new OccupationalSeries(code));
                    pendingPdNbrs.AddRange(
                        rows.Where(r => r.Rating == "PENDING").Select(r => r.PdNbr));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning("Skipping invalid series code '{Code}': {Message}", code, ex.Message);
                }
            }
        }

        if (!confirmed)
        {
            var estimatedCost = _costGate.Estimate(pendingPdNbrs.Count);
            if (_costGate.RequiresConfirmation(estimatedCost))
            {
                _logger.LogInformation(
                    "process_batch_by_series: cost gate triggered for {Count} PDs (est. ${Cost:F2})",
                    pendingPdNbrs.Count, estimatedCost);
                return new
                {
                    requiresConfirmation = true,
                    pendingCount = pendingPdNbrs.Count,
                    estimatedCostUsd = estimatedCost,
                    thresholdUsd = _costGate.ThresholdUsd
                };
            }
        }

        _logger.LogInformation(
            "process_batch_by_series: {PdCount} pending PDs across series [{Series}]",
            pendingPdNbrs.Count, string.Join(", ", seriesCodes));

        if (pendingPdNbrs.Count == 0)
            return new { total = 0, completed = 0, failed = 0, message = "No pending PDs found for the specified series." };

        var result = await _scorer.RunAsync(pendingPdNbrs, reportProgressAsync, cancellationToken);
        return new { result.Total, result.Completed, result.Failed, result.Message };
    }

    private static List<string> ParseSeries(JsonElement arguments) =>
        arguments.TryGetProperty("series", out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList()
            : [];
}
```

- [ ] **Step 3: Update `ProcessBatchByPdsToolHandler`**

Replace the entire file:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

public sealed class ProcessBatchByPdsToolHandler : IMcpStreamingToolHandler
{
    private readonly ParallelBatchScorer _scorer;
    private readonly ICostGateService _costGate;
    private readonly ILogger<ProcessBatchByPdsToolHandler> _logger;

    public ProcessBatchByPdsToolHandler(
        ParallelBatchScorer scorer,
        ICostGateService costGate,
        ILogger<ProcessBatchByPdsToolHandler> logger)
    {
        _scorer = scorer;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "process_batch_by_pds";

    public string Description =>
        "Score an explicit list of PD numbers in parallel (10-way concurrency) with live progress notifications. " +
        "When estimated cost exceeds the threshold, returns a requiresConfirmation payload — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        required = new[] { "pdNbrs" },
        properties = new
        {
            pdNbrs = new
            {
                type = "array",
                items = new { type = "string" },
                description = "PD numbers to score."
            },
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution when the cost gate requires confirmation."
            }
        }
    };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
        => InvokeStreamingAsync(arguments, null, null, cancellationToken);

    public async Task<object> InvokeStreamingAsync(
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var pdNbrs = ParsePdNbrs(arguments);

        if (pdNbrs.Count == 0)
        {
            _logger.LogWarning("process_batch_by_pds called with no PD numbers");
            return new { total = 0, completed = 0, failed = 0, message = "No PD numbers provided." };
        }

        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            var estimatedCost = _costGate.Estimate(pdNbrs.Count);
            if (_costGate.RequiresConfirmation(estimatedCost))
            {
                _logger.LogInformation(
                    "process_batch_by_pds: cost gate triggered for {Count} PDs (est. ${Cost:F2})",
                    pdNbrs.Count, estimatedCost);
                return new
                {
                    requiresConfirmation = true,
                    pendingCount = pdNbrs.Count,
                    estimatedCostUsd = estimatedCost,
                    thresholdUsd = _costGate.ThresholdUsd
                };
            }
        }

        _logger.LogInformation("process_batch_by_pds: {Count} PDs submitted", pdNbrs.Count);
        var result = await _scorer.RunAsync(pdNbrs, reportProgressAsync, cancellationToken);
        return new { result.Total, result.Completed, result.Failed, result.Message };
    }

    private static List<string> ParsePdNbrs(JsonElement arguments) =>
        arguments.TryGetProperty("pdNbrs", out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList()
            : [];
}
```

- [ ] **Step 4: Update `RunUnattendedScoringToolHandler`**

Replace the entire file:

```csharp
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class RunUnattendedScoringToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;
    private readonly ProcessAllRunStatusService _runStatusService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICostGateService _costGate;
    private readonly ILogger<RunUnattendedScoringToolHandler> _logger;

    public RunUnattendedScoringToolHandler(
        IScoringOrchestrator scoringOrchestrator,
        ProcessAllRunStatusService runStatusService,
        IServiceScopeFactory scopeFactory,
        ICostGateService costGate,
        ILogger<RunUnattendedScoringToolHandler> logger)
    {
        _scoringOrchestrator = scoringOrchestrator;
        _runStatusService = runStatusService;
        _scopeFactory = scopeFactory;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "run_unattended_scoring";

    public string Description =>
        "Start asynchronous scoring for all staged PENDING PDs across every occupational series. " +
        "When estimated cost exceeds the threshold, returns a requiresConfirmation payload — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution when the cost gate requires confirmation."
            }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            int pendingCount;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();
                var allRows = await evalRepo.GetAllAsync();
                pendingCount = allRows.Count(r => r.Rating == "PENDING");
            }

            var estimatedCost = _costGate.Estimate(pendingCount);
            if (_costGate.RequiresConfirmation(estimatedCost))
            {
                _logger.LogInformation(
                    "run_unattended_scoring: cost gate triggered for {Count} PDs (est. ${Cost:F2})",
                    pendingCount, estimatedCost);
                return new
                {
                    requiresConfirmation = true,
                    pendingCount,
                    estimatedCostUsd = estimatedCost,
                    thresholdUsd = _costGate.ThresholdUsd
                };
            }
        }

        _ = _runStatusService.StartAsync(_scoringOrchestrator.ScoreAllAsync);

        return new
        {
            status = "processing_started",
            message = "Scoring all staged PDs across all series. Poll get_unattended_queue_status to track progress."
        };
    }
}
```

- [ ] **Step 5: Update `RescorePdsBySeriesToolHandler`**

Replace the entire file:

```csharp
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class RescorePdsBySeriesToolHandler : IMcpToolHandler
{
    private readonly IScoringOrchestrator _scoringOrchestrator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICostGateService _costGate;
    private readonly ILogger<RescorePdsBySeriesToolHandler> _logger;

    public RescorePdsBySeriesToolHandler(
        IScoringOrchestrator scoringOrchestrator,
        IServiceScopeFactory scopeFactory,
        ICostGateService costGate,
        ILogger<RescorePdsBySeriesToolHandler> logger)
    {
        _scoringOrchestrator = scoringOrchestrator;
        _scopeFactory = scopeFactory;
        _costGate = costGate;
        _logger = logger;
    }

    public string Name => "rescore_by_series";

    public string Description =>
        "Force a fresh LLM rescore of all PDs in the specified occupational series, overwriting existing results regardless of status. " +
        "When estimated cost exceeds the threshold, returns a requiresConfirmation payload — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of 5-digit occupational series codes to rescore (e.g. ['00110', '00301'])"
            },
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution when the cost gate requires confirmation."
            }
        },
        required = new[] { "series" }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("series", out var seriesEl) ||
            seriesEl.ValueKind != JsonValueKind.Array)
            return new { error = "Missing required parameter: series (array of series codes)" };

        var series = seriesEl.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (series.Count == 0)
            return new { error = "series array cannot be empty" };

        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            int totalCount = 0;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var evalRepo = scope.ServiceProvider.GetRequiredService<IPositionAnalysisEvalRepository>();
                foreach (var code in series)
                {
                    try
                    {
                        var rows = await evalRepo.GetBySeriesAsync(new OccupationalSeries(code));
                        totalCount += rows.Count;
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogWarning("Skipping invalid series code '{Code}': {Message}", code, ex.Message);
                    }
                }
            }

            var estimatedCost = _costGate.Estimate(totalCount);
            if (_costGate.RequiresConfirmation(estimatedCost))
            {
                _logger.LogInformation(
                    "rescore_by_series: cost gate triggered for {Count} PDs (est. ${Cost:F2})",
                    totalCount, estimatedCost);
                return new
                {
                    requiresConfirmation = true,
                    pendingCount = totalCount,
                    estimatedCostUsd = estimatedCost,
                    thresholdUsd = _costGate.ThresholdUsd
                };
            }
        }

        await _scoringOrchestrator.RescoreBySeriesAsync(series);

        return new
        {
            series,
            status = $"Rescore complete for series: {string.Join(", ", series)}. Run generate_documents_all to regenerate Word forms."
        };
    }
}
```

- [ ] **Step 6: Build MCP project — verify no compile errors**

```
dotnet build src/PositionAnalysis.Mcp
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```
git add src/PositionAnalysis.Mcp/MCP/Tools/ProcessBatchToolHandler.cs \
        src/PositionAnalysis.Mcp/MCP/Tools/ProcessBatchBySeriesToolHandler.cs \
        src/PositionAnalysis.Mcp/MCP/Tools/ProcessBatchByPdsToolHandler.cs \
        src/PositionAnalysis.Mcp/MCP/Tools/RunUnattendedScoringToolHandler.cs \
        src/PositionAnalysis.Mcp/MCP/Tools/RescorePdsBySeriesToolHandler.cs
git commit -m "Add cost gate to batch/rescore tool handlers; require confirmed:true above threshold"
```

---

## Task 3: McpToolRegistry (CLI)

**Files:**
- Create: `src/PositionAnalysis.Cli/McpToolRegistry.cs`

**Interfaces:**
- Consumes: `McpClient` (two instances), `ModelContextProtocol.Client`, `ModelContextProtocol.Protocol`
- Produces:
  - `McpToolRegistry.InitializeAsync()` — must be called before accessing `Tools`
  - `IReadOnlyList<AITool> Tools` — concatenated list for `ChatOptions.Tools`
  - `Task<string> CallToolAsync(string name, IReadOnlyDictionary<string, object?> args)`

- [ ] **Step 1: Create `McpToolRegistry.cs`**

```csharp
// src/PositionAnalysis.Cli/McpToolRegistry.cs
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PositionAnalysis.Cli;

/// <summary>
/// Aggregates tools from multiple McpClient instances and dispatches LLM tool calls
/// to the correct client. Each McpClientTool retains an internal reference to its
/// parent McpClient, so dispatch is automatic via McpClientTool.CallAsync.
/// </summary>
public sealed class McpToolRegistry
{
    private IReadOnlyList<McpClientTool> _allTools = [];
    private Dictionary<string, McpClientTool> _toolsByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All tools as AITool instances, ready to pass to ChatOptions.Tools.</summary>
    public IReadOnlyList<AITool> Tools => _allTools.Cast<AITool>().ToList();

    /// <summary>
    /// Calls ListToolsAsync on each client and builds the combined tool registry.
    /// Must be called before accessing <see cref="Tools"/> or <see cref="CallToolAsync"/>.
    /// </summary>
    public async Task InitializeAsync(IEnumerable<McpClient> clients)
    {
        var all = new List<McpClientTool>();
        foreach (var client in clients)
        {
            var tools = await client.ListToolsAsync();
            all.AddRange(tools);
        }

        _allTools = all;
        _toolsByName = all.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Dispatches a tool call to the owning McpClient and returns the text content
    /// of the result as a JSON string.
    /// </summary>
    public async Task<string> CallToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken = default)
    {
        if (!_toolsByName.TryGetValue(name, out var tool))
            throw new InvalidOperationException($"Unknown tool: '{name}'. Available: {string.Join(", ", _toolsByName.Keys)}");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>(args),
            cancellationToken: cancellationToken);

        return string.Join("",
            result.Content
                  .OfType<TextContentBlock>()
                  .Select(t => t.Text ?? ""));
    }
}
```

- [ ] **Step 2: Build CLI to verify no compile errors**

```
dotnet build src/PositionAnalysis.Cli
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```
git add src/PositionAnalysis.Cli/McpToolRegistry.cs
git commit -m "Add McpToolRegistry to aggregate and dispatch tools from multiple McpClient instances"
```

---

## Task 4: AgenticChatSession (CLI)

**Files:**
- Create: `src/PositionAnalysis.Cli/AgenticChatSession.cs`

**Interfaces:**
- Consumes:
  - `IChatClient _chatClient` — for `GetResponseAsync`
  - `McpToolRegistry _registry` — for `Tools` and `CallToolAsync`
  - `string _modelName` — for banner display
- Produces: `Task RunAsync(CancellationToken ct)` — the interactive session loop

- [ ] **Step 1: Create `AgenticChatSession.cs`**

```csharp
// src/PositionAnalysis.Cli/AgenticChatSession.cs
using Microsoft.Extensions.AI;
using Serilog;
using Spectre.Console;

namespace PositionAnalysis.Cli;

/// <summary>
/// Interactive chat session where the LLM autonomously calls MCP tools.
/// Replaces the keyword-dispatch PositionAnalysisChatClient.
/// </summary>
public sealed class AgenticChatSession
{
    private const string SystemPrompt =
        "You are PositionAnalysis Assistant, helping evaluate federal positions under Schedule " +
        "Policy/Career authority. You have access to tools for staging, scoring, generating " +
        "evaluation documents, exporting results, and querying the Oracle database. " +
        "Call tools when the user requests workflow actions. " +
        "For expensive batch operations the tool returns a requiresConfirmation payload with " +
        "an estimated cost — tell the user the cost and ask them to confirm before re-calling " +
        "with { \"confirmed\": true }.";

    private readonly IChatClient _chatClient;
    private readonly McpToolRegistry _registry;
    private readonly string _modelName;
    private readonly List<ChatMessage> _history;

    public AgenticChatSession(IChatClient chatClient, McpToolRegistry registry, string modelName)
    {
        _chatClient = chatClient;
        _registry = registry;
        _modelName = modelName;
        _history = [new ChatMessage(ChatRole.System, SystemPrompt)];
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new Rule("[bold cyan]Schedule PC Position Evaluation[/]")
            .RuleStyle("cyan").LeftJustified());
        AnsiConsole.MarkupLine("[grey]Authority: EO Implementing Schedule Policy/Career[/]");
        AnsiConsole.MarkupLine($"[teal]Model:[/] [bold]{Markup.Escape(_modelName)}[/]");
        AnsiConsole.MarkupLine($"[grey]Tools available:[/] [bold]{_registry.Tools.Count}[/]\n");

        var options = new ChatOptions { Tools = [.. _registry.Tools] };

        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.Markup("[bold cyan]You[/] [grey]▶[/] ");
            var input = Console.ReadLine();

            if (input == null || cancellationToken.IsCancellationRequested)
                break;

            var trimmed = input.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed is "exit" or "quit" or "bye")
            {
                AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
                break;
            }

            _history.Add(new ChatMessage(ChatRole.User, trimmed));

            try
            {
                await RunAgenticTurnAsync(options, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during agentic turn");
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            }
        }
    }

    private async Task RunAgenticTurnAsync(ChatOptions options, CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await _chatClient.GetResponseAsync(_history, options, cancellationToken);
            _history.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (calls.Count == 0)
            {
                // No tool calls — print the text response and return to the prompt
                var text = string.Join("", response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<TextContent>()
                    .Select(t => t.Text ?? ""));

                if (!string.IsNullOrWhiteSpace(text))
                {
                    AnsiConsole.MarkupLine("\n[bold cyan]Assistant[/] [grey]▶[/]");
                    AnsiConsole.WriteLine(MarkdigSpectreRenderer.Render(text));
                    LogTokenUsage(response.Usage);
                }
                return;
            }

            // Execute all tool calls and feed results back
            var resultMessage = new ChatMessage(ChatRole.Tool);
            foreach (var call in calls)
            {
                AnsiConsole.MarkupLine($"[grey]  → calling [bold]{Markup.Escape(call.Name)}[/]…[/]");

                string resultJson;
                try
                {
                    resultJson = await _registry.CallToolAsync(
                        call.Name,
                        call.Arguments ?? new Dictionary<string, object?>(),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    resultJson = $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
                    AnsiConsole.MarkupLine($"[red]  Tool error:[/] {Markup.Escape(ex.Message)}");
                }

                resultMessage.Contents.Add(
                    new FunctionResultContent(call.CallId, call.Name, resultJson));
            }

            _history.Add(resultMessage);
            // Loop: feed results back to the LLM for its next response
        }
    }

    private static void LogTokenUsage(UsageDetails? usage)
    {
        if (usage is null) return;
        Log.Debug("Token usage — input: {Input}, output: {Output}",
            usage.InputTokenCount, usage.OutputTokenCount);
    }
}
```

- [ ] **Step 2: Build CLI — verify no compile errors**

```
dotnet build src/PositionAnalysis.Cli
```
Expected: Build succeeded, 0 errors. (`MarkdigSpectreRenderer` already exists in the project.)

- [ ] **Step 3: Commit**

```
git add src/PositionAnalysis.Cli/AgenticChatSession.cs
git commit -m "Add AgenticChatSession: agentic LLM loop replacing keyword-dispatch PositionAnalysisChatClient"
```

---

## Task 5: Rewire Program.cs and delete dead code (CLI)

**Files:**
- Modify: `src/PositionAnalysis.Cli/Program.cs` — replace StdioMcpClient startup, wire `McpToolRegistry` + `AgenticChatSession`; delete `StdioMcpClient` class and `PositionAnalysisChatClient` class
- Delete: `src/PositionAnalysis.Cli/ISchedulePcMcpClient.cs`

**Interfaces:**
- Consumes: `McpToolRegistry` (Task 3), `AgenticChatSession` (Task 4), `McpClient`, `StdioClientTransport` (official SDK)

The new `Program.cs` top-level code replaces everything from the `await using var mcpClient` line down to the end of `PositionAnalysisChatClient`. The `BuildChatClient`, `GetModelDisplay`, and `ResolveProjectLogsDirectory` helpers are unchanged.

- [ ] **Step 1: Replace the top-level startup block in `Program.cs`**

The section to replace starts at line 41 (`var unattendedMode = ...`) and ends at line 108 (`await client.RunAsync()`). Replace with:

```csharp
var unattendedMode = args.Length == 1 && args[0].Equals("--unattended", StringComparison.OrdinalIgnoreCase);
var schedulePcMcpLaunchCommand = SchedulePcMcpLaunchResolver.Resolve(AppContext.BaseDirectory);

// ── PositionAnalysis.Mcp transport ──────────────────────────────────────────
var paTransport = new StdioClientTransport(new StdioClientTransportOptions
{
    Command = schedulePcMcpLaunchCommand.Command,
    Arguments = schedulePcMcpLaunchCommand.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
    WorkingDirectory = schedulePcMcpLaunchCommand.WorkingDirectory,
    Name = "PositionAnalysis.Mcp",
    // Suppress MCP server log lines so they don't pollute the chat console
    StandardErrorLines = line => Log.Debug("[MCP] {Line}", line)
});

await using var paClient = await McpClient.CreateAsync(paTransport);

if (unattendedMode)
{
    using var unattendedCancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; unattendedCancellation.Cancel(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => unattendedCancellation.Cancel();

    Environment.ExitCode = await new ProcessAllRunner(
        paClient,
        TimeSpan.FromSeconds(30),
        (Func<TimeSpan, CancellationToken, Task>)Task.Delay,
        Log.Logger).RunAsync(unattendedCancellation.Token);
    return;
}

// ── Oracle SQLcl MCP transport (optional) ───────────────────────────────────
var sqlclPath = configuration["SqlclMcp:Path"];
var sqlclConnectionName = configuration["SqlclMcp:ConnectionName"] ?? "acrs";

McpClient? oracleClient = null;
if (!string.IsNullOrWhiteSpace(sqlclPath) && File.Exists(sqlclPath))
{
    var oracleTransport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Command = sqlclPath,
        Arguments = ["-mcp"],
        Name = "OracleSqlcl",
        StandardErrorLines = line => Log.Debug("[Oracle MCP] {Line}", line)
    });
    oracleClient = await McpClient.CreateAsync(oracleTransport);
}
else
{
    AnsiConsole.MarkupLine("[yellow]Oracle SQLcl MCP not configured or sql.exe not found — Oracle tools unavailable.[/]");
}

await using var oracleClientDisposer = oracleClient;

// ── Progress notification handler (shared across both clients) ───────────────
int lastPct = -1;

ValueTask OnProgressNotification(JsonRpcNotification notification, CancellationToken _ct)
{
    if (JsonSerializer.Deserialize<ProgressNotificationParams>(notification.Params) is { } pn)
    {
        var current = (int)pn.Progress.Progress;
        var total = (int)(pn.Progress.Total ?? 0);
        if (total > 0)
        {
            var pct = (int)(current * 100.0 / total);
            if (pct != lastPct)
            {
                lastPct = pct;
                Console.Error.Write($"\r  [{pct,3}%] {current}/{total} scored…   ");
            }
        }
    }
    return ValueTask.CompletedTask;
}

using var paProgressReg = paClient.RegisterNotificationHandler(
    NotificationMethods.ProgressNotification, OnProgressNotification);
using var oracleProgressReg = oracleClient?.RegisterNotificationHandler(
    NotificationMethods.ProgressNotification, OnProgressNotification);

// ── Tool registry ─────────────────────────────────────────────────────────────
var clients = oracleClient is not null
    ? new[] { paClient, oracleClient }
    : new[] { paClient };

var registry = new McpToolRegistry();
await registry.InitializeAsync(clients);

// ── Chat client + agentic session ─────────────────────────────────────────────
var provider = configuration["AI:Provider"] ?? "Ollama";
IChatClient chatClient = BuildChatClient(configuration, provider);
var modelDisplay = GetModelDisplay(configuration, provider);

var session = new AgenticChatSession(chatClient, registry, modelDisplay);

Console.CancelKeyPress += (_, e) => { e.Cancel = true; Environment.Exit(0); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => { /* await using handles teardown */ };

await session.RunAsync();
```

- [ ] **Step 2: Add missing `using` directives at the top of `Program.cs`**

Ensure these are present in the using block at the top of the file (before the top-level statements):

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
```

(`ModelContextProtocol` and `ModelContextProtocol.Client` are already there; add `ModelContextProtocol.Protocol` for `NotificationMethods` and `ProgressNotificationParams`.)

- [ ] **Step 3: Delete the `PositionAnalysisChatClient` class from `Program.cs`**

Delete everything from `class PositionAnalysisChatClient` down to and including its closing `}` — this is approximately lines 218–1277 in the current file.

- [ ] **Step 4: Delete the `StdioMcpClient` class from `Program.cs`**

Delete everything from `public sealed class StdioMcpClient` to its closing `}` — approximately lines 1279–1510.

- [ ] **Step 5: Delete `ISchedulePcMcpClient.cs`**

```
git rm src/PositionAnalysis.Cli/ISchedulePcMcpClient.cs
```

- [ ] **Step 6: Delete `ListOracleToolNamesAsync` top-level helper from `Program.cs`**

This is the `static async Task<IReadOnlyList<string>> ListOracleToolNamesAsync(McpClient mcpClient)` function (approximately lines 210–216). Delete it — it's no longer needed.

- [ ] **Step 7: Build CLI — verify no compile errors**

```
dotnet build src/PositionAnalysis.Cli
```
Expected: Build succeeded, 0 errors. If `SchedulePcMcpLaunchCommand.Arguments` is a space-separated string, the `.Split(' ', StringSplitOptions.RemoveEmptyEntries)` in Step 1 handles it. If it's already `string[]`, remove the `.Split(...)` call.

- [ ] **Step 8: Smoke-test — start the CLI interactively**

```
dotnet run --project src/PositionAnalysis.Cli
```
Expected: Banner prints, tool count shown, prompt appears. Type `list tools` (the LLM will describe available tools from its system context). Type `exit` to quit cleanly.

- [ ] **Step 9: Commit**

```
git add src/PositionAnalysis.Cli/Program.cs
git rm src/PositionAnalysis.Cli/ISchedulePcMcpClient.cs
git commit -m "Rewire CLI to official McpClient; replace keyword-dispatch loop with AgenticChatSession"
```

---

## Task 6: Update ProcessAllRunner (CLI)

**Files:**
- Modify: `src/PositionAnalysis.Cli/ProcessAllRunner.cs`

**Interfaces:**
- Consumes: `McpClient paClient` (direct — not through registry), `ModelContextProtocol.Protocol.TextContentBlock`
- The `--unattended` mode only calls two tools: `run_unattended_scoring` and `get_unattended_queue_status`

**Note:** The current `ProcessAllRunner.cs` has a bug on line 45 — it calls `get_queue_status` but the tool is named `get_unattended_queue_status`. Fix this as part of this task.

- [ ] **Step 1: Replace `ProcessAllRunner.cs`**

```csharp
// src/PositionAnalysis.Cli/ProcessAllRunner.cs
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Serilog;

namespace PositionAnalysis.Cli;

public sealed class ProcessAllRunner
{
    private readonly McpClient _paClient;
    private readonly TimeSpan _pollInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ILogger _logger;

    public ProcessAllRunner(
        McpClient paClient,
        TimeSpan pollInterval,
        Func<TimeSpan, Task> delayAsync,
        ILogger? logger = null)
        : this(paClient, pollInterval, (delay, _) => delayAsync(delay), logger)
    {
    }

    public ProcessAllRunner(
        McpClient paClient,
        TimeSpan pollInterval,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        ILogger? logger = null)
    {
        _paClient = paClient;
        _pollInterval = pollInterval;
        _delayAsync = delayAsync;
        _logger = logger ?? Log.Logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Information("Starting unattended Schedule PC scoring run");
            cancellationToken.ThrowIfCancellationRequested();
            await CallAndParseAsync("run_unattended_scoring", null, cancellationToken);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await CallAndParseAsync("get_unattended_queue_status", null, cancellationToken);
                var status = ParseQueueStatus(response);

                _logger.Information(
                    "Schedule PC queue status: Pending={Pending}, InProgress={InProgress}, Complete={Complete}, Failed={Failed}, IsDrained={IsDrained}, RunFailed={RunFailed}",
                    status.Pending,
                    status.InProgress,
                    status.Complete,
                    status.Failed,
                    status.IsDrained,
                    status.RunFailed);

                if (status.RunFailed)
                {
                    _logger.Error("Schedule PC unattended scoring run failed: {RunError}", status.RunError);
                    return 1;
                }

                if (status.IsDrained)
                {
                    var exitCode = status.Failed == 0 ? 0 : 1;
                    _logger.Information("Schedule PC unattended scoring run finished with exit code {ExitCode}", exitCode);
                    return exitCode;
                }

                await _delayAsync(_pollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Information("Schedule PC unattended scoring run was cancelled");
            return 2;
        }
    }

    private async Task<JsonElement> CallAndParseAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? args,
        CancellationToken cancellationToken)
    {
        var result = await _paClient.CallToolAsync(toolName, args, cancellationToken);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static QueueStatusResponse ParseQueueStatus(JsonElement response)
    {
        var pending = GetRequiredInt32(response, "pending");
        var inProgress = GetRequiredInt32(response, "inProgress");
        var isDrained = GetRequiredBoolean(response, "isDrained");
        var calculatedDrained = pending == 0 && inProgress == 0;
        if (isDrained != calculatedDrained)
            throw new InvalidOperationException("get_unattended_queue_status response has an isDrained field inconsistent with pending and inProgress.");

        return new QueueStatusResponse(
            pending,
            inProgress,
            GetRequiredInt32(response, "complete"),
            GetRequiredInt32(response, "failed"),
            isDrained,
            GetRequiredBoolean(response, "runFailed"),
            GetRequiredNullableString(response, "runError"));
    }

    private static int GetRequiredInt32(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value))
            throw new InvalidOperationException($"get_unattended_queue_status response is missing a valid integer '{propertyName}' field.");
        return value;
    }

    private static bool GetRequiredBoolean(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"get_unattended_queue_status response is missing a valid boolean '{propertyName}' field.");
        return property.GetBoolean();
    }

    private static string? GetRequiredNullableString(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
            throw new InvalidOperationException($"get_unattended_queue_status response is missing a valid string or null '{propertyName}' field.");
        return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
    }

    private sealed record QueueStatusResponse(
        int Pending,
        int InProgress,
        int Complete,
        int Failed,
        bool IsDrained,
        bool RunFailed,
        string? RunError);
}
```

- [ ] **Step 2: Build CLI — verify no compile errors**

```
dotnet build src/PositionAnalysis.Cli
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run full test suite**

```
dotnet test src/PositionAnalysis.Mcp.Tests
```
Expected: All existing tests pass. (No CLI tests exist for `ProcessAllRunner`.)

- [ ] **Step 4: Commit**

```
git add src/PositionAnalysis.Cli/ProcessAllRunner.cs
git commit -m "Update ProcessAllRunner to use official McpClient; fix get_queue_status → get_unattended_queue_status bug"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Covered by |
|---|---|
| Replace StdioMcpClient with official McpClient | Task 5 |
| Both MCP clients' tools exposed to LLM | Task 3 (McpToolRegistry) |
| Agentic loop with FunctionCallContent dispatch | Task 4 (AgenticChatSession) |
| Live progress via OnNotification | Task 5 (progress handler registration) |
| Cost gate in MCP server tools | Tasks 1 + 2 |
| CostGate configurable threshold in appsettings | Task 1 |
| ProcessAllRunner updated (not replaced) | Task 6 |
| ISchedulePcMcpClient deleted | Task 5 |
| Clean lifecycle via IAsyncDisposable | Task 5 (`await using var paClient / oracleClient`) |

**Type consistency check:**

- `McpToolRegistry.CallToolAsync` returns `Task<string>` — `AgenticChatSession` passes this string as `FunctionResultContent` result: ✓
- `McpClientTool.CallAsync` takes `Dictionary<string, object?>` — registry wraps with `new Dictionary<string, object?>(args)`: ✓
- `TextContentBlock` used in both `McpToolRegistry` and `ProcessAllRunner` for result extraction: ✓
- `NotificationMethods.ProgressNotification` + `ProgressNotificationParams` from `ModelContextProtocol.Protocol`: ✓
- `ICostGateService` used in 5 handlers, registered as `AddSingleton` in `Mcp/Program.cs`: ✓
- `FunctionCallContent.Arguments` is `IReadOnlyDictionary<string, object?>?` — null-coalesced with `new Dictionary<string, object?>()` before passing to registry: ✓

**`SchedulePcMcpLaunchCommand.Arguments`:** The `StdioClientTransportOptions.Arguments` is `string[]`. Check `SchedulePcMcpLaunchCommand` — if `Arguments` is already `string[]`, pass it directly; if it's a space-separated `string`, use `.Split(' ', StringSplitOptions.RemoveEmptyEntries)` as shown in Task 5 Step 1.
