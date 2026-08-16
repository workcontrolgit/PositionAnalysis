namespace PositionAnalysis.Mcp.Infrastructure.Config;

/// <summary>
/// Oracle database connection settings
/// </summary>
public class OracleSettings
{
    public string ConnectionName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeout { get; set; } = 300;
}

/// <summary>
/// AI/LLM provider settings (Ollama or Azure OpenAI)
/// </summary>
public class AiSettings
{
    public string Type { get; set; } = "Ollama"; // Ollama or AzureOpenAI
    public OllamaSettings Ollama { get; set; } = new();
    public AzureOpenAiSettings AzureOpenAI { get; set; } = new();
}

public class OllamaSettings
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
}

public class AzureOpenAiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public int MaxCompletionTokens { get; set; } = 16384;
    public double Temperature { get; set; } = 0.7;
    /// <summary>Cost per 1,000 input tokens (USD). Overrides the built-in pricing table when non-zero.</summary>
    public decimal CostPerInputTokenK { get; set; } = 0m;
    /// <summary>Cost per 1,000 output tokens (USD). Overrides the built-in pricing table when non-zero.</summary>
    public decimal CostPerOutputTokenK { get; set; } = 0m;
}

/// <summary>
/// Thresholds mapping the count of triggered Schedule P/C criteria (0-4) to a rating bucket.
/// Changing these values only affects future scoring and rebucket_ratings runs; it does not
/// require rescoring already-evaluated PDs since the trigger flags are already stored.
/// </summary>
public class RatingThresholdSettings
{
    /// <summary>Minimum triggered-criteria count (out of 4) required for a HIGH rating.</summary>
    public int HighMinCriteriaTriggered { get; set; } = 3;
    /// <summary>Minimum triggered-criteria count (out of 4) required for a MEDIUM rating.</summary>
    public int MediumMinCriteriaTriggered { get; set; } = 1;

    public string RatingFor(int triggeredCount) => triggeredCount switch
    {
        var n when n >= HighMinCriteriaTriggered => "HIGH",
        var n when n >= MediumMinCriteriaTriggered => "MEDIUM",
        _ => "LOW"
    };
}

/// <summary>
/// Storage provider settings (FileSystem or Database)
/// </summary>
public class StorageSettings
{
    public string Provider { get; set; } = "FileSystem"; // FileSystem or Database
    public FileSystemStorageSettings FileSystem { get; set; } = new();
    public DatabaseStorageSettings Database { get; set; } = new();
}

public class FileSystemStorageSettings
{
    public string ReportsBaseDirectory { get; set; } = "C:\\apps\\schedulepc\\reports";
}

public class DatabaseStorageSettings
{
    public string BlobTableName { get; set; } = "SCHEDULE_PC_ARTIFACTS";
    public string MetadataTableName { get; set; } = "SCHEDULE_PC_ARTIFACTS_METADATA";
}

/// <summary>
/// Document generation settings (Word form templates)
/// </summary>
public class DocumentGenerationSettings
{
    public string Strategy { get; set; } = "OpenXml"; // OpenXml or PowerShell (legacy)
    public string TemplateFile { get; set; } = "C:\\apps\\schedulepc\\templates\\EvaluationForm-Template.docx";
    public string OutputPath { get; set; } = "C:\\apps\\schedulepc\\reports";
}

/// <summary>
/// Excel export settings
/// </summary>
public class ExcelExportSettings
{
    public string Implementation { get; set; } = "ClosedXML";
    public string OutputPath { get; set; } = "C:\\apps\\schedulepc\\reports";
}

/// <summary>
/// MCP server settings
/// </summary>
public class McpSettings
{
    public bool EnableDevLogging { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    /// <summary>Maximum parallel PDs scored simultaneously by process_batch_* tools.</summary>
    public int BatchConcurrency { get; set; } = 10;
}

/// <summary>
/// Output directory management
/// </summary>
public class OutputSettings
{
    private readonly string _reportsBaseDirectory;

    public OutputSettings(StorageSettings storageSettings)
    {
        var raw = storageSettings.FileSystem.ReportsBaseDirectory;
        _reportsBaseDirectory = Path.IsPathRooted(raw)
            ? raw
            : Path.GetFullPath(raw, AppContext.BaseDirectory);
    }

    public string GetExcelOutputPath(string fileName)
    {
        var dir = Path.Combine(_reportsBaseDirectory, "tracker-excel");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    public string GetWordOutputPath(string fileName)
    {
        var dir = Path.Combine(_reportsBaseDirectory, "form-word");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    public string GetLogsDirectory()
    {
        var logsDir = Path.Combine(_reportsBaseDirectory, "..", "logs");
        Directory.CreateDirectory(logsDir);
        return logsDir;
    }
}

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
