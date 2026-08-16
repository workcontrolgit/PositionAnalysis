using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using PositionAnalysis.Mcp.MCP;
using PositionAnalysis.Mcp.MCP.Tools;
using System.Text.Json;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class ProcessAllRunStatusServiceTests
{
    [Fact]
    public async Task ProcessAllHandler_InstructsCallersToPollQueueStatus()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPositionAnalysisEvalRepository>(_ => new FakeEvalRepository());
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var handler = new RunUnattendedScoringToolHandler(
            new NoOpScoringOrchestrator(),
            new ProcessAllRunStatusService(),
            scopeFactory,
            new NeverGateCostGateService(),
            NullLogger<RunUnattendedScoringToolHandler>.Instance);

        using var arguments = JsonDocument.Parse("{}");

        var response = await handler.InvokeAsync(arguments.RootElement, CancellationToken.None);
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Contains("get_unattended_queue_status", responseDocument.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_CapturesBackgroundOperationFailure()
    {
        var service = new ProcessAllRunStatusService();

        await service.StartAsync(() => Task.FromException(new InvalidOperationException("worker crashed")));

        var status = service.GetStatus();
        Assert.True(status.HasFailed);
        Assert.False(status.IsActive);
        Assert.Equal("worker crashed", status.Error);
    }

    private sealed class NoOpScoringOrchestrator : Application.Interfaces.IScoringOrchestrator
    {
        public Task ScoreAsync(string pdNbr) => Task.CompletedTask;
        public Task ScoreAllAsync() => Task.CompletedTask;
        public Task RescoreBySeriesAsync(IEnumerable<string> series) => Task.CompletedTask;
        public Task<int> RebucketRatingsAsync(IEnumerable<string>? series = null, IEnumerable<string>? pdNumbers = null) => Task.FromResult(0);
        public Task<Domain.Entities.EvaluationResult?> GetResultAsync(string pdNbr) => Task.FromResult<Domain.Entities.EvaluationResult?>(null);
    }

    private sealed class NeverGateCostGateService : ICostGateService
    {
        public decimal ThresholdUsd => decimal.MaxValue;
        public decimal Estimate(int pdCount) => 0m;
        public bool RequiresConfirmation(decimal estimatedCost) => false;
    }

    private sealed class FakeEvalRepository : IPositionAnalysisEvalRepository
    {
        public Task<string> InsertAsync(Domain.Entities.EvaluationResult result) => Task.FromResult("inserted");
        public Task UpdateAsync(Domain.Entities.EvaluationResult result) => Task.CompletedTask;
        public Task<int> DeleteAllAsync() => Task.FromResult(0);
        public Task<Domain.Entities.StagingResult> StageFromMaxPdAsync(Domain.ValueObjects.StagingFilter filter) => Task.FromResult(new Domain.Entities.StagingResult(0, 0));
        public Task<List<Infrastructure.Repositories.SeriesCounts>> GetSeriesCountsAsync() => Task.FromResult(new List<Infrastructure.Repositories.SeriesCounts>());
        public Task<Domain.Entities.EvaluationResult?> GetByPdAsync(string pdNbr) => Task.FromResult<Domain.Entities.EvaluationResult?>(null);
        public Task<List<Domain.Entities.EvaluationResult>> GetAllAsync() => Task.FromResult(new List<Domain.Entities.EvaluationResult>());
        public Task<List<Domain.Entities.EvaluationResult>> GetBySeriesAsync(Domain.ValueObjects.OccupationalSeries series) => Task.FromResult(new List<Domain.Entities.EvaluationResult>());
        public Task<List<Domain.Entities.EvaluationResult>> GetByStatusAsync(Domain.Enums.EvaluationStatus status) => Task.FromResult(new List<Domain.Entities.EvaluationResult>());
        public Task<int> GetCountByStatusAsync(Domain.Enums.EvaluationStatus status) => Task.FromResult(0);
        public Task<int> ResetFailedAsync() => Task.FromResult(0);
        public Task UpdateRatingAsync(string pdNbr, Domain.ValueObjects.OccupationalSeries series, string rating, bool isCandidate) => Task.CompletedTask;
    }
}
