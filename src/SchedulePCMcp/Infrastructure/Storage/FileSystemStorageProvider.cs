using System.Text.Json;
using Microsoft.Extensions.Logging;
using SchedulePCMcp.Infrastructure.Config;

namespace SchedulePCMcp.Infrastructure.Storage;

/// <summary>
/// File system storage implementation - stores artifacts in local directories
/// Used for development; production can swap to database implementation
/// </summary>
public class FileSystemStorageProvider : IStorageProvider
{
    private readonly string _baseDirectory;
    private readonly ILogger<FileSystemStorageProvider> _logger;

    public FileSystemStorageProvider(StorageSettings storageSettings, ILogger<FileSystemStorageProvider> logger)
    {
        _baseDirectory = storageSettings.FileSystem.ReportsBaseDirectory;
        _logger = logger;

        Directory.CreateDirectory(_baseDirectory);
        _logger.LogInformation("FileSystemStorageProvider initialized at {BaseDirectory}", _baseDirectory);
    }

    public async Task SaveAsync(
        string runId,
        string artifactType,
        string fileName,
        byte[] content,
        Dictionary<string, string>? metadata = null)
    {
        var runDir = Path.Combine(_baseDirectory, runId);
        var typeDir = Path.Combine(runDir, artifactType);
        Directory.CreateDirectory(typeDir);

        var filePath = Path.Combine(typeDir, fileName);
        
        // Save binary content
        await File.WriteAllBytesAsync(filePath, content);
        _logger.LogInformation("Saved {ArtifactType} artifact: {FilePath} ({ByteCount} bytes)", 
            artifactType, filePath, content.Length);

        // Save metadata if provided
        if (metadata != null && metadata.Any())
        {
            var metadataPath = Path.Combine(typeDir, $"{fileName}.metadata.json");
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataPath, json);
            _logger.LogDebug("Saved metadata for {FileName}", fileName);
        }
    }

    public async Task<StorageArtifact?> RetrieveAsync(string runId, string artifactType, string fileName)
    {
        var filePath = Path.Combine(_baseDirectory, runId, artifactType, fileName);
        
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Artifact not found: {FilePath}", filePath);
            return null;
        }

        var content = await File.ReadAllBytesAsync(filePath);
        
        // Load metadata if available
        var metadata = new Dictionary<string, string>();
        var metadataPath = Path.Combine(_baseDirectory, runId, artifactType, $"{fileName}.metadata.json");
        if (File.Exists(metadataPath))
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (loaded != null)
                metadata = loaded;
        }

        _logger.LogInformation("Retrieved {ArtifactType} artifact: {FileName} ({ByteCount} bytes)", 
            artifactType, fileName, content.Length);

        return new StorageArtifact(content, metadata);
    }

    public Task<List<string>> ListAsync(string runId, string artifactType)
    {
        var typeDir = Path.Combine(_baseDirectory, runId, artifactType);
        
        if (!Directory.Exists(typeDir))
            return Task.FromResult(new List<string>());

        var files = Directory.GetFiles(typeDir)
            .Where(f => !f.EndsWith(".metadata.json"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        _logger.LogInformation("Listed {Count} artifacts in {RunId}/{ArtifactType}", 
            files.Count, runId, artifactType);

        return Task.FromResult(files);
    }

    public Task DeleteAsync(string runId, string artifactType, string fileName)
    {
        var filePath = Path.Combine(_baseDirectory, runId, artifactType, fileName);
        
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Deleted artifact: {FilePath}", filePath);
        }

        var metadataPath = Path.Combine(_baseDirectory, runId, artifactType, $"{fileName}.metadata.json");
        if (File.Exists(metadataPath))
            File.Delete(metadataPath);

        return Task.CompletedTask;
    }
}
