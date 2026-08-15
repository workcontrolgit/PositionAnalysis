using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Config;
using PositionAnalysis.Mcp.MCP;
using PositionAnalysis.Mcp.MCP.Tools;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class ParallelBatchScorerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (ParallelBatchScorer Scorer, TrackingOrchestrator Orchestrator) BuildScorer(
        string? throwOn = null, bool throwOnAll = false)
    {
        var orchestrator = new TrackingOrchestrator(throwOn, throwOnAll);
        var services = new ServiceCollection();
        services.AddSingleton<IScoringOrchestrator>(orchestrator);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var scorer = new ParallelBatchScorer(scopeFactory, Options.Create(new McpSettings()), NullLogger<ParallelBatchScorer>.Instance);
        return (scorer, orchestrator);
    }

    // ── RunAsync tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ScoresEveryPdAndReturnsCorrectCounts()
    {
        var (scorer, orchestrator) = BuildScorer();

        var result = await scorer.RunAsync(
            ["100001", "100002", "100003"],
            reportProgressAsync: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Completed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(3, orchestrator.ScoredPdNbrs.Count);
        Assert.Contains("100001", orchestrator.ScoredPdNbrs);
        Assert.Contains("100002", orchestrator.ScoredPdNbrs);
        Assert.Contains("100003", orchestrator.ScoredPdNbrs);
    }

    [Fact]
    public async Task RunAsync_ReportsProgressForEveryItemPlusInitial()
    {
        var (scorer, _) = BuildScorer();
        const int pdCount = 4;
        var reports = new List<(double Current, double Total)>();

        await scorer.RunAsync(
            ["A", "B", "C", "D"],
            reportProgressAsync: (c, t) => { reports.Add((c, t)); return Task.CompletedTask; },
            cancellationToken: CancellationToken.None);

        // Initial report: progress=0, total=4
        Assert.Equal((0d, (double)pdCount), reports[0]);

        // All remaining reports must have total=4 and progress values 1..4 (in any order)
        var progressValues = reports.Skip(1).Select(r => (int)r.Current).OrderBy(x => x).ToList();
        Assert.Equal([1, 2, 3, 4], progressValues);

        // Every report uses the correct total
        Assert.All(reports, r => Assert.Equal(pdCount, (int)r.Total));
    }

    [Fact]
    public async Task RunAsync_NullReportProgress_CompletesWithoutThrowing()
    {
        var (scorer, orchestrator) = BuildScorer();

        var result = await scorer.RunAsync(
            ["200001", "200002"],
            reportProgressAsync: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, result.Completed);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task RunAsync_EmptyList_ReturnsZeroCounts()
    {
        var (scorer, orchestrator) = BuildScorer();

        var result = await scorer.RunAsync(
            [],
            reportProgressAsync: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(orchestrator.ScoredPdNbrs);
    }

    [Fact]
    public async Task RunAsync_OneOrchestratorThrows_OthersCompleteAndFailedCountIsOne()
    {
        const string badPd = "FAIL-ME";
        var (scorer, orchestrator) = BuildScorer(throwOn: badPd);

        var result = await scorer.RunAsync(
            ["400001", badPd, "400003"],
            reportProgressAsync: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Completed);
        Assert.Equal(1, result.Failed);
        Assert.Contains("400001", orchestrator.ScoredPdNbrs);
        Assert.Contains("400003", orchestrator.ScoredPdNbrs);
    }

    [Fact]
    public async Task RunAsync_AllOrchestratorsFail_ReturnsAllFailed()
    {
        var (scorer, _) = BuildScorer(throwOnAll: true);

        var result = await scorer.RunAsync(
            ["X1", "X2"],
            reportProgressAsync: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(0, result.Completed);
        Assert.Equal(2, result.Failed);
    }

    // ── ProcessBatchByPdsToolHandler integration ──────────────────────────────

    private static JsonElement PdNbrsArgs(params string[] pdNbrs)
    {
        var json = JsonSerializer.Serialize(new { pdNbrs });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement EmptyArgs()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ProcessBatchByPdsToolHandler_Name_IsProcessBatchByPds()
    {
        var (scorer, _) = BuildScorer();
        var handler = new ProcessBatchByPdsToolHandler(scorer, NullLogger<ProcessBatchByPdsToolHandler>.Instance);
        Assert.Equal("process_batch_by_pds", handler.Name);
    }

    [Fact]
    public async Task ProcessBatchByPdsToolHandler_InvokeAsync_ScoresAllPds()
    {
        var (scorer, orchestrator) = BuildScorer();
        var handler = new ProcessBatchByPdsToolHandler(scorer, NullLogger<ProcessBatchByPdsToolHandler>.Instance);

        var result = await handler.InvokeAsync(PdNbrsArgs("300001", "300002"), CancellationToken.None);

        var json = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;
        Assert.Equal(2, json.GetProperty("Completed").GetInt32());
        Assert.Contains("300001", orchestrator.ScoredPdNbrs);
        Assert.Contains("300002", orchestrator.ScoredPdNbrs);
    }

    [Fact]
    public async Task ProcessBatchByPdsToolHandler_EmptyArray_ReturnsZero()
    {
        var (scorer, orchestrator) = BuildScorer();
        var handler = new ProcessBatchByPdsToolHandler(scorer, NullLogger<ProcessBatchByPdsToolHandler>.Instance);

        var result = await handler.InvokeAsync(PdNbrsArgs(), CancellationToken.None);

        var json = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;
        Assert.Equal(0, json.GetProperty("total").GetInt32()); // handler short-circuits before scorer
        Assert.Empty(orchestrator.ScoredPdNbrs);
    }

    [Fact]
    public async Task ProcessBatchByPdsToolHandler_MissingProperty_ReturnsZero()
    {
        var (scorer, _) = BuildScorer();
        var handler = new ProcessBatchByPdsToolHandler(scorer, NullLogger<ProcessBatchByPdsToolHandler>.Instance);

        var result = await handler.InvokeAsync(EmptyArgs(), CancellationToken.None);

        var json = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;
        Assert.Equal(0, json.GetProperty("total").GetInt32()); // handler short-circuits before scorer
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class TrackingOrchestrator : IScoringOrchestrator
    {
        private readonly string? _throwOn;
        private readonly bool _throwOnAll;
        private readonly List<string> _scored = [];
        private readonly Lock _lock = new();

        public TrackingOrchestrator(string? throwOn = null, bool throwOnAll = false)
        {
            _throwOn = throwOn;
            _throwOnAll = throwOnAll;
        }

        public IReadOnlyList<string> ScoredPdNbrs
        {
            get { lock (_lock) return _scored.ToList(); }
        }

        public Task ScoreAsync(string pdNbr)
        {
            if (_throwOnAll || pdNbr == _throwOn)
                throw new InvalidOperationException($"Simulated failure for PD {pdNbr}");

            lock (_lock) _scored.Add(pdNbr);
            return Task.CompletedTask;
        }

        public Task ScoreAllAsync() => Task.CompletedTask;
        public Task RescoreBySeriesAsync(IEnumerable<string> series) => Task.CompletedTask;
        public Task<int> RebucketRatingsAsync(IEnumerable<string>? series = null, IEnumerable<string>? pdNumbers = null) => Task.FromResult(0);
        public Task<EvaluationResult?> GetResultAsync(string pdNbr) => Task.FromResult<EvaluationResult?>(null);
    }
}
