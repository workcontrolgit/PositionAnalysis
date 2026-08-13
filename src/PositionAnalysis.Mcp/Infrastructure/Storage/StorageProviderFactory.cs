using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Infrastructure.Config;

namespace PositionAnalysis.Mcp.Infrastructure.Storage;

/// <summary>
/// Factory that creates storage provider instances based on configuration
/// Supports both FileSystem (dev) and Database (server) implementations
/// </summary>
public class StorageProviderFactory : IStorageProviderFactory
{
    private readonly StorageSettings _settings;
    private readonly ILogger<StorageProviderFactory> _logger;
    private readonly IServiceProvider _serviceProvider;

    public StorageProviderFactory(
        IOptions<StorageSettings> options,
        ILogger<StorageProviderFactory> logger,
        IServiceProvider serviceProvider)
    {
        _settings = options.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public IStorageProvider CreateStorageProvider()
    {
        var provider = _settings.Provider.ToLowerInvariant();
        
        _logger.LogInformation("Creating storage provider: {Provider}", provider);

        return provider switch
        {
            "filesystem" => CreateFileSystemProvider(),
            "database" => CreateDatabaseProvider(),
            _ => throw new InvalidOperationException($"Unknown storage provider: {_settings.Provider}")
        };
    }

    private IStorageProvider CreateFileSystemProvider()
    {
        _logger.LogInformation("Initializing FileSystemStorageProvider at {Path}", 
            _settings.FileSystem.ReportsBaseDirectory);
        
        var logger = _serviceProvider.GetRequiredService<ILogger<FileSystemStorageProvider>>();
        return new FileSystemStorageProvider(_settings, logger);
    }

    private IStorageProvider CreateDatabaseProvider()
    {
        // TODO: Implement DbStorageProvider when database schema is ready
        throw new NotImplementedException("Database storage provider not yet implemented");
        
        // var logger = _serviceProvider.GetRequiredService<ILogger<DbStorageProvider>>();
        // var oracleSettings = _serviceProvider.GetRequiredService<IOptions<OracleSettings>>();
        // return new DbStorageProvider(_settings, oracleSettings.Value, logger);
    }
}
