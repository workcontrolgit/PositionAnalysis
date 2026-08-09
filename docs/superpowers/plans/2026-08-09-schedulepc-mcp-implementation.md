# Schedule PC MCP Server — Implementation Plan
**Date:** 2026-08-09  
**Status:** Ready for Implementation  
**Owner:** Schedule PC Team

---

## 📋 Overview

Build a clean-architecture Model Context Protocol (MCP) server that orchestrates the complete Schedule PC evaluation pipeline: staging → reporting → scoring → document generation → export. The server exposes MCP tools that the chat integrates with, providing a unified interface for PD staging, LLM scoring, and result export.

**Estimated Effort:** 4–5 days (phased approach)

---

## 🎯 Success Criteria

- [ ] MCP server starts and registers 6 tools via stdio transport
- [ ] `stage_pds` queries Oracle, stages to SCHEDULE_PC_EVAL, returns run_id
- [ ] `get_staging_report` aggregates counts by occ_series with status
- [ ] `process_pds_by_series` scores PDs asynchronously (fire-and-forget)
- [ ] `get_processing_status` returns live progress by series
- [ ] `generate_documents` fills Word forms for complete PDs (all or by series)
- [ ] `export_results` exports SCHEDULE_PC_EVAL to Excel with tracker summary
- [ ] All outputs saved to `C:\apps\schedulepc\reports\{RunId}\` structure
- [ ] Unit & integration tests pass (80%+ coverage on orchestrators)
- [ ] Configuration via appsettings.json (AI provider, Oracle conn, output paths)

---

## 📁 Project Structure

```
C:\apps\oracle\src\SchedulePCMcp\
├── Program.cs                                  (MCP host, DI setup, entry point)
├── appsettings.json                            (config: Oracle, AI, output paths)
├── appsettings.Development.json
├── SchedulePCMcp.csproj                        (project file, dependencies)
│
├── MCP/
│   ├── McpToolsProvider.cs                     (RegisterTools: all 6 tools)
│   ├── Tools/
│   │   ├── StagePdsToolHandler.cs
│   │   ├── GetStagingReportToolHandler.cs
│   │   ├── ProcessPdsBySeriesToolHandler.cs
│   │   ├── GetProcessingStatusToolHandler.cs
│   │   ├── GenerateDocumentsToolHandler.cs
│   │   └── ExportResultsToolHandler.cs
│   └── Dto/
│       ├── StagePdsRequest.cs
│       ├── StagePdsResponse.cs
│       ├── StagingReportRequest.cs
│       ├── StagingReportResponse.cs
│       ├── SeriesStatusDto.cs
│       ├── ProcessPdsRequest.cs
│       ├── ProcessPdsResponse.cs
│       ├── ProcessingStatusResponse.cs
│       ├── GenerateDocumentsRequest.cs
│       ├── GenerateDocumentsResponse.cs
│       ├── ExportResultsRequest.cs
│       └── ExportResultsResponse.cs
│
├── Application/
│   ├── Services/
│   │   ├── StagingOrchestrator.cs              (stage PDs from MAX_PD_VW)
│   │   ├── ReportingService.cs                 (aggregate by_series counts)
│   │   ├── ScoringOrchestrator.cs              (async LLM scoring)
│   │   ├── ProcessingStatusService.cs          (live progress tracker)
│   │   ├── DocumentGenerationOrchestrator.cs   (scope: all|series)
│   │   └── ExportOrchestrator.cs               (export to Excel)
│   └── Interfaces/
│       ├── IStagingOrchestrator.cs
│       ├── IReportingService.cs
│       ├── IScoringOrchestrator.cs
│       ├── IProcessingStatusService.cs
│       ├── IDocumentGenerationOrchestrator.cs
│       └── IExportOrchestrator.cs
│
├── Domain/
│   ├── Entities/
│   │   ├── PositionDescription.cs              (PD entity with validation)
│   │   ├── EvaluationResult.cs                 (score, rating, justification)
│   │   └── RunMetadata.cs                      (run_id, created_at, status)
│   ├── ValueObjects/
│   │   ├── StagingFilter.cs                    (gradeMin, gradeMax, series, org)
│   │   ├── Grade.cs                            (Grade: 1–15, validate range)
│   │   ├── OccupationalSeries.cs               (4-digit series validation)
│   │   ├── EvaluationCriteria.cs               (scoring rules, weights)
│   │   └── SeriesStatus.cs                     (series, staged, in_progress, complete)
│   └── Enums/
│       ├── EvaluationStatus.cs                 (Staged, InProgress, Complete, Failed)
│       └── LlmProvider.cs                      (Ollama, AzureOpenAI)
│
├── Infrastructure/
│   ├── Config/
│   │   ├── OutputSettings.cs                   (ReportsBaseDirectory, GetRunDirectory, etc.)
│   │   └── AiSettings.cs                       (Provider, Model, endpoints)
│   ├── Repositories/
│   │   ├── IPositionDescriptionRepository.cs
│   │   ├── OraclePositionDescriptionRepository.cs  (queries MAX_PD_VW, PD_DUTIES)
│   │   ├── ISchedulePCEvalRepository.cs
│   │   ├── OracleSchedulePCEvalRepository.cs       (CRUD SCHEDULE_PC_EVAL)
│   │   ├── IRunMetadataRepository.cs
│   │   └── OracleRunMetadataRepository.cs
│   ├── AI/
│   │   ├── IAiClient.cs
│   │   ├── OllamaAiClient.cs                   (connects to localhost:11434)
│   │   ├── AzureOpenAiClient.cs                (uses config credentials)
│   │   └── AiClientFactory.cs                  (builds based on appsettings)
│   ├── Storage/                                 (ABSTRACTION: supports file system → database migration)
│   │   ├── IStorageProvider.cs                 (interface: Save, Retrieve, Delete artifacts)
│   │   ├── FileSystemStorageProvider.cs        (DEV: saves to C:\apps\schedulepc\reports\)
│   │   ├── DbStorageProvider.cs                (FUTURE: stores BLOBs in Oracle)
│   │   └── StorageProviderFactory.cs           (builds based on appsettings config)
│   ├── Export/
│   │   ├── IExcelExporter.cs
│   │   └── ClosedXmlExcelExporter.cs           (generates xlsx, delegates save to IStorageProvider)
│   ├── Documents/
│   │   ├── IDocumentGenerationStrategy.cs      (interface: pure C# OpenXml approach)
│   │   ├── OpenXmlDocumentStrategy.cs          (fills Word templates using DocumentFormat.OpenXml)
│   │   ├── DocumentStrategyFactory.cs          (builds strategy from config)
│   │   └── DocumentRunState.cs                 (tracks which PDs to generate)
│   ├── Data/
│   │   └── OracleConnectionFactory.cs          (creates IDbConnection from config)
│   └── Logging/
│       └── LogEnricher.cs                      (enriches logs with RunId, PdNbr)
│
├── Tests/
│   ├── UnitTests/
│   │   ├── Application/
│   │   │   ├── StagingOrchestratorTests.cs
│   │   │   ├── ReportingServiceTests.cs
│   │   │   ├── ScoringOrchestratorTests.cs
│   │   │   ├── ProcessingStatusServiceTests.cs
│   │   │   ├── DocumentGenerationOrchestratorTests.cs
│   │   │   └── ExportOrchestratorTests.cs
│   │   ├── Domain/
│   │   │   ├── GradeTests.cs
│   │   │   ├── OccupationalSeriesTests.cs
│   │   │   └── StagingFilterTests.cs
│   │   └── Infrastructure/
│   │       ├── AiClientFactoryTests.cs
│   │       ├── OracleRepositoryTests.cs
│   │       └── ExcelExporterTests.cs
│   ├── IntegrationTests/
│   │   ├── SchedulePCMcpIntegrationTests.cs
│   │   ├── OracleIntegrationTests.cs
│   │   └── MockOracleFixture.cs
│   └── TestData/
│       ├── SamplePDs.cs
│       └── SampleEvaluationResults.cs
│
└── bin/, obj/, logs/

```

---

## 📝 Phase-by-Phase Implementation

### **Phase 1: Scaffolding & Configuration** (1 day)

**Deliverables:**
- [ ] Create SchedulePCMcp project with .csproj and dependencies
- [ ] Set up appsettings.json with AI provider, Oracle conn, output paths
- [ ] Create Program.cs with MCP host, DI container, logging setup
- [ ] Create all domain entities & value objects (stub implementations)
- [ ] Create all application service interfaces

**Key Files to Create:**
1. `SchedulePCMcp.csproj` — Add NuGet deps:
   - `Microsoft.Extensions.Hosting` (v8+)
   - `Serilog`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`
   - `OllamaSharp` (v0.x)
   - `Azure.AI.OpenAI` (v1.x)
   - `Oracle.ManagedDataAccess.Core` (v23+)
   - `ClosedXML` (v0.x)
   - `DocumentFormat.OpenXml` (v3+)
   - `Model.Context.Protocol` (MCP package)

2. `appsettings.json` — Full config with comments
3. `Program.cs` — Host builder, DI registration
4. Domain entities (empty bodies, ready for Phase 2)

**Definition of Done:**
- Project compiles, no warnings
- `dotnet run` starts MCP server (doesn't crash)
- appsettings.json validates without errors

---

### **Phase 2: Domain & Value Objects** (0.5 day)

**Deliverables:**
- [ ] Implement `Grade`, `OccupationalSeries` with validation
- [ ] Implement `StagingFilter` value object
- [ ] Implement `PositionDescription`, `EvaluationResult` entities
- [ ] Implement `SeriesStatus` for aggregation
- [ ] Unit tests for all value objects (validation rules)

**Key Logic:**
```csharp
// Grade: 1–15
public class Grade : IComparable<Grade>
{
    public int Value { get; }
    public Grade(int value)
    {
        if (value < 1 || value > 15)
            throw new ArgumentException("Grade must be 1–15");
        Value = value;
    }
}

// OccupationalSeries: 4-digit string
public class OccupationalSeries
{
    public string Code { get; }
    public OccupationalSeries(string code)
    {
        if (!Regex.IsMatch(code, @"^\d{4}$"))
            throw new ArgumentException("Series must be 4 digits");
        Code = code;
    }
}

// StagingFilter: aggregate filters
public class StagingFilter
{
    public Grade GradeMin { get; }
    public Grade GradeMax { get; }
    public OccupationalSeries? Series { get; }
    public string? OrgCode { get; }
    
    public StagingFilter(Grade min, Grade max, OccupationalSeries? series = null, string? org = null)
    {
        if (min.CompareTo(max) > 0)
            throw new ArgumentException("GradeMin must be <= GradeMax");
        GradeMin = min;
        GradeMax = max;
        Series = series;
        OrgCode = org;
    }
}
```

**Definition of Done:**
- All value objects are immutable, self-validating
- 100% test coverage for validation rules
- No throws from domain logic except on invalid input

---

### **Phase 3: Infrastructure & Repositories** (1.5 days)

**Deliverables:**
- [ ] `OracleConnectionFactory` — creates connections from appsettings
- [ ] `IPositionDescriptionRepository` + `OraclePositionDescriptionRepository`
  - Query `MAX_PD_VW` with grade/series filters
  - Query `PD_DUTIES` for each PD
  - Return `PositionDescription` domain objects
- [ ] `ISchedulePCEvalRepository` + `OracleSchedulePCEvalRepository`
  - Insert/update SCHEDULE_PC_EVAL rows
  - Query by RunId, by status, by series
- [ ] `IRunMetadataRepository` + `OracleRunMetadataRepository`
  - Track run creation, row counts, timestamps
- [ ] `AiClientFactory` — returns `IOracleAiClient` (Ollama or Azure)
- [ ] `OllamaAiClient`, `AzureOpenAiClient` implementations
- [ ] `ClosedXmlExcelExporter` — exports to xlsx
- [ ] `WordDocumentGenerator` — invokes Fill-EvalTemplate-Batch-v2.ps1
- [ ] Integration tests using mock Oracle or test DB

**Key SQL (via SQLcl/ODP.NET):**
```sql
-- Staging: fetch PDs by grade + series
SELECT pd.PD_NBR, pd.PD_TITLE, pd.GRADE, pd.SERIES, pd.ORG_CODE, pd.INTRO_TEXT
FROM MAX_PD_VW pd
WHERE pd.GRADE BETWEEN :gradeMin AND :gradeMax
  AND (:series IS NULL OR pd.SERIES = :series)
ORDER BY pd.SERIES, pd.PD_NBR;

-- Get duties for PD
SELECT duty_text, percent_time, is_critical
FROM PD_DUTIES
WHERE pd_nbr = :pdNbr
ORDER BY duty_seq;

-- Insert run metadata
INSERT INTO SCHEDULE_PC_EVAL (run_id, pd_nbr, series, status, created_at, result_json)
VALUES (:runId, :pdNbr, :series, 'Staged', SYSDATE, NULL);

-- Update after scoring
UPDATE SCHEDULE_PC_EVAL
SET status = 'Complete', result_json = :resultJson, updated_at = SYSDATE
WHERE run_id = :runId AND pd_nbr = :pdNbr;
```

**Definition of Done:**
- All repositories have unit tests with mocks
- Integration tests pass against test Oracle instance (or mock)
- AI clients successfully score a sample PD
- Excel export generates valid .xlsx file

---

### **Phase 4: Application Orchestrators** (1.5 days)

**Deliverables:**
- [ ] `StagingOrchestrator` — fetch + stage PDs to DB
- [ ] `ReportingService` — aggregate status by series
- [ ] `ScoringOrchestrator` — async loop over PDs, call LLM, persist
- [ ] `ProcessingStatusService` — in-memory tracker (staged→in_progress→complete)
- [ ] `DocumentGenerationOrchestrator` — scope-aware generation
- [ ] `ExportOrchestrator` — query results, export to Excel
- [ ] Unit tests for all orchestrators (mock repos, mock AI client)

**Key Pseudocode:**

```csharp
// StagingOrchestrator.StagePds
public async Task<string> StagePds(StagingFilter filter)
{
    string runId = $"RUN_{DateTime.Now:yyyyMMdd_HHmmss}";
    var pds = await _pdRepository.QueryByFilter(filter);
    
    foreach (var pd in pds)
    {
        await _schedulePCRepository.InsertStaged(runId, pd);
    }
    
    await _runMetadataRepository.InsertRun(runId, pds.Count);
    return runId;
}

// ScoringOrchestrator.ProcessPdsBySeriesAsync (fire-and-forget)
public void ProcessPdsBySeriesAsync(string runId, List<string> series)
{
    _ = Task.Run(async () =>
    {
        var stagedPds = await _schedulePCRepository.QueryByRunAndStatus(runId, "Staged");
        var filtered = stagedPds.Where(p => series.Contains(p.Series)).ToList();
        
        foreach (var pd in filtered)
        {
            _statusService.MarkInProgress(runId, pd.PdNbr);
            try
            {
                var score = await _aiClient.ScorePD(pd.Duties);
                var result = new EvaluationResult { Score = score, ... };
                await _schedulePCRepository.UpdateComplete(runId, pd.PdNbr, result);
                _statusService.MarkComplete(runId, pd.PdNbr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed scoring PD {PdNbr}", pd.PdNbr);
                await _schedulePCRepository.UpdateFailed(runId, pd.PdNbr, ex.Message);
                _statusService.MarkFailed(runId, pd.PdNbr);
            }
        }
    });
}

// ProcessingStatusService (in-memory tracker)
public ProcessingStatusResponse GetStatus(string runId)
{
    var byStatus = _statusCache[runId]
        .GroupBy(x => x.Status)
        .ToDictionary(g => g.Key, g => g.Count());
    
    var bySeries = _statusCache[runId]
        .GroupBy(x => x.Series)
        .Select(g => new SeriesStatusDto
        {
            Series = g.Key,
            Staged = g.Count(x => x.Status == "Staged"),
            InProgress = g.Count(x => x.Status == "InProgress"),
            Complete = g.Count(x => x.Status == "Complete")
        })
        .ToList();
    
    return new ProcessingStatusResponse { Overall = byStatus, BySeries = bySeries };
}

// DocumentGenerationOrchestrator.GenerateDocuments
public async Task<GenerateDocumentsResponse> GenerateDocuments(string runId, string scope, List<string>? series = null)
{
    var completePds = await _schedulePCRepository.QueryByRunAndStatus(runId, "Complete");
    if (scope == "series" && series != null)
        completePds = completePds.Where(p => series.Contains(p.Series)).ToList();
    
    _outputSettings.EnsureDirectoriesExist(runId);
    var wordDir = _outputSettings.GetWordDirectory(runId);
    
    foreach (var pd in completePds)
    {
        var jsonFile = Path.Combine(Path.GetTempPath(), $"{pd.PdNbr}.json");
        await File.WriteAllTextAsync(jsonFile, JsonConvert.SerializeObject(pd.Result));
        
        await _wordGenerator.GenerateForm(jsonFile, wordDir);
    }
    
    return new GenerateDocumentsResponse 
    { 
        DocumentsGenerated = completePds.Count, 
        OutputPath = wordDir 
    };
}

// ExportOrchestrator.ExportResults
public async Task<ExportResultsResponse> ExportResults(string runId, string format)
{
    var rows = await _schedulePCRepository.QueryByRun(runId);
    var excelPath = Path.Combine(_outputSettings.GetExcelDirectory(runId), $"SchedulePC_{runId}_Tracker.xlsx");
    
    await _excelExporter.Export(rows, excelPath);
    
    return new ExportResultsResponse { FilePath = excelPath, RowCount = rows.Count };
}
```

**Definition of Done:**
- All orchestrators tested with mocks (100% method coverage)
- Async scoring doesn't block tool responses
- Status service correctly tracks state transitions
- Document & export paths created per runId structure

---

### **Phase 5: MCP Tools & Tool Provider** (1 day)

**Deliverables:**
- [ ] `McpToolsProvider` — `RegisterTools()` method
- [ ] All 6 tool handlers:
  - `StagePdsToolHandler`
  - `GetStagingReportToolHandler`
  - `ProcessPdsBySeriesToolHandler`
  - `GetProcessingStatusToolHandler`
  - `GenerateDocumentsToolHandler`
  - `ExportResultsToolHandler`
- [ ] Tool request/response serialization (JSON)
- [ ] Error handling & validation
- [ ] Integration with MCP host

**Tool Definitions (Pseudo-JSON):**

```json
{
  "name": "stage_pds",
  "description": "Stage PDs by grade range and optional series filter",
  "inputSchema": {
    "type": "object",
    "properties": {
      "grade_min": {"type": "integer", "min": 1, "max": 15},
      "grade_max": {"type": "integer", "min": 1, "max": 15},
      "occ_series": {"type": "string", "pattern": "^\\d{4}$"},
      "org_code": {"type": "string"}
    },
    "required": ["grade_min", "grade_max"]
  }
}
```

**Tool Handler Pattern:**

```csharp
public class StagePdsToolHandler : IToolHandler
{
    private readonly IStagingOrchestrator _staging;
    
    public async Task<string> HandleAsync(Dictionary<string, object> input)
    {
        var gradeMin = new Grade((int)input["grade_min"]);
        var gradeMax = new Grade((int)input["grade_max"]);
        var series = input.ContainsKey("occ_series") 
            ? new OccupationalSeries((string)input["occ_series"]) 
            : null;
        var orgCode = input.ContainsKey("org_code") ? (string)input["org_code"] : null;
        
        var filter = new StagingFilter(gradeMin, gradeMax, series, orgCode);
        var runId = await _staging.StagePds(filter);
        
        return JsonConvert.SerializeObject(new { run_id = runId, staged_count = 47 });
    }
}
```

**Definition of Done:**
- MCP server starts, tools register without errors
- Each tool can be invoked via test client
- Responses return valid JSON
- Tool errors are caught and logged (don't crash server)

---

### **Phase 6: Configuration, DI & Program.cs** (0.5 day)

**Deliverables:**
- [ ] Full DI registration in `Program.cs`
- [ ] Serilog configuration (console + file)
- [ ] Load appsettings.json + environment overrides
- [ ] MCP host initialization
- [ ] Error handling middleware

**Program.cs Template:**

```csharp
var host = new HostBuilder()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
            .AddEnvironmentVariables();
    })
    .UseSerilog((context, logger) =>
    {
        logger
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/spc-.log", rollingInterval: RollingInterval.Day)
            .Enrich.FromLogContext();
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;
        
        // Config objects
        services.Configure<AiSettings>(config.GetSection("AI"));
        services.Configure<OutputSettings>(config.GetSection("Output"));
        
        // Storage abstraction (file system for dev, database for prod)
        services.Configure<StorageSettings>(config.GetSection("Storage"));
        var storageProvider = config.GetValue<string>("Storage:Provider", "FileSystem");
        if (storageProvider == "Database")
        {
            services.AddSingleton<IStorageProvider, DbStorageProvider>();
        }
        else
        {
            services.AddSingleton<IStorageProvider, FileSystemStorageProvider>();
        }
        
        // Repositories
        services.AddSingleton<IPositionDescriptionRepository, OraclePositionDescriptionRepository>();
        services.AddSingleton<ISchedulePCEvalRepository, OracleSchedulePCEvalRepository>();
        services.AddSingleton<IRunMetadataRepository, OracleRunMetadataRepository>();
        
        // AI
        services.AddSingleton<IAiClientFactory, AiClientFactory>();
        
        // Export & Documents
        services.AddSingleton<IExcelExporter, ClosedXmlExcelExporter>();
        services.AddSingleton<IDocumentGenerator, WordDocumentGenerator>();
        
        // Application Services
        services.AddSingleton<IStagingOrchestrator, StagingOrchestrator>();
        services.AddSingleton<IReportingService, ReportingService>();
        services.AddSingleton<IScoringOrchestrator, ScoringOrchestrator>();
        services.AddSingleton<IProcessingStatusService, ProcessingStatusService>();
        services.AddSingleton<IDocumentGenerationOrchestrator, DocumentGenerationOrchestrator>();
        services.AddSingleton<IExportOrchestrator, ExportOrchestrator>();
        
        // MCP
        services.AddSingleton<McpToolsProvider>();
    })
    .Build();

await host.RunAsync();
```

**Definition of Done:**
- `dotnet run` starts without errors
- Logs output to console and file
- All services are injectable and not null

---

### **Phase 7: Testing** (0.5 day)

**Deliverables:**
- [ ] Unit test suite for domain, application, infrastructure
- [ ] Integration test for full workflow (stage → report → process → status → documents → export)
- [ ] Mock Oracle repository for unit tests
- [ ] Test data fixtures

**Test Coverage Target:**
- Domain: 100% (validation rules)
- Application Services: 90%+ (orchestrators, status service)
- Infrastructure: 80%+ (repos with mocks, AI clients with mocks)
- MCP Tools: Smoke tests (each tool invokes orchestrator)

**Integration Test Skeleton:**

```csharp
[Fact]
public async Task FullWorkflow_StagesToExport()
{
    // 1. Stage PDs
    var runId = await _stagingOrch.StagePds(new StagingFilter(...));
    Assert.NotNull(runId);
    
    // 2. Get report
    var report = await _reportingService.GetReport(runId);
    Assert.NotEmpty(report.BySeriesStatus);
    
    // 3. Process by series
    _scoringOrch.ProcessPdsBySeriesAsync(runId, new[] { "0110" });
    await Task.Delay(5000); // wait for async
    
    // 4. Get status
    var status = await _statusService.GetStatus(runId);
    Assert.True(status.Overall["Complete"] > 0);
    
    // 5. Generate docs
    var docResp = await _docGenOrch.GenerateDocuments(runId, "all");
    Assert.True(File.Exists(docResp.OutputPath));
    
    // 6. Export
    var excelResp = await _exportOrch.ExportResults(runId, "xlsx");
    Assert.True(File.Exists(excelResp.FilePath));
}
```

**Definition of Done:**
- `dotnet test` passes all tests
- Coverage report shows 80%+ overall
- No flaky tests (deterministic behavior)

---

## 🔧 Technical Decisions & Trade-offs

| Decision | Rationale | Alternative |
|----------|-----------|-------------|
| **Async Scoring** | Fire-and-forget; don't block MCP responses | Blocking scoring (simpler, slower) |
| **In-Memory Status Tracker** | Fast status queries; survives process restart via DB | Pure DB queries (slower) |
| **Repository Pattern** | Testable; can swap Oracle for mock/other DB | Direct SQL in orchestrators (tightly coupled) |
| **Value Objects** | Self-validating, immutable; strong domain model | Primitives + manual validation |
| **Serilog** | Structured logging with enrichment (RunId, PdNbr) | Console.WriteLine (no context) |
| **ClosedXML** | Mature, no Office dependency, simple API | NPOI (older), Open XML (lower-level) |
| **appsettings.json** | Familiar .NET pattern; env overrides | hardcoded config |

---

## 🏗️ Storage Abstraction: File System → Database Migration Path

**Current (Dev):** Files saved locally to `C:\apps\schedulepc\reports\{RunId}\`  
**Future (Server/Prod):** Files stored as BLOBs in Oracle with metadata in SCHEDULE_PC_ARTIFACTS

### Design: IStorageProvider Interface

Instead of direct file I/O in exporters/generators, use an abstraction:

```csharp
// Infrastructure/Storage/IStorageProvider.cs
public interface IStorageProvider
{
    /// <summary>Save artifact (Word form, Excel tracker) to storage</summary>
    Task SaveAsync(string runId, string artifactType, string fileName, byte[] content, 
                   Dictionary<string, string>? metadata = null);
    
    /// <summary>Retrieve artifact from storage</summary>
    Task<(byte[] Content, Dictionary<string, string> Metadata)?> RetrieveAsync(string runId, string artifactType, string fileName);
    
    /// <summary>List all artifacts for a run</summary>
    Task<List<ArtifactInfo>> ListAsync(string runId, string? artifactType = null);
    
    /// <summary>Delete artifact or entire run</summary>
    Task DeleteAsync(string runId, string? artifactType = null, string? fileName = null);
}

// Infrastructure/Storage/ArtifactInfo.cs
public class ArtifactInfo
{
    public string RunId { get; set; }
    public string ArtifactType { get; set; }  // "Word", "Excel", "DataJson"
    public string FileName { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}
```

### Implementation 1: FileSystemStorageProvider (Dev/Now)

```csharp
public class FileSystemStorageProvider : IStorageProvider
{
    private readonly OutputSettings _output;
    
    public async Task SaveAsync(string runId, string artifactType, string fileName, byte[] content, 
                                 Dictionary<string, string>? metadata = null)
    {
        var folder = artifactType switch
        {
            "Word" => _output.GetWordDirectory(runId),
            "Excel" => _output.GetExcelDirectory(runId),
            "DataJson" => _output.GetDataJsonDirectory(runId),
            _ => throw new ArgumentException($"Unknown artifact type: {artifactType}")
        };
        
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(path, content);
        
        // Optionally save metadata to .json sidecar
        if (metadata != null)
        {
            var metaPath = Path.Combine(folder, $"{fileName}.meta.json");
            await File.WriteAllTextAsync(metaPath, JsonConvert.SerializeObject(metadata));
        }
    }
    
    public async Task<(byte[] Content, Dictionary<string, string> Metadata)?> RetrieveAsync(
        string runId, string artifactType, string fileName)
    {
        var folder = GetFolderForType(artifactType);
        var path = Path.Combine(folder, fileName);
        
        if (!File.Exists(path))
            return null;
        
        var content = await File.ReadAllBytesAsync(path);
        var metadata = new Dictionary<string, string>();
        
        var metaPath = Path.Combine(folder, $"{fileName}.meta.json");
        if (File.Exists(metaPath))
        {
            var metaJson = await File.ReadAllTextAsync(metaPath);
            metadata = JsonConvert.DeserializeObject<Dictionary<string, string>>(metaJson) ?? new();
        }
        
        return (content, metadata);
    }
    
    // ... other methods omitted for brevity
}
```

### Implementation 2: DbStorageProvider (Future/Server)

```csharp
public class DbStorageProvider : IStorageProvider
{
    private readonly IDbConnection _connection;
    private readonly ILogger<DbStorageProvider> _logger;
    
    public async Task SaveAsync(string runId, string artifactType, string fileName, byte[] content,
                                 Dictionary<string, string>? metadata = null)
    {
        // INSERT or UPDATE SCHEDULE_PC_ARTIFACTS table
        const string sql = @"
            INSERT INTO SCHEDULE_PC_ARTIFACTS (run_id, artifact_type, file_name, content, metadata_json, created_at)
            VALUES (:runId, :type, :fileName, :content, :metadata, SYSDATE)
        ";
        
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.Add(":runId", runId);
            cmd.Parameters.Add(":type", artifactType);
            cmd.Parameters.Add(":fileName", fileName);
            cmd.Parameters.Add(":content", content);  // BLOB column
            cmd.Parameters.Add(":metadata", metadata != null ? JsonConvert.SerializeObject(metadata) : null);
            
            await cmd.ExecuteNonQueryAsync();
        }
        
        _logger.LogInformation("Saved artifact to DB | RunId: {RunId} | Type: {Type} | File: {FileName} | Size: {Size}",
            runId, artifactType, fileName, content.Length);
    }
    
    public async Task<(byte[] Content, Dictionary<string, string> Metadata)?> RetrieveAsync(
        string runId, string artifactType, string fileName)
    {
        const string sql = @"
            SELECT content, metadata_json
            FROM SCHEDULE_PC_ARTIFACTS
            WHERE run_id = :runId AND artifact_type = :type AND file_name = :fileName
        ";
        
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.Add(":runId", runId);
            cmd.Parameters.Add(":type", artifactType);
            cmd.Parameters.Add(":fileName", fileName);
            
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    var content = (byte[])reader["content"];
                    var metadataJson = reader["metadata_json"] as string;
                    var metadata = metadataJson != null 
                        ? JsonConvert.DeserializeObject<Dictionary<string, string>>(metadataJson) ?? new()
                        : new Dictionary<string, string>();
                    
                    return (content, metadata);
                }
            }
        }
        
        return null;
    }
    
    // ... other methods omitted for brevity
}
```

### Integration with Exporters

**Before (Direct File I/O):**
```csharp
public class ClosedXmlExcelExporter : IExcelExporter
{
    public async Task ExportAsync(string runId, List<SchedulePCEvalRow> rows)
    {
        var ws = workbook.Worksheets.Add("Tracker");
        // ... populate cells ...
        
        var excelPath = Path.Combine(_outputSettings.GetExcelDirectory(runId), "Tracker.xlsx");
        workbook.SaveAs(excelPath);  // ❌ Direct file I/O
    }
}
```

**After (Using IStorageProvider):**
```csharp
public class ClosedXmlExcelExporter : IExcelExporter
{
    private readonly IStorageProvider _storage;
    
