using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using PositionAnalysis.Mcp.MCP.Tools;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class RescoreHumanSchedulePcPdsToolHandlerTests
{
    [Fact]
    public async Task InvokeAsync_ScoresAllNinetyHumanFlaggedPdsInOrder()
    {
        var pdNumbers = Enumerable.Range(1, 90).Select(i => $"PD-{i:000}").ToList();
        var repository = new HumanPdRepository(pdNumbers);
        var orchestrator = new CapturingScoringOrchestrator();
        var handler = new RescoreHumanSchedulePcPdsToolHandler(repository, orchestrator);
        using var arguments = JsonDocument.Parse("{}");

        var response = await handler.InvokeAsync(arguments.RootElement, CancellationToken.None);
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Equal(90, responseDocument.RootElement.GetProperty("selectedCount").GetInt32());
        Assert.Equal(90, responseDocument.RootElement.GetProperty("scoredCount").GetInt32());
        Assert.Equal(0, responseDocument.RootElement.GetProperty("failedCount").GetInt32());
        Assert.Equal(pdNumbers, orchestrator.ScoredPdNumbers);
    }

    [Fact]
    public async Task InvokeAsync_RejectsWhenHumanFlaggedCountIsNotNinety()
    {
        var pdNumbers = Enumerable.Range(1, 89).Select(i => $"PD-{i:000}").ToList();
        var repository = new HumanPdRepository(pdNumbers);
        var orchestrator = new CapturingScoringOrchestrator();
        var handler = new RescoreHumanSchedulePcPdsToolHandler(repository, orchestrator);
        using var arguments = JsonDocument.Parse("{}");

        var response = await handler.InvokeAsync(arguments.RootElement, CancellationToken.None);
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Equal(89, responseDocument.RootElement.GetProperty("selectedCount").GetInt32());
        Assert.Equal(90, responseDocument.RootElement.GetProperty("expectedCount").GetInt32());
        Assert.True(responseDocument.RootElement.TryGetProperty("error", out _));
        Assert.Empty(orchestrator.ScoredPdNumbers);
    }

    [Fact]
    public async Task InvokeAsync_ContinuesScoringAfterAPerPdFailure()
    {
        var pdNumbers = Enumerable.Range(1, 90).Select(i => $"PD-{i:000}").ToList();
        var repository = new HumanPdRepository(pdNumbers);
        var failingPdNbr = pdNumbers[45];
        var orchestrator = new CapturingScoringOrchestrator(failingPdNbr);
        var handler = new RescoreHumanSchedulePcPdsToolHandler(repository, orchestrator);
        using var arguments = JsonDocument.Parse("{}");

        var response = await handler.InvokeAsync(arguments.RootElement, CancellationToken.None);
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Equal(90, responseDocument.RootElement.GetProperty("selectedCount").GetInt32());
        Assert.Equal(89, responseDocument.RootElement.GetProperty("scoredCount").GetInt32());
        Assert.Equal(1, responseDocument.RootElement.GetProperty("failedCount").GetInt32());
        var failedPdNumbers = responseDocument.RootElement.GetProperty("failedPdNumbers")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToList();
        Assert.Equal(new[] { failingPdNbr }, failedPdNumbers);
        Assert.Equal(89, orchestrator.ScoredPdNumbers.Count);
    }

    private sealed class HumanPdRepository(List<string> pdNumbers) : IPositionDescriptionRepository
    {
        public Task<List<string>> GetHumanSchedulePcPdNumbersAsync() => Task.FromResult(pdNumbers);
        public Task<List<PositionDescription>> GetAllAsync() => throw new NotSupportedException();
        public Task<PositionDescription?> GetByPdNbrAsync(string pdNbr) => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetBySeriesAsync(OccupationalSeries series) => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetByGradeRangeAsync(Grade minGrade, Grade maxGrade) => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetByFilterAsync(StagingFilter filter) => throw new NotSupportedException();
    }

    private sealed class CapturingScoringOrchestrator(params string[] pdNumbersToFail) : IScoringOrchestrator
    {
        private readonly HashSet<string> _pdNumbersToFail = pdNumbersToFail.ToHashSet();

        public List<string> ScoredPdNumbers { get; } = new();

        public Task ScoreAsync(string pdNbr)
        {
            if (_pdNumbersToFail.Contains(pdNbr))
                throw new InvalidOperationException($"Scoring failed for {pdNbr}");

            ScoredPdNumbers.Add(pdNbr);
            return Task.CompletedTask;
        }

        public Task ScoreBySeriesAsync(IEnumerable<string> series) => throw new NotSupportedException();
        public Task ScoreAllAsync() => throw new NotSupportedException();
        public Task RescoreBySeriesAsync(IEnumerable<string> series) => throw new NotSupportedException();
        public Task RescoreAllAsync() => throw new NotSupportedException();
        public Task RescoreFlaggedAsync() => throw new NotSupportedException();
        public Task RescoreFlaggedBySeriesAsync(IEnumerable<string> series) => throw new NotSupportedException();
        public Task RescoreFlaggedByPdAsync(IEnumerable<string> pdNumbers) => throw new NotSupportedException();
        public Task<EvaluationResult?> GetResultAsync(string pdNbr) => throw new NotSupportedException();
    }
}
