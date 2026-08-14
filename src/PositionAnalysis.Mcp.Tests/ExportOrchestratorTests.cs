using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Services;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Config;
using PositionAnalysis.Mcp.Infrastructure.Repositories;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class ExportOrchestratorTests
{
    [Fact]
    public async Task ExportAllAsync_CreatesReferenceHrTrackerWorkbook()
    {
        var reportsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var result = new EvaluationResult
            {
                PdNbr = "PD-100",
                Series = new OccupationalSeries("00130"),
                Grade = new Grade(14),
                Rating = "HIGH",
                IsCandidate = true,
                JustificationSummary = "Policy authority is explicit.",
                EvaluatedDate = new DateTime(2026, 8, 10),
                CriteriaScores =
                [
                    new CriterionScore { CriterionName = "Policy-Determining", Triggered = true, Evidence = "Sets agency policy." },
                    new CriterionScore { CriterionName = "Policy-Making", Triggered = true, Evidence = "Drafts policy." },
                    new CriterionScore { CriterionName = "Policy-Advocating", Triggered = false, Evidence = "No external advocacy." },
                    new CriterionScore { CriterionName = "Confidential", Triggered = false, Evidence = "No confidential relationship." }
                ]
            };
            var position = new PositionDescription
            {
                PdNbr = "PD-100",
                Title = "Policy Director",
                Series = new OccupationalSeries("00130"),
                Grade = new Grade(14),
                OrganizationCode = "ORG1",
                PayPlan = "GS",
                ManagerLevel = "2",
                PositionSensitivity = "3",
                PublicTrust = "9",
                ServiceCategory = "1"
            };
            var orchestrator = new ExportOrchestrator(
                new EvaluationRepository(result),
                new PositionDescriptionRepository(position),
                new OutputSettings(new StorageSettings
                {
                    FileSystem = new FileSystemStorageSettings { ReportsBaseDirectory = reportsDirectory }
                }),
                NullLogger<ExportOrchestrator>.Instance,
                Options.Create(new ExcelExportSettings()));

            await orchestrator.ExportAllAsync();

            // ExportAllAsync names the file from DateTime.Now, not the evaluation's EvaluatedDate.
            var workbookPath = Path.Combine(reportsDirectory, "tracker-excel", $"PositionAnalysis-Eval-Tracker-{DateTime.Now:yyyy-MM-dd}.xlsx");
            Assert.True(File.Exists(workbookPath));

            using var workbook = new XLWorkbook(workbookPath);
            var worksheet = workbook.Worksheet("Evaluation Results");
            Assert.Equal("PD Number", worksheet.Cell(1, 1).GetString());
            Assert.Equal("Rating", worksheet.Cell(1, 12).GetString());
            Assert.Equal("Eval Date", worksheet.Cell(1, 24).GetString());
            Assert.Equal("Word Form Filename", worksheet.Cell(1, 25).GetString());
            Assert.Equal("Policy Director", worksheet.Cell(2, 2).GetString());
            Assert.Equal("GS", worksheet.Cell(2, 4).GetString());
            Assert.Equal("14", worksheet.Cell(2, 6).GetString());
            Assert.Equal("Supervisor or Manager", worksheet.Cell(2, 7).GetString());
            Assert.Equal("HIGH", worksheet.Cell(2, 12).GetString());
            Assert.Equal(XLColor.FromHtml("#E2F0D9"), worksheet.Cell(2, 12).Style.Fill.BackgroundColor);
            Assert.Equal("PD-PD-100_Policy-Director_GS-00130-14.docx", worksheet.Cell(2, 25).GetString());
        }
        finally
        {
            if (Directory.Exists(reportsDirectory))
                Directory.Delete(reportsDirectory, recursive: true);
        }
    }

    private sealed class EvaluationRepository(EvaluationResult result) : IPositionAnalysisEvalRepository
    {
        public Task<List<EvaluationResult>> GetAllAsync() => Task.FromResult(new List<EvaluationResult> { result });
        public Task<string> InsertAsync(EvaluationResult evaluationResult) => throw new NotSupportedException();
        public Task UpdateAsync(EvaluationResult evaluationResult) => throw new NotSupportedException();
        public Task<int> DeleteAllAsync() => throw new NotSupportedException();
        public Task<StagingResult> StageFromMaxPdAsync(StagingFilter filter) => throw new NotSupportedException();
        public Task<List<SeriesCounts>> GetSeriesCountsAsync() => throw new NotSupportedException();
        public Task<EvaluationResult?> GetByPdAsync(string pdNbr) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetBySeriesAsync(OccupationalSeries series) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetByStatusAsync(EvaluationStatus status) =>
            Task.FromResult(status == EvaluationStatus.Complete ? new List<EvaluationResult> { result } : new List<EvaluationResult>());
        public Task<List<EvaluationResult>> GetNeedsRescoreAsync() => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetNeedsRescoreBySeriesAsync(IEnumerable<string> series) => throw new NotSupportedException();
        public Task<List<EvaluationResult>> GetNeedsRescoreByPdNumbersAsync(IEnumerable<string> pdNumbers) => throw new NotSupportedException();
        public Task<int> GetCountByStatusAsync(EvaluationStatus status) => throw new NotSupportedException();
        public Task<int> ResetFailedAsync() => throw new NotSupportedException();
        public Task UpdateRatingAsync(string pdNbr, OccupationalSeries series, string rating, bool isCandidate) => throw new NotSupportedException();
        public Task<int> RecoverExpiredClaimsAsync() => throw new NotSupportedException();
        public Task<EvaluationResult?> ClaimNextPendingAsync(string workerId, TimeSpan leaseDuration) => throw new NotSupportedException();
        public Task<bool> CompleteClaimAsync(EvaluationResult result, string workerId) => throw new NotSupportedException();
        public Task<QueueStatus> GetQueueStatusAsync() => throw new NotSupportedException();
    }

    private sealed class PositionDescriptionRepository(PositionDescription position) : IPositionDescriptionRepository
    {
        public Task<PositionDescription?> GetByPdNbrAsync(string pdNbr) => Task.FromResult<PositionDescription?>(position);
        public Task<List<PositionDescription>> GetAllAsync() => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetBySeriesAsync(OccupationalSeries series) => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetByGradeRangeAsync(Grade minGrade, Grade maxGrade) => throw new NotSupportedException();
        public Task<List<PositionDescription>> GetByFilterAsync(StagingFilter filter) => throw new NotSupportedException();
        public Task<List<string>> GetHumanSchedulePcPdNumbersAsync() => throw new NotSupportedException();
    }
}