    public async Task ExportAsync(string runId, List<SchedulePCEvalRow> rows)
    {
        var ws = workbook.Worksheets.Add("Tracker");
        // ... populate cells ...
        
        using (var ms = new MemoryStream())
        {
            workbook.SaveAs(ms);
            var content = ms.ToArray();
            
            // ✅ Abstracted save (works with file system or database)
            await _storage.SaveAsync(runId, "Excel", $"SchedulePC_{runId}_Tracker.xlsx", content,
                new Dictionary<string, string>
                {
                    { "RowCount", rows.Count.ToString() },
                    { "Timestamp", DateTime.UtcNow.ToString("O") }
                });
        }
    }
}
```

### Configuration (appsettings.json)

```json
{
  "Storage": {
    "Provider": "FileSystem",  // or "Database" for prod
    "FileSystem": {
      "ReportsBaseDirectory": "C:\\apps\\schedulepc\\reports",
      "CreateSubdirectories": true
    },
    "Database": {
      "TableName": "SCHEDULE_PC_ARTIFACTS",
      "ConnectionString": "hr/HrUser_2026@//localhost:1521/XEPDB1"
    }
  }
}
```

### Database Schema (Future)

```sql
CREATE TABLE SCHEDULE_PC_ARTIFACTS (
    artifact_id    INTEGER PRIMARY KEY,
    run_id         VARCHAR2(50) NOT NULL,
    artifact_type  VARCHAR2(20),  -- 'Word', 'Excel', 'DataJson'
    file_name      VARCHAR2(255) NOT NULL,
    content        BLOB NOT NULL,
    metadata_json  CLOB,
    created_at     DATE,
    FOREIGN KEY (run_id) REFERENCES SCHEDULE_PC_EVAL(run_id)
);

