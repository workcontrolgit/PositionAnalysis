using SchedulePCMcp.MCP;
using SchedulePCMcp.MCP.Tools;
using System.Text.Json;
using Xunit;

namespace SchedulePCMcp.Tests;

public class ProcessAllRunStatusServiceTests
{
    [Fact]
    public async Task ProcessAllHandler_InstructsCallersToPollQueueStatus()
    {
        var handler = new ProcessAllPdsToolHandler(new NoOpScoringOrchestrator(), new ProcessAllRunStatusService());
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
        public Task ScoreBySeriesAsync(IEnumerable<string> series) => Task.CompletedTask;
        public Task ScoreAllAsync() => Task.CompletedTask;
        public Task RescoreBySeriesAsync(IEnumerable<string> series) => Task.CompletedTask;
        public Task RescoreAllAsync() => Task.CompletedTask;
        public Task<Domain.Entities.EvaluationResult?> GetResultAsync(string pdNbr) => Task.FromResult<Domain.Entities.EvaluationResult?>(null);
    }
}