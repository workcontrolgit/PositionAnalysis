using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.MCP.Tools;
using Xunit;

namespace SchedulePCMcp.Tests;

public class StagePdsToolHandlerTests
{
    [Fact]
    public async Task InvokeAsync_AlwaysStagesGradesThirteenThroughFifteen()
    {
        var orchestrator = new CapturingStagingOrchestrator();
        var handler = new StagePdsToolHandler(orchestrator);
        using var document = JsonDocument.Parse("{\"gradeMin\":1,\"gradeMax\":15}");

        await handler.InvokeAsync(document.RootElement, CancellationToken.None);

        Assert.Equal(13, orchestrator.Filter!.GradeMin!.Value);
        Assert.Equal(15, orchestrator.Filter.GradeMax!.Value);
    }

    private sealed class CapturingStagingOrchestrator : IStagingOrchestrator
    {
        public StagingFilter? Filter { get; private set; }

        public Task<int> StageAsync(StagingFilter filter)
        {
            Filter = filter;
            return Task.FromResult(0);
        }
    }
}