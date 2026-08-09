namespace SchedulePCMcp.Infrastructure.Storage;

/// <summary>
/// Result from storage retrieval operations
/// </summary>
public record StorageArtifact(byte[] Content, Dictionary<string, string> Metadata);

/// <summary>
/// Storage abstraction interface - allows pluggable implementations
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Save artifact (Word, Excel, JSON, etc.) to storage
    /// </summary>
    Task SaveAsync(
        string runId,
        string artifactType, // "Word", "Excel", "Json", etc.
        string fileName,
        byte[] content,
        Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Retrieve artifact from storage
    /// </summary>
    Task<StorageArtifact?> RetrieveAsync(string runId, string artifactType, string fileName);

    /// <summary>
    /// List all artifacts for a run
    /// </summary>
    Task<List<string>> ListAsync(string runId, string artifactType);

    /// <summary>
    /// Delete artifact from storage
    /// </summary>
    Task DeleteAsync(string runId, string artifactType, string fileName);
}

/// <summary>
/// Factory for creating storage provider instances based on configuration
/// </summary>
public interface IStorageProviderFactory
{
    IStorageProvider CreateStorageProvider();
}
