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
        var handler = new RunUnattendedScoringToolHandler(new NoOpScoringOrchestrator(), new ProcessAllRunStatusService());
        using var arguments = JsonDocument.Parse("{}");

        var response = await handler.InvokeAsync(arguments.RootElement, CancellationToken.None);
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Contains("get_queue_status", responseDocument.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
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
        public Task RescoreAllAsync() => Task.CompletedTask;
        public Task RescoreFlaggedAsync() => Task.CompletedTask;
        public Task RescoreFlaggedBySeriesAsync(IEnumerable<string> series) => Task.CompletedTask;
        public Task RescoreFlaggedByPdAsync(IEnumerable<string> pdNumbers) => Task.CompletedTask;
        public Task<int> RebucketRatingsAsync(IEnumerable<string>? series = null, IEnumerable<string>? pdNumbers = null) => Task.FromResult(0);
        public Task<Domain.Entities.EvaluationResult?> GetResultAsync(string pdNbr) => Task.FromResult<Domain.Entities.EvaluationResult?>(null);
    }
}
