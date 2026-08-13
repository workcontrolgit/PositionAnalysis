using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Config;
using PositionAnalysis.Mcp.Infrastructure.DocumentGeneration;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.Application.Services;

/// <summary>
/// Orchestrates Excel export of Schedule PC evaluation results.
/// </summary>
public class ExportOrchestrator : IExportOrchestrator
{
    private readonly IPositionAnalysisEvalRepository _evalRepository;
    private readonly IPositionDescriptionRepository _positionDescriptionRepository;
    private readonly OutputSettings _outputSettings;
    private readonly ILogger<ExportOrchestrator> _logger;
    private readonly ExcelExportSettings _excelSettings;

    public ExportOrchestrator(
        IPositionAnalysisEvalRepository evalRepository,
        IPositionDescriptionRepository positionDescriptionRepository,
        OutputSettings outputSettings,
        ILogger<ExportOrchestrator> logger,
        IOptions<ExcelExportSettings> options)
    {
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _positionDescriptionRepository = positionDescriptionRepository ?? throw new ArgumentNullException(nameof(positionDescriptionRepository));
        _outputSettings = outputSettings ?? throw new ArgumentNullException(nameof(outputSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _excelSettings = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    public async Task ExportAllAsync()
    {
        var results = await _evalRepository.GetAllAsync();
        var fileName = $"PositionAnalysis-Eval-Tracker-{DateTime.Now:yyyy-MM-dd}.xlsx";
        await ExportAsync(results, fileName);
    }

    public async Task ExportBySeriesAsync(IEnumerable<string> series)
    {
        if (series == null)
            throw new ArgumentNullException(nameof(series));

        var seriesList = series
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (seriesList.Count == 0)
        {
            _logger.LogInformation("No series provided; skipping export");
            return;
        }

        var results = new List<EvaluationResult>();
        foreach (var seriesCode in seriesList)
        {
            try
            {
                var occupationalSeries = new OccupationalSeries(seriesCode);
                var seriesResults = await _evalRepository.GetBySeriesAsync(occupationalSeries);
                results.AddRange(seriesResults);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid series code {SeriesCode}; skipping", seriesCode);
            }
        }

        var dedupedResults = results
            .GroupBy(r => r.PdNbr, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var fileName = $"PositionAnalysis-Eval-Tracker-{DateTime.Now:yyyy-MM-dd}.xlsx";
        await ExportAsync(dedupedResults, fileName);
    }

    public async Task ExportByPdNumbersAsync(IEnumerable<string> pdNumbers)
    {
        if (pdNumbers == null)
            throw new ArgumentNullException(nameof(pdNumbers));

        var pdNbrList = pdNumbers
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pdNbrList.Count == 0)
        {
            _logger.LogInformation("No PD numbers provided; skipping export");
            return;
        }

        var results = new List<EvaluationResult>();
        foreach (var pdNbr in pdNbrList)
        {
            var result = await _evalRepository.GetByPdAsync(pdNbr);
            if (result == null)
            {
                _logger.LogWarning("No evaluation result found for PD {PdNbr}; skipping", pdNbr);
                continue;
            }

            results.Add(result);
        }

        var fileName = $"PositionAnalysis-Eval-Tracker-{DateTime.Now:yyyy-MM-dd}.xlsx";
        await ExportAsync(results, fileName);
    }

    public async Task ExportByOrgCodesAsync(IEnumerable<string> orgCodes)
    {
        if (orgCodes == null)
            throw new ArgumentNullException(nameof(orgCodes));

        var orgCodeList = orgCodes
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orgCodeList.Count == 0)
        {
            _logger.LogInformation("No org codes provided; skipping export");
            return;
        }

        var allResults = await _evalRepository.GetAllAsync();
        var results = new List<EvaluationResult>();

        foreach (var result in allResults)
        {
            var pd = await _positionDescriptionRepository.GetByPdNbrAsync(result.PdNbr);
            if (pd == null) continue;

            var matches = orgCodeList.Any(code =>
                string.Equals(pd.OrganizationCode, code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pd.BureauCode, code, StringComparison.OrdinalIgnoreCase));

            if (matches)
                results.Add(result);
        }

        var fileName = $"PositionAnalysis-Eval-Tracker-{DateTime.Now:yyyy-MM-dd}.xlsx";
        await ExportAsync(results, fileName);
    }

    public async Task<(int Total, int Exported)> GetExportStatusAsync()
    {
        var allResults = await _evalRepository.GetAllAsync();
        var total = allResults.Count;

        var directoryProbePath = _outputSettings.GetExcelOutputPath("_status_probe.xlsx");
        var exportDirectory = Path.GetDirectoryName(directoryProbePath);

        if (string.IsNullOrWhiteSpace(exportDirectory) || !Directory.Exists(exportDirectory))
            return (total, 0);

        var files = Directory.GetFiles(exportDirectory, "PositionAnalysis-Eval-Tracker-*.xlsx", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            return (total, 0);

        var latestFile = files
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .First();

        try
        {
            var exported = await Task.Run(() =>
            {
                using var workbook = new XLWorkbook(latestFile.FullName);
                var worksheet = workbook.Worksheet("Evaluation Results");
                var usedRange = worksheet.RangeUsed();

                if (usedRange == null)
                    return 0;

                var dataRowCount = usedRange.RowCount() - 1;
                return Math.Max(0, dataRowCount);
            });

            return (total, exported);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error while checking export status");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission error while checking export status");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel parsing error while checking export status");
            throw;
        }
    }

    private async Task ExportAsync(IReadOnlyCollection<EvaluationResult> results, string fileName)
    {
        var outputPath = _outputSettings.GetExcelOutputPath(fileName);
        var outputDirectory = Path.GetDirectoryName(outputPath);

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("Unable to determine Excel output directory");

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error while preparing Excel output directory");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission error while preparing Excel output directory");
            throw;
        }

        try
        {
            await Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Evaluation Results");

                WriteHeaderRow(worksheet);
                WriteDataRowsAsync(worksheet, results).GetAwaiter().GetResult();

                worksheet.Columns().AdjustToContents();
                worksheet.SheetView.FreezeRows(1);

                workbook.SaveAs(outputPath);
            });
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error while writing Excel export to {OutputPath}", outputPath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission error while writing Excel export to {OutputPath}", outputPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Excel generation error using implementation {Implementation}",
                _excelSettings.Implementation);
            throw;
        }

        _logger.LogInformation("Exported {Count} evaluation results to Excel at {OutputPath}", results.Count, outputPath);
    }

    private static void WriteHeaderRow(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "PD Number", "Position Title", "Org Code", "Pay Plan", "Series", "Grade",
            "Manager Level", "Position Sensitivity", "Public Trust", "Service Category",
            "Is Candidate", "Rating", "AI Score", "Criteria Met Count", "Policy-Determining",
            "Policy-Determining Evidence", "Policy-Making", "Policy-Making Evidence",
            "Policy-Advocating", "Policy-Advocating Evidence", "Confidential",
            "Confidential Evidence", "Justification Summary", "Eval Date", "Word Form Filename"
        };

        for (var column = 0; column < headers.Length; column++)
            worksheet.Cell(1, column + 1).Value = headers[column];

        worksheet.Row(1).Style.Font.Bold = true;
    }

    private async Task WriteDataRowsAsync(IXLWorksheet worksheet, IEnumerable<EvaluationResult> results)
    {
        var row = 2;
        foreach (var result in results
                     .OrderBy(r => r.Series.ToString(), StringComparer.Ordinal)
                     .ThenByDescending(r => r.Grade.Value)
                     .ThenBy(r => r.PdNbr, StringComparer.Ordinal))
        {
            var position = await _positionDescriptionRepository.GetByPdNbrAsync(result.PdNbr);
            var policyDetermining = GetCriterion(result, "Policy-Determining");
            var policyMaking = GetCriterion(result, "Policy-Making");
            var policyAdvocating = GetCriterion(result, "Policy-Advocating");
            var confidential = GetCriterion(result, "Confidential");
            var title = position?.Title ?? string.Empty;
            var payPlan = position?.PayPlan ?? string.Empty;
            var wordFileName = $"PD-{result.PdNbr}_{ToFileNameSlug(title)}_{payPlan}-{result.Series}-{result.Grade.Value}.docx";

            worksheet.Cell(row, 1).Value = result.PdNbr;
            worksheet.Cell(row, 2).Value = title;
            worksheet.Cell(row, 3).Value = string.IsNullOrWhiteSpace(position?.OrganizationCode) ? position?.BureauCode ?? string.Empty : position.OrganizationCode;
            worksheet.Cell(row, 4).Value = payPlan;
            worksheet.Cell(row, 5).Value = result.Series.ToString();
            worksheet.Cell(row, 6).Value = result.Grade.ToString();
            worksheet.Cell(row, 7).Value = GetManagerLevelLabel(position?.ManagerLevel);
            worksheet.Cell(row, 8).Value = GetSensitivityLabel(position?.PositionSensitivity);
            worksheet.Cell(row, 9).Value = GetPublicTrustLabel(position?.PublicTrust);
            worksheet.Cell(row, 10).Value = GetServiceCategoryLabel(position?.ServiceCategory);
            worksheet.Cell(row, 11).Value = result.IsCandidate ? "YES" : "NO";
            worksheet.Cell(row, 12).Value = result.Rating;
            worksheet.Cell(row, 13).Value = result.OverallScore;
            worksheet.Cell(row, 14).Value = result.CriteriaScores.Count(c => c.Triggered);
            worksheet.Cell(row, 15).Value = policyDetermining.Triggered.ToString();
            worksheet.Cell(row, 16).Value = policyDetermining.Evidence;
            worksheet.Cell(row, 17).Value = policyMaking.Triggered.ToString();
            worksheet.Cell(row, 18).Value = policyMaking.Evidence;
            worksheet.Cell(row, 19).Value = policyAdvocating.Triggered.ToString();
            worksheet.Cell(row, 20).Value = policyAdvocating.Evidence;
            worksheet.Cell(row, 21).Value = confidential.Triggered.ToString();
            worksheet.Cell(row, 22).Value = confidential.Evidence;
            worksheet.Cell(row, 23).Value = result.JustificationSummary;
            worksheet.Cell(row, 24).Value = result.EvaluatedDate;
            worksheet.Cell(row, 25).Value = wordFileName;

            worksheet.Cell(row, 12).Style.Fill.BackgroundColor = GetRatingBackgroundColor(result.Rating);
            worksheet.Cell(row, 12).Style.Font.FontColor = GetRatingFontColor(result.Rating);
            worksheet.Cell(row, 13).Style.Fill.BackgroundColor = XLColor.FromHtml($"#{ScoreGradient.Background(result.OverallScore)}");
            worksheet.Cell(row, 13).Style.Font.FontColor = XLColor.FromHtml($"#{ScoreGradient.Foreground(result.OverallScore)}");
            worksheet.Cell(row, 24).Style.DateFormat.Format = "yyyy-mm-dd";

            row++;
        }
    }

    private static CriterionScore GetCriterion(EvaluationResult result, string name) =>
        result.CriteriaScores.FirstOrDefault(c => string.Equals(c.CriterionName, name, StringComparison.OrdinalIgnoreCase))
        ?? new CriterionScore { CriterionName = name };

    private static string ToFileNameSlug(string value)
    {
        // Source titles sometimes carry a leading numeric code, e.g. "015 - FOREIGN AFFAIRS OFFICER"; drop it for the filename.
        var withoutPrefix = Regex.Replace(value, @"^\d+\s*-\s*", "");
        var sanitizedValue = new string(withoutPrefix
            .Where(character => char.IsLetterOrDigit(character) || character == ' ' || character == '-')
            .ToArray());
        return string.Join("-", sanitizedValue.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static XLColor GetRatingBackgroundColor(string rating) => rating switch
    {
        "HIGH" => XLColor.FromHtml("#E2F0D9"),
        "MEDIUM" => XLColor.FromHtml("#FFF2CC"),
        _ => XLColor.FromHtml("#FCE4D6")
    };

    private static XLColor GetRatingFontColor(string rating) => rating switch
    {
        "HIGH" => XLColor.FromHtml("#1E4620"),
        "MEDIUM" => XLColor.FromHtml("#5C4300"),
        _ => XLColor.FromHtml("#801414")
    };

    private static string GetManagerLevelLabel(string? code) => code switch
    {
        "2" => "Supervisor or Manager",
        "4" => "Supervisor (CSRA)",
        "5" => "Management Official (CSRA)",
        "6" => "Leader",
        "7" => "Team Leader",
        "8" => "All Other Positions",
        _ => string.Empty
    };

    private static string GetSensitivityLabel(string? code) => code switch
    {
        "1" => "Non-Sensitive", "2" => "Non-Critical Sensitive", "3" => "Critical Sensitive", "4" => "Special Sensitive", _ => string.Empty
    };

    private static string GetPublicTrustLabel(string? code) => code switch
    {
        "9" => "High Risk", "10" => "Mod Risk", "11" => "Low Risk", "99" => "No Risk", _ => "No Risk"
    };

    private static string GetServiceCategoryLabel(string? code) => code switch
    {
        "1" => "Competitive", "2" => "Excepted", "3" => "SES General", "4" => "SES Career Reserved", "5" => "Federal Wage System", _ => "Competitive"
    };
}
