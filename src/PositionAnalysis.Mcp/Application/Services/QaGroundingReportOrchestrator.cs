using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Domain.Services;
using PositionAnalysis.Mcp.Infrastructure.Config;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.Application.Services;

/// <summary>
/// Tier 1 QA automation (see docs/schedulepc/schedule-pc-qa-plan.md, Section 9): a deterministic,
/// no-LLM check that flags per-criterion evidence quotes which cannot be located in the PD's own
/// duty text, and separately flags criteria left with blank evidence (the prompt requires either a
/// verbatim quote or an explicit negative finding for every criterion). Reuses the same grounding
/// logic already used for Word document duty attribution (<see cref="EvidenceGroundingChecker"/>),
/// so this check costs nothing beyond compute time.
/// </summary>
public class QaGroundingReportOrchestrator : IQaGroundingReportOrchestrator
{
    private readonly IPositionAnalysisEvalRepository _evalRepository;
    private readonly IPositionDescriptionRepository _positionDescriptionRepository;
    private readonly OutputSettings _outputSettings;
    private readonly ILogger<QaGroundingReportOrchestrator> _logger;

    public QaGroundingReportOrchestrator(
        IPositionAnalysisEvalRepository evalRepository,
        IPositionDescriptionRepository positionDescriptionRepository,
        OutputSettings outputSettings,
        ILogger<QaGroundingReportOrchestrator> logger)
    {
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _positionDescriptionRepository = positionDescriptionRepository ?? throw new ArgumentNullException(nameof(positionDescriptionRepository));
        _outputSettings = outputSettings ?? throw new ArgumentNullException(nameof(outputSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<QaGroundingReportSummary> RunTier1GroundingCheckAsync()
    {
        var results = await _evalRepository.GetByStatusAsync(EvaluationStatus.Complete);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Tier1 Grounding Check");
        WriteHeaderRow(worksheet);

        var row = 2;
        var totalCriteriaChecked = 0;
        var ungroundedCount = 0;
        var missingNegativeFindingCount = 0;

        foreach (var result in results.OrderBy(r => r.PdNbr, StringComparer.Ordinal))
        {
            var position = await _positionDescriptionRepository.GetByPdNbrAsync(result.PdNbr);
            var fullDutyText = position == null
                ? string.Empty
                : string.Join(" ", position.Duties.Select(d => d.Text));

            foreach (var criterion in result.CriteriaScores)
            {
                worksheet.Cell(row, 1).Value = result.PdNbr;
                worksheet.Cell(row, 2).Value = result.Series.ToString();
                worksheet.Cell(row, 3).Value = result.Grade.ToString();
                worksheet.Cell(row, 4).Value = result.Rating;
                worksheet.Cell(row, 5).Value = result.IsCandidate ? "YES" : "NO";
                worksheet.Cell(row, 6).Value = criterion.CriterionName;
                worksheet.Cell(row, 7).Value = criterion.Triggered.ToString();
                worksheet.Cell(row, 8).Value = criterion.Evidence;

                if (string.IsNullOrWhiteSpace(criterion.Evidence))
                {
                    // Prompt requires a verbatim quote or an explicit negative finding for every
                    // criterion; a blank evidence field means the model skipped that instruction.
                    missingNegativeFindingCount++;
                    worksheet.Cell(row, 9).Value = "MISSING";
                    worksheet.Cell(row, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
                    worksheet.Cell(row, 9).Style.Font.FontColor = XLColor.FromHtml("#7F6000");
                    row++;
                    continue;
                }

                totalCriteriaChecked++;
                var grounded = EvidenceGroundingChecker.IsSupported(fullDutyText, criterion.Evidence);
                if (!grounded)
                    ungroundedCount++;

                worksheet.Cell(row, 9).Value = grounded ? "YES" : "NO";

                if (!grounded)
                {
                    worksheet.Cell(row, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
                    worksheet.Cell(row, 9).Style.Font.FontColor = XLColor.FromHtml("#C00000");
                }

                row++;
            }
        }

        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.SheetView.FreezeRows(1);
        worksheet.RangeUsed()?.SetAutoFilter();
        worksheet.Columns().AdjustToContents();

        var fileName = $"Tier1-Grounding-Check-{DateTime.Now:yyyy-MM-dd}.xlsx";
        var outputPath = _outputSettings.GetQaReportOutputPath(fileName);
        workbook.SaveAs(outputPath);

        _logger.LogInformation(
            "Tier 1 QA grounding check complete: {TotalPds} PDs, {TotalChecked} criteria checked, {Ungrounded} flagged ungrounded, {MissingNegativeFinding} missing negative findings. Report: {OutputPath}",
            results.Count, totalCriteriaChecked, ungroundedCount, missingNegativeFindingCount, outputPath);

        return new QaGroundingReportSummary(outputPath, results.Count, totalCriteriaChecked, ungroundedCount, missingNegativeFindingCount);
    }

    private static void WriteHeaderRow(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "PD Number", "Series", "Grade", "Rating", "Is Candidate",
            "Criterion", "Triggered", "Evidence", "Grounded"
        };

        for (var column = 0; column < headers.Length; column++)
            worksheet.Cell(1, column + 1).Value = headers[column];
    }
}
