using Microsoft.Extensions.Logging.Abstractions;
using PositionAnalysis.Mcp.Application.Services;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.AiClients;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class ScoringOrchestratorQueueTests
{
    [Fact]
    public async Task ScoreAllAsync_RecoversExpiredClaimsAndScoresEachClaimedPdOnce()
    {
        var evaluationRepository = new ClaimingEvaluationRepository(
            CreatePending("PD-1", 1),
            CreatePending("PD-2", 2));
        var positionRepository = new InMemoryPositionDescriptionRepository(
            CreatePositionDescription("PD-1", 1),
            CreatePositionDescription("PD-2", 2));
        var orchestrator = new ScoringOrchestrator(
            new SuccessfulAiClient(),
            evaluationRepository,
            positionRepository,
            NullLogger<ScoringOrchestrator>.Instance);

        await orchestrator.ScoreAllAsync();

        Assert.Equal(1, evaluationRepository.RecoverExpiredClaimsCallCount);
        Assert.Equal("recover", evaluationRepository.OperationTrace.First());
        Assert.Equal(
            new[]
            {
                "recover",
                "claim:PD-1",
                "complete:PD-1",
                "claim:PD-2",
                "complete:PD-2",
                "claim:empty"
            },
            evaluationRepository.OperationTrace);
        Assert.Equal(3, evaluationRepository.ClaimWorkerIds.Count);
        Assert.Equal("claim:empty", evaluationRepository.OperationTrace.Last());
        Assert.Equal(new[] { "PD-1", "PD-2" }, evaluationRepository.ClaimedPdNbrs);
        Assert.Equal(new[] { "PD-1", "PD-2" }, evaluationRepository.CompletedPdNbrs);
        Assert.NotEmpty(evaluationRepository.ClaimWorkerIds);
        Assert.NotEmpty(evaluationRepository.CompletionWorkerIds);
        Assert.Single(evaluationRepository.WorkerIds);
        Assert.All(evaluationRepository.ClaimWorkerIds, workerId => Assert.Equal(evaluationRepository.WorkerIds.Single(), workerId));
        Assert.All(evaluationRepository.CompletionWorkerIds, workerId => Assert.Equal(evaluationRepository.WorkerIds.Single(), workerId));
    }

    [Fact]
    public async Task ScoreAllAsync_DoesNotUseLegacyUpdateWhenClaimCompletionLosesOwnership()
    {
        var evaluationRepository = new ClaimingEvaluationRepository(CreatePending("PD-1", 1))
        {
            CompleteClaimSucceeds = false
        };
        var positionRepository = new InMemoryPositionDescriptionRepository(CreatePositionDescription("PD-1", 1));
        var orchestrator = new ScoringOrchestrator(
            new SuccessfulAiClient(),
            evaluationRepository,
            positionRepository,
            NullLogger<ScoringOrchestrator>.Instance);

        await orchestrator.ScoreAllAsync();

        Assert.Single(evaluationRepository.CompletedPdNbrs);
        Assert.Equal("PD-1", evaluationRepository.CompletedPdNbrs.Single());
        Assert.Equal(0, evaluationRepository.UpdateCallCount);
    }

    [Fact]
    public async Task ScoreAllAsync_CompletesFailedClaimAndContinuesToNextClaim()
    {
        var evaluationRepository = new ClaimingEvaluationRepository(
            CreatePending("PD-1", 1),
            CreatePending("PD-2", 2));
        var positionRepository = new InMemoryPositionDescriptionRepository(
            CreatePositionDescription("PD-1", 1),
            CreatePositionDescription("PD-2", 2));
        var orchestrator = new ScoringOrchestrator(
            new SequencedAiClient(
                new AiCompletionResult("", 0, 0, false, ErrorMessage: "first PD failure"),
                SuccessfulAiClient.Result),
            evaluationRepository,
            positionRepository,
            NullLogger<ScoringOrchestrator>.Instance);

        await orchestrator.ScoreAllAsync();

        Assert.Equal(
            new[]
            {
                "recover",
                "claim:PD-1",
                "complete:PD-1",
                "claim:PD-2",
                "complete:PD-2",
                "claim:empty"
            },
            evaluationRepository.OperationTrace);
        Assert.Equal("FAILED", evaluationRepository.CompletedResults["PD-1"].Rating);
        Assert.Equal("HIGH", evaluationRepository.CompletedResults["PD-2"].Rating);
        Assert.Single(evaluationRepository.WorkerIds);
        Assert.All(evaluationRepository.CompletionWorkerIds, workerId => Assert.Equal(evaluationRepository.WorkerIds.Single(), workerId));
        Assert.Equal(0, evaluationRepository.UpdateCallCount);
        Assert.Equal("claim:empty", evaluationRepository.OperationTrace.Last());
    }

    private static EvaluationResult CreatePending(string pdNbr, int pdSeqNum) => new()
    {
        PdNbr = pdNbr,
        PdSeqNum = pdSeqNum,
        Series = new OccupationalSeries("00130"),
        Grade = new Grade(14),
        Rating = "PENDING"
    };

    private static PositionDescription CreatePositionDescription(string pdNbr, int pdSeqNum) => new()
    {
        PdNbr = pdNbr,
        PdSeqNum = pdSeqNum,
        Title = "Policy Analyst",
        Series = new OccupationalSeries("00130"),
        Grade = new Grade(14),
        OrganizationName = "Policy Office",
        IntroText = "Evaluates policy options.",
        Duties =
        [
            new MajorDuty
            {
                SequenceNumber = 1,
                Text = "Develops agency policy recommendations.",
                PercentTimeAllotted = 100m
            }
        ]
    };

    private sealed class SuccessfulAiClient : IAiClient
    {
        public static AiCompletionResult Result { get; } = new(
                """
                {
                  "score": 85,
                  "rating": "HIGH",
                  "justification": "Develops policy recommendations.",
                  "isCandidate": true,
                  "criteria": [
                    { "name": "Policy-Determining", "triggered": true,  "evidence": "Sets agency policy.", "supportingDutyNumbers": [1] },
                    { "name": "Policy-Making",       "triggered": true,  "evidence": "Develops policy.",   "supportingDutyNumbers": [1] },
                    { "name": "Policy-Advocating",   "triggered": true,  "evidence": "Advocates policy.",  "supportingDutyNumbers": [1] },
                    { "name": "Confidential",        "triggered": false, "evidence": "",                   "supportingDutyNumbers": [] }
                  ]
                }
                """,
                0,
                0,
                true);

        public Task<AiCompletionResult> CompleteAsync(string prompt, string systemPrompt = "") =>
            Task.FromResult(Result);
    }

    private sealed class SequencedAiClient(params AiCompletionResult[] results) : IAiClient
    {
        private readonly Queue<AiCompletionResult> _results = new(results);

        public Task<AiCompletionResult> CompleteAsync(string prompt, string systemPrompt = "") =>
            Task.FromResult(_results.Dequeue());
    }

    private sealed class InMemoryPositionDescriptionRepository(params PositionDescription[] positions)
        : IPositionDescriptionRepository
    {
        private readonly Dictionary<string, PositionDescription> _positions = positions.ToDictionary(position => position.PdNbr);

        public Task<PositionDescription?> GetByPdNbrAsync(string pdNbr) =>
            Task.FromResult(_positions.GetValueOrDefault(pdNbr));

        public Task<List<PositionDescription>> GetAllAsync() => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetBySeriesAsync(OccupationalSeries series) => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetByGradeRangeAsync(Grade minGrade, Grade maxGrade) => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetByFilterAsync(StagingFilter filter) => throw new NotSupportedException();
        public Task<List<string>> GetHumanSchedulePcPdNumbersAsync() => throw new NotSupportedException();
    }

    private sealed class ClaimingEvaluationRepository(params EvaluationResult[] pendingResults)
        : IPositionAnalysisEvalRepository
    {
        private readonly Queue<EvaluationResult> _pendingResults = new(pendingResults);

        public int RecoverExpiredClaimsCallCount { get; private set; }
        public List<string> OperationTrace { get; } = new();
        public List<string> ClaimedPdNbrs { get; } = new();
        public List<string> CompletedPdNbrs { get; } = new();
        public Dictionary<string, EvaluationResult> CompletedResults { get; } = new();
        public List<string> ClaimWorkerIds { get; } = new();
        public List<string> CompletionWorkerIds { get; } = new();
        public HashSet<string> WorkerIds { get; } = new();
        public bool CompleteClaimSucceeds { get; init; } = true;
        public int UpdateCallCount { get; private set; }

        public Task<int> RecoverExpiredClaimsAsync()
        {
            RecoverExpiredClaimsCallCount++;
            OperationTrace.Add("recover");
            return Task.FromResult(0);
        }

        public Task<EvaluationResult?> ClaimNextPendingAsync(string workerId, TimeSpan leaseDuration)
        {
            WorkerIds.Add(workerId);
            ClaimWorkerIds.Add(workerId);

            var result = _pendingResults.Count > 0 ? _pendingResults.Dequeue() : null;
            if (result is null)
            {
                OperationTrace.Add("claim:empty");
                return Task.FromResult<EvaluationResult?>(null);
            }

            ClaimedPdNbrs.Add(result.PdNbr);
            OperationTrace.Add($"claim:{result.PdNbr}");
            return Task.FromResult<EvaluationResult?>(result);
        }

        public Task<bool> CompleteClaimAsync(EvaluationResult result, string workerId)
        {
            WorkerIds.Add(workerId);
            CompletionWorkerIds.Add(workerId);
            CompletedPdNbrs.Add(result.PdNbr);
            CompletedResults.Add(result.PdNbr, result);
            OperationTrace.Add($"complete:{result.PdNbr}");
            return Task.FromResult(CompleteClaimSucceeds);
        }

        public Task<QueueStatus> GetQueueStatusAsync() => throw new NotSupportedException();

        public Task<string> InsertAsync(EvaluationResult result) => throw new NotSupportedException();
        public Task UpdateAsync(EvaluationResult result)
        {
            UpdateCallCount++;
            return Task.CompletedTask;
        }
        public Task<int> DeleteAllAsync() => throw new NotSupportedException();
        public Task<StagingResult> StageFromMaxPdAsync(StagingFilter filter) => throw new NotSupportedException();
        public Task<List<SeriesCounts>> GetSeriesCountsAsync() => Task.FromResult(new List<SeriesCounts>());
        public Task<EvaluationResult?> GetByPdAsync(string pdNbr) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetAllAsync() => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetBySeriesAsync(OccupationalSeries series) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetByStatusAsync(EvaluationStatus status) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetNeedsRescoreAsync() => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetNeedsRescoreBySeriesAsync(IEnumerable<string> series) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetNeedsRescoreByPdNumbersAsync(IEnumerable<string> pdNumbers) => throw new NotSupportedException();
        public Task<int> GetCountByStatusAsync(EvaluationStatus status) => throw new NotSupportedException();
        public Task<int> ResetFailedAsync() => throw new NotSupportedException();
    }
}
