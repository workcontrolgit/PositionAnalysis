using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Application.Services;
using PositionAnalysis.Mcp.Infrastructure.Config;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using PositionAnalysis.Mcp.Infrastructure.Storage;
using PositionAnalysis.Mcp.Infrastructure.AiClients;
using PositionAnalysis.Mcp.Infrastructure.DocumentGeneration;
using PositionAnalysis.Mcp.MCP;
using PositionAnalysis.Mcp.MCP.Tools;

namespace PositionAnalysis.Mcp;

/// <summary>
/// Schedule PC MCP Server
/// Orchestrates the complete evaluation pipeline: staging → reporting → scoring → document generation → export
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();
            
            Log.Information("=== Schedule PC MCP Server Starting ===");
            Log.Information("Environment: {Environment}", host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
            
            // TODO: Initialize MCP server and register tools
            // var mcpServer = host.Services.GetRequiredService<IMcpServer>();
            // await mcpServer.InitializeAsync();
            
            Log.Information("Schedule PC MCP Server initialized successfully");
            await host.RunAsync();
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal startup error: {ex}");
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                        .AddUserSecrets<Program>(optional: true)
                    .AddEnvironmentVariables();

                // Always write logs under the Mcp project's own logs/ folder, regardless of
                // the process's working directory (the CLI launches this from bin/Debug/...).
                var logFilePath = Path.Combine(ResolveProjectLogsDirectory(), "schedulepcmcp-.log");
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Serilog:WriteTo:1:Args:path"] = logFilePath
                });
            })
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.Configure<OracleSettings>(context.Configuration.GetSection("Oracle"));
                services.Configure<AiSettings>(context.Configuration.GetSection("AiProvider"));
                services.Configure<StorageSettings>(context.Configuration.GetSection("Storage"));
                services.Configure<DocumentGenerationSettings>(context.Configuration.GetSection("DocumentGeneration"));
                services.Configure<ExcelExportSettings>(context.Configuration.GetSection("ExcelExport"));
                services.Configure<McpSettings>(context.Configuration.GetSection("MCP"));
                services.Configure<RatingThresholdSettings>(context.Configuration.GetSection("RatingThresholds"));

                // Some services require the concrete settings object, not only IOptions<T>.
                services.AddSingleton(sp =>
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageSettings>>().Value);

                // Output directory configuration
                services.AddSingleton<OutputSettings>();

                // Storage Provider Factory
                services.AddSingleton<IStorageProviderFactory, StorageProviderFactory>();
                services.AddSingleton(sp =>
                {
                    var factory = sp.GetRequiredService<IStorageProviderFactory>();
                    return factory.CreateStorageProvider();
                });

                // Document Generation Strategy Factory
                services.AddSingleton<IDocumentGenerationStrategyFactory, DocumentGenerationStrategyFactory>();
                services.AddSingleton(sp =>
                {
                    var factory = sp.GetRequiredService<IDocumentGenerationStrategyFactory>();
                    return factory.CreateStrategy();
                });

                // Repositories
                services.AddScoped<IPositionDescriptionRepository, OraclePositionDescriptionRepository>();
                services.AddScoped<IPositionAnalysisEvalRepository, OraclePositionAnalysisEvalRepository>();

                // AI Clients
                services.AddSingleton<IAiClient>(sp =>
                {
                    var aiSettings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiSettings>>();
                    return aiSettings.Value.Type.ToLowerInvariant() switch
                    {
                        "ollama" => sp.GetRequiredService<OllamaAiClient>(),
                        "azureopenai" => sp.GetRequiredService<AzureOpenAiClient>(),
                        _ => throw new InvalidOperationException($"Unknown AI provider: {aiSettings.Value.Type}")
                    };
                });
                services.AddSingleton<OllamaAiClient>();
                services.AddSingleton<AzureOpenAiClient>();

                // Application Services/Orchestrators
                services.AddScoped<IStagingOrchestrator, StagingOrchestrator>();
                services.AddScoped<IScoringOrchestrator, ScoringOrchestrator>();
                services.AddScoped<IReportingService, ReportingService>();
                services.AddScoped<IProcessingStatusService, ProcessingStatusService>();
                services.AddScoped<IDocumentGenerationOrchestrator, DocumentGenerationOrchestrator>();
                services.AddScoped<IExportOrchestrator, ExportOrchestrator>();
                services.AddSingleton<ProcessAllRunStatusService>();

                // MCP Tool Handlers
                services.AddScoped<IMcpToolHandler, StagePdsToolHandler>();
                services.AddScoped<IMcpToolHandler, RetryFailedPdsToolHandler>();
                services.AddScoped<IMcpToolHandler, RescorePdToolHandler>();
                services.AddScoped<IMcpToolHandler, RescorePdsBySeriesToolHandler>();
                services.AddScoped<IMcpToolHandler, RescoreAllPdsToolHandler>();
                services.AddScoped<IMcpToolHandler, RescoreFlaggedPdsToolHandler>();
                services.AddScoped<IMcpToolHandler, RescoreFlaggedPdsBySeriesToolHandler>();
                services.AddScoped<IMcpToolHandler, RescoreFlaggedPdsByPdToolHandler>();
                services.AddScoped<IMcpToolHandler, GetNeedsRescoreCountToolHandler>();
                services.AddScoped<IMcpToolHandler, RebucketRatingsToolHandler>();
                services.AddScoped<IMcpToolHandler, RescoreHumanSchedulePcPdsToolHandler>();
                services.AddScoped<IMcpToolHandler, ClearSchedulePcEvalToolHandler>();
                services.AddScoped<IMcpToolHandler, GetStagingReportToolHandler>();
                services.AddScoped<IMcpToolHandler, ProcessPdsBySeriesToolHandler>();
                services.AddScoped<IMcpToolHandler, ProcessAllPdsToolHandler>();
                services.AddScoped<IMcpToolHandler, GetProcessingStatusToolHandler>();
                services.AddScoped<IMcpToolHandler, GetProcessingStatusBySeriesToolHandler>();
                services.AddScoped<IMcpToolHandler, GetProcessingStatusByOrgsToolHandler>();
                services.AddScoped<IMcpToolHandler, GetProcessingStatusByPdToolHandler>();
                services.AddScoped<IMcpToolHandler, GetQueueStatusToolHandler>();
                services.AddScoped<IMcpToolHandler, GenerateDocumentsToolHandler>();
                services.AddScoped<IMcpToolHandler, GenerateDocumentsBySeriesToolHandler>();
                services.AddScoped<IMcpToolHandler, GenerateDocumentsByOrgsToolHandler>();
                services.AddScoped<IMcpToolHandler, GenerateDocumentsByPdToolHandler>();
                services.AddScoped<IMcpToolHandler, ExportResultsToolHandler>();
                services.AddScoped<IMcpToolHandler, ExportResultsBySeriesToolHandler>();
                services.AddScoped<IMcpToolHandler, ExportResultsByOrgsToolHandler>();
                services.AddScoped<IMcpToolHandler, ExportResultsByPdToolHandler>();
                services.AddSingleton<McpToolsProvider>();

                // MCP Stdio Host
                services.AddHostedService<McpStdioServer>();
            })
            .UseSerilog((context, services, config) =>
            {
                config
                    .ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "PositionAnalysis.Mcp");
            });

    // Walks up from the running assembly's directory to find the Mcp project folder,
    // so logs land in src/PositionAnalysis.Mcp/logs even when launched from bin/Debug/....
    private static string ResolveProjectLogsDirectory()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "PositionAnalysis.Mcp.csproj")))
                return Path.Combine(dir, "logs");

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "logs");
    }
}
