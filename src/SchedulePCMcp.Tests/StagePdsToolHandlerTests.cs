using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.MCP.Tools;
using Xunit;

namespace SchedulePCMcp.Tests;

public class StagePdsToolHandlerTests
{
    [Fact]
    public async Task InvokeAsync_StagesGradesThirteenThroughFifteenAndReturnsStagingCounts()
    {
        var expectedResult = new StagingResult(7, 3);
        var orchestrator = new CapturingStagingOrchestrator(expectedResult);
        var handler = new StagePdsToolHandler(orchestrator);
        using var document = JsonDocument.Parse("{\"gradeMin\":1,\"gradeMax\":15}");

        var response = await handler.InvokeAsync(document.RootElement, CancellationToken.None);
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Equal(13, orchestrator.Filter!.GradeMin!.Value);
        Assert.Equal(15, orchestrator.Filter.GradeMax!.Value);
        Assert.Equal(expectedResult.StagedCount, responseDocument.RootElement.GetProperty("stagedCount").GetInt32());
        Assert.Equal(expectedResult.ExcludedWithoutDutiesCount, responseDocument.RootElement.GetProperty("excludedWithoutDutiesCount").GetInt32());
        Assert.Equal("staged", responseDocument.RootElement.GetProperty("status").GetString());
    }

    private sealed class CapturingStagingOrchestrator : IStagingOrchestrator
    {
        private readonly StagingResult _result;

        public CapturingStagingOrchestrator(StagingResult result)
        {
            _result = result;
        }

        public StagingFilter? Filter { get; private set; }

        public Task<StagingResult> StageAsync(StagingFilter filter)
        {
            Filter = filter;
            return Task.FromResult(_result);
        }
    }
}