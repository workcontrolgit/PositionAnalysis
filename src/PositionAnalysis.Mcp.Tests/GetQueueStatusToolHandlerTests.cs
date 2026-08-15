using System.Text.Json;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using PositionAnalysis.Mcp.MCP;
using PositionAnalysis.Mcp.MCP.Tools;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class RunUnattendedQueueStatusToolHandlerTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsAggregateQueueStatusWithDrainedState()
    {
        var handler = new RunUnattendedQueueStatusToolHandler(
            new QueueStatusRepository(new QueueStatus(0, 0, 4, 1)),
            new ProcessAllRunStatusService());
        using var arguments = JsonDocument.Parse("{}");

        var response = await handler.InvokeAsync(arguments.RootElement, CancellationToken.None);
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response));
        var result = responseDocument.RootElement;

        Assert.Equal(0, result.GetProperty("pending").GetInt32());
        Assert.Equal(0, result.GetProperty("inProgress").GetInt32());
        Assert.Equal(4, result.GetProperty("complete").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.True(result.GetProperty("isDrained").GetBoolean());
        Assert.False(result.GetProperty("runFailed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("runError").ValueKind);
    }

    private sealed class QueueStatusRepository : IPositionAnalysisEvalRepository
    {
        private readonly QueueStatus _status;

        public QueueStatusRepository(QueueStatus status)
        {
            _status = status;
        }

        public Task<string> InsertAsync(EvaluationResult result) => throw new NotSupportedException();
        public Task UpdateAsync(EvaluationResult result) => throw new NotSupportedException();
        public Task<int> DeleteAllAsync() => throw new NotSupportedException();
        public Task<StagingResult> StageFromMaxPdAsync(StagingFilter filter) => throw new NotSupportedException();
        public Task<List<SeriesCounts>> GetSeriesCountsAsync() => throw new NotSupportedException();
        public Task<EvaluationResult?> GetByPdAsync(string pdNbr) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetAllAsync() => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetBySeriesAsync(OccupationalSeries series) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetByStatusAsync(EvaluationStatus status) => throw new NotSupportedException();
        public Task<int> GetCountByStatusAsync(EvaluationStatus status) => throw new NotSupportedException();
        public Task<int> ResetFailedAsync() => throw new NotSupportedException();
        public Task UpdateRatingAsync(string pdNbr, OccupationalSeries series, string rating, bool isCandidate) => throw new NotSupportedException();
        public Task<QueueStatus> GetQueueStatusAsync() => Task.FromResult(_status);
    }
}
