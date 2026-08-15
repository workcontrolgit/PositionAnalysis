using Microsoft.Extensions.Logging.Abstractions;
using PositionAnalysis.Mcp.Application.Services;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class StagingOrchestratorTests
{
    [Fact]
    public async Task StageAsync_DeletesExistingRowsThenUsesOracleBulkStagingAndReturnsCounts()
    {
        var evaluationRepository = new RecordingEvaluationRepository();
        var orchestrator = new StagingOrchestrator(
            new EmptyPositionDescriptionRepository(),
            evaluationRepository,
            NullLogger<StagingOrchestrator>.Instance);

        var result = await orchestrator.StageAsync(new StagingFilter(new Grade(13), new Grade(15), null, null));

        Assert.Equal(1, evaluationRepository.DeleteAllCallCount);
        Assert.Equal(1, evaluationRepository.StageFromMaxPdCallCount);
        Assert.Equal(new[] { "clear", "stage" }, evaluationRepository.Calls);
        Assert.Equal(new StagingResult(4, 2), result);
    }

    private sealed class EmptyPositionDescriptionRepository : IPositionDescriptionRepository
    {
        public Task<List<PositionDescription>> GetAllAsync() => Task.FromResult(new List<PositionDescription>());
        public Task<PositionDescription?> GetByPdNbrAsync(string pdNbr) => Task.FromResult<PositionDescription?>(null);
        public Task<List<PositionDescription>> GetBySeriesAsync(OccupationalSeries series) => Task.FromResult(new List<PositionDescription>());
        public Task<List<PositionDescription>> GetByGradeRangeAsync(Grade minGrade, Grade maxGrade) => Task.FromResult(new List<PositionDescription>());
        public Task<List<PositionDescription>> GetByFilterAsync(StagingFilter filter) => Task.FromResult(new List<PositionDescription>());
        public Task<List<string>> GetHumanSchedulePcPdNumbersAsync() => Task.FromResult(new List<string>());
    }

    private sealed class RecordingEvaluationRepository : IPositionAnalysisEvalRepository
    {
        public int DeleteAllCallCount { get; private set; }
        public int StageFromMaxPdCallCount { get; private set; }
        public List<string> Calls { get; } = new();

        public Task<int> DeleteAllAsync()
        {
            DeleteAllCallCount++;
            Calls.Add("clear");
            return Task.FromResult(0);
        }

        public Task<StagingResult> StageFromMaxPdAsync(StagingFilter filter)
        {
            StageFromMaxPdCallCount++;
            Calls.Add("stage");
            return Task.FromResult(new StagingResult(4, 2));
        }

        public Task<string> InsertAsync(EvaluationResult result) => throw new NotSupportedException();
        public Task UpdateAsync(EvaluationResult result) => throw new NotSupportedException();
        public Task<List<SeriesCounts>> GetSeriesCountsAsync() => throw new NotSupportedException();
        public Task<EvaluationResult?> GetByPdAsync(string pdNbr) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetAllAsync() => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetBySeriesAsync(OccupationalSeries series) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetByStatusAsync(EvaluationStatus status) => throw new NotSupportedException();
        public Task<int> GetCountByStatusAsync(EvaluationStatus status) => throw new NotSupportedException();
        public Task<int> ResetFailedAsync() => throw new NotSupportedException();
        public Task UpdateRatingAsync(string pdNbr, OccupationalSeries series, string rating, bool isCandidate) => throw new NotSupportedException();
        public Task<int> RecoverExpiredClaimsAsync() => throw new NotSupportedException();
        public Task<EvaluationResult?> ClaimNextPendingAsync(string workerId, TimeSpan leaseDuration) => throw new NotSupportedException();
        public Task<bool> CompleteClaimAsync(EvaluationResult result, string workerId) => throw new NotSupportedException();
        public Task<QueueStatus> GetQueueStatusAsync() => throw new NotSupportedException();
    }
}
