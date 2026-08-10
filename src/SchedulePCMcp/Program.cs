using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Application.Services;
using SchedulePCMcp.Infrastructure.Config;
using SchedulePCMcp.Infrastructure.Repositories;
using SchedulePCMcp.Infrastructure.Storage;
using SchedulePCMcp.Infrastructure.AiClients;
using SchedulePCMcp.Infrastructure.DocumentGeneration;
using SchedulePCMcp.MCP;
using SchedulePCMcp.MCP.Tools;

namespace SchedulePCMcp;

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
                services.AddScoped<ISchedulePCEvalRepository, OracleSchedulePCEvalRepository>();

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

                // MCP Tool Handlers
                services.AddScoped<IMcpToolHandler, StagePdsToolHandler>();
                services.AddScoped<IMcpToolHandler, RetryFailedPdsToolHandler>();
                services.AddScoped<IMcpToolHandler, RescorePdToolHandler>();
                services.AddScoped<IMcpToolHandler, RescorePdsBySeriesToolHandler>();
                services.AddScoped<IMcpToolHandler, RescoreAllPdsToolHandler>();
                services.AddScoped<IMcpToolHandler, ClearSchedulePcEvalToolHandler>();
                services.AddScoped<IMcpToolHandler, GetStagingReportToolHandler>();
                services.AddScoped<IMcpToolHandler, ProcessPdsBySeriesToolHandler>();
                services.AddScoped<IMcpToolHandler, ProcessAllPdsToolHandler>();
                services.AddScoped<IMcpToolHandler, GetProcessingStatusToolHandler>();
                services.AddScoped<IMcpToolHandler, GenerateDocumentsToolHandler>();
                services.AddScoped<IMcpToolHandler, ExportResultsToolHandler>();
                services.AddSingleton<McpToolsProvider>();

                // MCP Stdio Host
                services.AddHostedService<McpStdioServer>();
            })
            .UseSerilog((context, services, config) =>
            {
                config
                    .ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "SchedulePCMcp");
            });
}
