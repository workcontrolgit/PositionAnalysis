using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Infrastructure.Config;
using SchedulePCMcp.Infrastructure.Repositories;

namespace SchedulePCMcp.Application.Services;

/// <summary>
/// Orchestrates Excel export of Schedule PC evaluation results.
/// </summary>
public class ExportOrchestrator : IExportOrchestrator
{
    private readonly ISchedulePCEvalRepository _evalRepository;
    private readonly OutputSettings _outputSettings;
    private readonly ILogger<ExportOrchestrator> _logger;
    private readonly ExcelExportSettings _excelSettings;

    public ExportOrchestrator(
        ISchedulePCEvalRepository evalRepository,
        OutputSettings outputSettings,
        ILogger<ExportOrchestrator> logger,
        IOptions<ExcelExportSettings> options)
    {
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _outputSettings = outputSettings ?? throw new ArgumentNullException(nameof(outputSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _excelSettings = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    public async Task ExportAllAsync()
    {
        var results = await _evalRepository.GetAllAsync();
        var fileName = $"evaluation_results_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
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

        var fileName = $"evaluation_results_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        await ExportAsync(dedupedResults, fileName);
    }

    public async Task<(int Total, int Exported)> GetExportStatusAsync()
    {
        var allResults = await _evalRepository.GetAllAsync();
        var total = allResults.Count;

        var directoryProbePath = _outputSettings.GetExcelOutputPath("_status_probe.xlsx");
        var exportDirectory = Path.GetDirectoryName(directoryProbePath);

        if (string.IsNullOrWhiteSpace(exportDirectory) || !Directory.Exists(exportDirectory))
            return (total, 0);

        var files = Directory.GetFiles(exportDirectory, "evaluation_results_*.xlsx", SearchOption.TopDirectoryOnly);
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
                WriteDataRows(worksheet, results);

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
        worksheet.Cell(1, 1).Value = "PdNbr";
        worksheet.Cell(1, 2).Value = "Series";
        worksheet.Cell(1, 3).Value = "Grade";
        worksheet.Cell(1, 4).Value = "OverallScore";
        worksheet.Cell(1, 5).Value = "Rating";
        worksheet.Cell(1, 6).Value = "IsCandidate";
        worksheet.Cell(1, 7).Value = "JustificationSummary";
        worksheet.Cell(1, 8).Value = "EvaluatedDate";

        worksheet.Row(1).Style.Font.Bold = true;
    }

    private static void WriteDataRows(IXLWorksheet worksheet, IEnumerable<EvaluationResult> results)
    {
        var row = 2;
        foreach (var result in results)
        {
            worksheet.Cell(row, 1).Value = result.PdNbr;
            worksheet.Cell(row, 2).Value = result.Series.ToString();
            worksheet.Cell(row, 3).Value = result.Grade.Value;
            worksheet.Cell(row, 4).Value = result.OverallScore;
            worksheet.Cell(row, 5).Value = result.Rating;
            worksheet.Cell(row, 6).Value = result.IsCandidate ? "Yes" : "No";
            worksheet.Cell(row, 7).Value = result.JustificationSummary;
            worksheet.Cell(row, 8).Value = result.EvaluatedDate;

            worksheet.Cell(row, 4).Style.NumberFormat.Format = "0.00";
            worksheet.Cell(row, 8).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";

            row++;
        }
    }
}
