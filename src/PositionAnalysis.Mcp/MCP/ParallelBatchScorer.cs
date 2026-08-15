using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Config;

namespace PositionAnalysis.Mcp.MCP;

/// <summary>
/// Shared engine for all process_batch_* tools.
/// Encapsulates the Parallel.ForEachAsync loop so none of the three
/// handler classes duplicate it.
/// </summary>
public sealed class ParallelBatchScorer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParallelBatchScorer> _logger;
    private readonly int _concurrency;

    public ParallelBatchScorer(
        IServiceScopeFactory scopeFactory,
        IOptions<McpSettings> mcpSettings,
        ILogger<ParallelBatchScorer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _concurrency = Math.Max(1, mcpSettings.Value.BatchConcurrency);
    }

    /// <param name="pdNbrs">PD numbers to score.</param>
    /// <param name="reportProgressAsync">
    ///   Delegate wired to StdioChannel; null when the caller supplied no progressToken.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<BatchResult> RunAsync(
        IReadOnlyList<string> pdNbrs,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
    {
        if (pdNbrs.Count == 0)
            return new BatchResult(0, 0, 0, "No pending PDs found.");

        int total     = pdNbrs.Count;
        int completed = 0;
        int failed    = 0;

        _logger.LogInformation("ParallelBatchScorer: starting {Total} PDs (parallelism={Concurrency})", total, _concurrency);

        if (reportProgressAsync is not null)
            await reportProgressAsync(0, total);

        await Parallel.ForEachAsync(pdNbrs, new ParallelOptions
        {
            MaxDegreeOfParallelism = _concurrency,
            CancellationToken = cancellationToken
        },
        async (pdNbr, ct) =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IScoringOrchestrator>();
                await orchestrator.ScoreAsync(pdNbr);

                var done = Interlocked.Increment(ref completed);
                if (reportProgressAsync is not null)
                    await reportProgressAsync(done, total);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(
                    $"[BATCH-ERROR] PD={pdNbr} | {ex.GetType().Name}: {ex.Message}");
                _logger.LogError(ex, "Unexpected failure scoring PD {PdNbr}", pdNbr);
                Interlocked.Increment(ref failed);
            }
        });

        var summary = $"Batch complete: {completed} scored, {failed} failed out of {total}.";
        _logger.LogInformation(summary);

        return new BatchResult(total, completed, failed, summary);
    }
}

public sealed record BatchResult(int Total, int Completed, int Failed, string Message);
