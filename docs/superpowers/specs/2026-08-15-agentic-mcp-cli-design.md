# Agentic MCP CLI Design

**Date:** 2026-08-15
**Status:** Approved for implementation

## Overview

Replace the hand-rolled `StdioMcpClient` and keyword-dispatch chat loop in `PositionAnalysis.Cli` with the official `ModelContextProtocol.Client` SDK. The LLM drives all tool calls autonomously. Both MCP servers (PositionAnalysis.Mcp and Oracle SQLcl) expose their tools directly to the LLM. Live progress notifications and a configurable cost gate are preserved.

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  PositionAnalysis.Cli (Program.cs)                      │
│                                                         │
│  McpToolRegistry                                        │
│  ├── McpClient (PositionAnalysis.Mcp, stdio)  ──────┐  │
│  └── McpClient (Oracle SQLcl, stdio)          ──────┤  │
│       ↓ .Tools → IReadOnlyList<AITool>              │  │
│                                                     │  │
│  AgenticChatLoop                                    │  │
│  ├── IChatClient.GetResponseAsync(msgs, tools)      │  │
│  ├── detect FunctionCallContent                     │  │
│  ├── registry.CallToolAsync(name, args) ────────────┘  │
│  ├── append FunctionResultContent                       │
│  └── loop until TextContent only                        │
│                                                         │
│  ProgressNotificationHandler (shared)                   │
│  └── both McpClients → OnNotification                   │
│      → Spectre.Console live update                      │
│                                                         │
│  ProcessAllRunner (--unattended mode)                   │
│  └── McpClient (PositionAnalysis.Mcp) directly          │
│      (bypasses LLM entirely)                            │
└─────────────────────────────────────────────────────────┘
         │ stdio                        │ stdio
         ▼                             ▼
  PositionAnalysis.Mcp          Oracle SQLcl MCP
  (domain tools)                (db query tools)
  ↑ confirmation gate
    in expensive tool handlers
```

---

## Components

### CLI: McpToolRegistry

A single class constructed with both `McpClient` instances.

**Initialization (`InitializeAsync`):**
- Calls `ListToolsAsync()` on each client
- Casts results to `AITool`
- Builds `IReadOnlyList<AITool> Tools` — concatenated list passed to `ChatOptions`
- Builds `Dictionary<string, McpClient> _toolOwner` — tool name → owning client

**Dispatch:**
```csharp
Task<string> CallToolAsync(string name, IReadOnlyDictionary<string, object?> args)
```
Looks up `_toolOwner[name]`, calls `mcpClient.CallToolAsync(name, args)`, extracts text via `ToAIContents(null!).OfType<TextContent>()`, returns as string.

Not `IAsyncDisposable` — does not own client lifetimes.

---

### CLI: Agentic Chat Loop

Replaces `PositionAnalysisChatClient` (~1,300 lines → ~200 lines). No tool-name branching.

```
loop:
  response = chatClient.GetResponseAsync(messages, options { Tools = registry.Tools })
  append response.Messages to history
  if no FunctionCallContent → print text → break
  for each FunctionCallContent:
    result = registry.CallToolAsync(call.Name, call.Arguments)
    append FunctionResultContent(call.CallId, result)
  continue loop
```

---

### CLI: Progress Notification Handler

Registered on both `McpClient` instances at startup:

```csharp
foreach (var client in new[] { paClient, oracleClient })
    client.RegisterNotificationHandler("notifications/progress", OnProgressNotification);
