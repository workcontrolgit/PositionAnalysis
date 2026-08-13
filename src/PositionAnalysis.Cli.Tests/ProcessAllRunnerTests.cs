using System.Text.Json;
using PositionAnalysis.Cli;
using Xunit;

namespace PositionAnalysis.Cli.Tests;

public class ProcessAllRunnerTests
{
    [Fact]
    public async Task RunAsync_TriggersScoringAndPollsAtConfiguredIntervalUntilDrained()
    {
        var client = new FakeMcpClient(
            QueueStatus(pending: 2, inProgress: 0, complete: 0, failed: 0, isDrained: false),
            QueueStatus(pending: 0, inProgress: 0, complete: 2, failed: 0, isDrained: true));
        var delays = new List<TimeSpan>();
        var interval = TimeSpan.FromSeconds(7);
        var runner = new ProcessAllRunner(client, interval, delay =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "process_all_pds", "get_queue_status", "get_queue_status" }, client.ToolCalls);
        Assert.Single(delays);
        Assert.Equal(interval, delays[0]);
    }

    [Fact]
    public async Task RunAsync_ReturnsOneWhenDrainedQueueContainsFailures()
    {
        var client = new FakeMcpClient(
            QueueStatus(pending: 0, inProgress: 0, complete: 1, failed: 1, isDrained: true));
        var runner = new ProcessAllRunner(client, TimeSpan.Zero, _ => Task.CompletedTask);

        var exitCode = await runner.RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Equal(1, client.ToolCalls.Count(name => name == "process_all_pds"));
    }

    [Fact]
    public async Task RunAsync_ReturnsOneImmediatelyWhenTheBackgroundRunFails()
    {
        var client = new FakeMcpClient(
            QueueStatus(pending: 1, inProgress: 0, complete: 0, failed: 0, isDrained: false, runFailed: true, runError: "worker crashed"));
        var runner = new ProcessAllRunner(client, TimeSpan.Zero, _ => Task.CompletedTask);

        var exitCode = await runner.RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Equal(new[] { "process_all_pds", "get_queue_status" }, client.ToolCalls);
    }

    [Fact]
    public async Task RunAsync_ReturnsTwoWhenCancelledDuringPolling()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FakeMcpClient(
            QueueStatus(pending: 1, inProgress: 0, complete: 0, failed: 0, isDrained: false));
        var runner = new ProcessAllRunner(client, TimeSpan.FromMinutes(1), (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(token);
        });

        var exitCode = await runner.RunAsync(cancellation.Token);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_RejectsDrainedStateThatContradictsOutstandingWork()
    {
        var client = new FakeMcpClient(
            QueueStatus(pending: 1, inProgress: 0, complete: 0, failed: 0, isDrained: true));
        var runner = new ProcessAllRunner(client, TimeSpan.Zero, _ => Task.CompletedTask);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Contains("isDrained", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RejectsResponseMissingRunFailedField()
    {
        var client = new FakeMcpClient(Json("""
            {"pending":0,"inProgress":0,"complete":1,"failed":0,"isDrained":true,"runError":null}
            """));
        var runner = new ProcessAllRunner(client, TimeSpan.Zero, _ => Task.CompletedTask);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Contains("runFailed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RejectsResponseWithInvalidPendingField()
    {
        var client = new FakeMcpClient(Json("""
            {"pending":"one","inProgress":0,"complete":0,"failed":0,"isDrained":false,"runFailed":false,"runError":null}
            """));
        var runner = new ProcessAllRunner(client, TimeSpan.Zero, _ => Task.CompletedTask);

            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());
    }

    private static JsonElement QueueStatus(
        int pending,
        int inProgress,
        int complete,
        int failed,
        bool isDrained,
        bool runFailed = false,
        string? runError = null)
    {
        var errorJson = runError is null ? "null" : JsonSerializer.Serialize(runError);
        using var document = JsonDocument.Parse($"{{\"pending\":{pending},\"inProgress\":{inProgress},\"complete\":{complete},\"failed\":{failed},\"isDrained\":{isDrained.ToString().ToLowerInvariant()},\"runFailed\":{runFailed.ToString().ToLowerInvariant()},\"runError\":{errorJson}}}");
        return document.RootElement.Clone();
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FakeMcpClient : ISchedulePcMcpClient
    {
        private readonly Queue<JsonElement> _queueStatuses;

        public FakeMcpClient(params JsonElement[] queueStatuses)
        {
            _queueStatuses = new Queue<JsonElement>(queueStatuses);
        }

        public List<string> ToolCalls { get; } = [];

        public Task<JsonElement> CallToolAsync(string name, object arguments)
        {
            ToolCalls.Add(name);

            return name switch
            {
                "process_all_pds" => Task.FromResult(EmptyResponse()),
                "get_queue_status" when _queueStatuses.Count > 0 => Task.FromResult(_queueStatuses.Dequeue()),
                _ => throw new InvalidOperationException($"Unexpected MCP tool call: {name}.")
            };
        }

        private static JsonElement EmptyResponse()
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
    }
}
