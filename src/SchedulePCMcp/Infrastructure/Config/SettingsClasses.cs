namespace SchedulePCMcp.Infrastructure.Config;

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
    public double Temperature { get; set; } = 0.7;
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