```

`OnProgressNotification` reads `progress` and `total` from params and writes `\r [pct%] X/Y…` to `Console.Error`. Fires naturally while `registry.CallToolAsync` is awaited — no special agentic loop wiring needed.

---

### CLI: Lifecycle / Teardown

Both clients declared `await using` at top level:

```csharp
await using var paClient     = await McpClient.CreateAsync(paTransport);
await using var oracleClient = await McpClient.CreateAsync(oracleTransport);
```

The SDK's transport disposes the child process on `DisposeAsync`. `KillProcess()` calls are removed. `CancelKeyPress` cancels the token; the `await using` handles teardown.

---

### CLI: ProcessAllRunner (`--unattended` mode)

- Constructor takes `McpClient` (PositionAnalysis.Mcp instance) instead of `ISchedulePcMcpClient`
- Private helper `CallAndParseAsync(string name, object? args) → JsonElement` wraps the SDK call + JSON extraction
- Polling logic reads the `JsonElement` as before
- `ISchedulePcMcpClient.cs` is deleted

---

### MCP Server: Cost Gate

**Settings (`appsettings.json`, Mcp project):**
```json
"CostGate": {
  "ThresholdUsd": 5.00,
  "EstimatedCostPerPdUsd": 0.05
}
```

**`ICostGateService`:**
```csharp
decimal Estimate(int pdCount);
bool RequiresConfirmation(decimal estimatedCost);
decimal ThresholdUsd { get; }
```

**Tool handler logic** (injected `ICostGateService`):
```csharp
var confirmed = arguments.TryGetProperty("confirmed", ...) && ...GetBoolean();
if (!confirmed)
{
    var pendingCount = await _repo.GetStagedCountAsync();
    var estimatedCost = _costGate.Estimate(pendingCount);
    if (_costGate.RequiresConfirmation(estimatedCost))
        return new { requiresConfirmation = true, pendingCount,
                     estimatedCostUsd = estimatedCost, thresholdUsd = _costGate.ThresholdUsd };
}
// proceed
```

Below threshold → runs directly. Above threshold → LLM receives payload, asks user to confirm, user says yes, LLM re-calls with `{ "confirmed": true }`.

**Tools that get the gate:**

| Tool | Gate |
|---|---|
| `process_batch_all` | Yes |
| `process_batch_by_series` | Yes |
| `process_batch_by_pds` | Yes |
| `run_unattended_scoring` | Yes |
| `rescore_by_series` | Yes |
| `rescore_by_pds` | No — user named explicit PDs |
| `stage_pds`, `export_*`, `generate_*` | No — no LLM cost |

---

### MCP Server: Progress Notifications

Tool handlers that do batch work accept `IProgress<ProgressNotificationValue>` from the request context. The SDK correlates the `progressToken` from `_meta` (sent automatically by `McpClient.CallToolAsync`) and routes notifications back to the CLI.

**Handler shape:**
```csharp
public async Task<object> InvokeAsync(
    JsonElement arguments,
    IProgress<ProgressNotificationValue> progress,
    CancellationToken ct)
{
    for (int i = 0; i < total; i++)
    {
        await ScoreOneAsync(..., ct);
        progress.Report(new ProgressNotificationValue { Progress = i + 1, Total = total });
    }
}
```

`IMcpToolHandler` interface gains the `IProgress<ProgressNotificationValue>` parameter. Handlers that don't do batch work receive it and ignore it.

---

## Files Changed

| File | Change |
|---|---|
| `Cli/Program.cs` | Replace StdioMcpClient startup; add McpToolRegistry init, agentic loop, progress handler; delete PositionAnalysisChatClient and all keyword dispatch |
| `Cli/ISchedulePcMcpClient.cs` | **Delete** |
| `Cli/ProcessAllRunner.cs` | Swap `ISchedulePcMcpClient` → `McpClient`; add `CallAndParseAsync` helper |
| `Mcp/Program.cs` | Register `ICostGateService`, `CostGateSettings` |
| `Mcp/Infrastructure/Config/CostGateSettings.cs` | **New** — `ThresholdUsd`, `EstimatedCostPerPdUsd` |
| `Mcp/Application/Interfaces/ICostGateService.cs` | **New** |
| `Mcp/Application/Services/CostGateService.cs` | **New** |
| `Mcp/Application/Interfaces/IOrchestrators.cs` | Add `IProgress<ProgressNotificationValue>` to batch orchestrator interfaces |
| `Mcp/MCP/Tools/IMcpToolHandler.cs` | Add `IProgress<ProgressNotificationValue>` parameter to `InvokeAsync` |
| `Mcp/MCP/Tools/[5 batch/run handlers]` | Add cost gate check + `progress.Report()` calls |
| `Mcp/MCP/McpStdioServer.cs` | Pass `IProgress` from request context into handler `InvokeAsync` |
| `Mcp/appsettings.json` | Add `CostGate` section |

---

## What Is Deleted

- `StdioMcpClient` class (~220 lines in Program.cs)
- `ISchedulePcMcpClient` interface
- `PositionAnalysisChatClient` class (~1,100 lines)
- All keyword-detection predicates (`IsProcessBatchPrompt`, `IsRescorePdPrompt`, `IsSchemaInformationPrompt`, etc.)
- All direct tool dispatch methods (`ProcessBatchAsync`, `RescoreBySeriesAsync`, `ShowSchemaInformationAsync`, etc.)
- `ListOracleToolNamesAsync` helper

---

## What Is Preserved

- `ProcessAllRunner` (updated, not replaced)
- `MarkdigSpectreRenderer`
- `SchedulePcMcpLaunchCommand`
- Serilog wiring
- Banner rendering
- All MCP server tool handlers (updated with gate + progress, not replaced)
- All existing tool names and their JSON contracts