CREATE INDEX idx_artifacts_runid ON SCHEDULE_PC_ARTIFACTS(run_id);
CREATE INDEX idx_artifacts_type ON SCHEDULE_PC_ARTIFACTS(artifact_type);
```

### Testing Storage Abstraction

```csharp
[Fact]
public async Task FileSystemStorageProvider_SaveAndRetrieve()
{
    var storage = new FileSystemStorageProvider(_output);
    var content = Encoding.UTF8.GetBytes("test content");
    var metadata = new Dictionary<string, string> { { "PdNbr", "D01880" } };
    
    // Save
    await storage.SaveAsync("RUN_001", "Excel", "test.xlsx", content, metadata);
    
    // Retrieve
    var result = await storage.RetrieveAsync("RUN_001", "Excel", "test.xlsx");
    Assert.NotNull(result);
    Assert.Equal(content, result.Value.Content);
    Assert.Equal("D01880", result.Value.Metadata["PdNbr"]);
}

[Fact]
public async Task DbStorageProvider_SaveAndRetrieve()
{
    var storage = new DbStorageProvider(_connection);
    var content = Encoding.UTF8.GetBytes("test content");
    
    // Save (in test, use real Oracle test DB)
    await storage.SaveAsync("RUN_001", "Excel", "test.xlsx", content);
    
    // Retrieve
    var result = await storage.RetrieveAsync("RUN_001", "Excel", "test.xlsx");
    Assert.NotNull(result);
    Assert.Equal(content, result.Value.Content);
}
```

### Benefits

✅ **Immediate (Dev):** Fast file system writes, no database dependency  
✅ **Future (Server):** Seamless migration to BLOB storage; just change config + implement DbStorageProvider  
✅ **Testability:** Mock IStorageProvider in unit tests  
✅ **No Code Changes:** Exporters/generators don't know which storage backend is used  
✅ **Scalability:** Server can store multiple runs' artifacts without filling disk  

---

## 🚀 Deployment Checklist

**Dev Deployment (File System Storage):**
- [ ] Build release: `dotnet publish -c Release`
- [ ] Update appsettings.json with dev settings:
  - Storage:Provider = "FileSystem"
  - Storage:FileSystem:ReportsBaseDirectory = "C:\apps\schedulepc\reports"
- [ ] Create `C:\apps\schedulepc\reports` directory
- [ ] Create `C:\apps\schedulepc\logs` directory
- [ ] Verify Oracle connection string (hr_local)
- [ ] Test Ollama endpoint (or Azure OpenAI) connectivity
- [ ] Run integration tests
- [ ] Start MCP server: `dotnet SchedulePCMcp.dll`

**Server Deployment (Database Storage - Future):**
- [ ] Create SCHEDULE_PC_ARTIFACTS table in Oracle
- [ ] Create indexes on (run_id, artifact_type)
- [ ] Update appsettings.json:
  - Storage:Provider = "Database"
  - Storage:Database:ConnectionString = "{prod Oracle connection}"
- [ ] Run `dotnet publish -c Release`
- [ ] Verify artifact retrieval from DB
- [ ] Start MCP server with prod config

---

## 📊 Metrics & Monitoring

**Log every:**
- Tool invocation (tool name, input, duration, result/error)
- PD staging (run_id, filter, count, duration)
- PD scoring (runId, pdNbr, score, duration, LLM tokens)
- Document generation (runId, count, duration, output_path)
- Export (runId, row_count, duration, file_path)

**Example log:**
```
[INFO] StagingOrchestrator: Staged 47 PDs in run RUN_2026_0809_001 (filter: grades 13-15, series=0110) | Duration: 2.3s | Connection: hr_local
[INFO] ScoringOrchestrator: Scored PD D01880 | RunId: RUN_2026_0809_001 | Rating: GS-14 | Duration: 3.1s | Tokens: 892
[INFO] DocumentGenerationOrchestrator: Generated 44 Word forms | RunId: RUN_2026_0809_001 | OutputPath: C:\apps\schedulepc\reports\RUN_2026_0809_001\Word\ | Duration: 8.5s
[INFO] ExportOrchestrator: Exported tracker | RunId: RUN_2026_0809_001 | RowCount: 47 | FilePath: C:\apps\schedulepc\reports\RUN_2026_0809_001\Excel\SchedulePC_RUN_2026_0809_001_Tracker.xlsx | Duration: 1.2s
```

---

## 🎓 Learning Resources

- MCP Protocol: `https://github.com/modelcontextprotocol/specification`
- Clean Architecture (Uncle Bob): Domain → Application → Infrastructure
- Repository Pattern: Mediating between Domain & Data layers
- Value Objects (Domain-Driven Design): Self-validating, immutable aggregates

---

## ✅ Sign-Off

**Prepared by:** Schedule PC Team  
**Date:** 2026-08-09  
**Status:** Ready for Phase 1 implementation  

**Next Step:** Begin Phase 1 (scaffolding) — create project, add dependencies, stub domain objects.
