using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.Enums;
using PositionAnalysis.Mcp.Domain.ValueObjects;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.Application.Services;

/// <summary>
/// Builds staging and processing summaries grouped by occupational series.
/// </summary>
public class ReportingService : IReportingService
{
    private readonly IPositionAnalysisEvalRepository _evalRepository;
    private readonly IPositionDescriptionRepository _pdRepository;

    public ReportingService(IPositionAnalysisEvalRepository evalRepository, IPositionDescriptionRepository pdRepository)
    {
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
        _pdRepository = pdRepository ?? throw new ArgumentNullException(nameof(pdRepository));
    }

    public async Task<Dictionary<OccupationalSeries, SeriesStatus>> GetProcessingReportAsync()
    {
        var results = await _evalRepository.GetAllAsync();
        return BuildSeriesStatus(results, stagedOnly: false);
    }

    public async Task<Dictionary<OccupationalSeries, SeriesStatus>> GetProcessingReportBySeriesAsync(IEnumerable<string> series)
    {
        var seriesCodes = (series ?? throw new ArgumentNullException(nameof(series)))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<Domain.Entities.EvaluationResult>();
        foreach (var seriesCode in seriesCodes)
        {
            var occupationalSeries = new OccupationalSeries(seriesCode);
            results.AddRange(await _evalRepository.GetBySeriesAsync(occupationalSeries));
        }

        return BuildSeriesStatus(results, stagedOnly: false);
    }

    public async Task<Dictionary<OccupationalSeries, SeriesStatus>> GetProcessingReportByOrgCodesAsync(IEnumerable<string> orgCodes)
    {
        var orgCodeList = (orgCodes ?? throw new ArgumentNullException(nameof(orgCodes)))
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .ToList();

        var results = await _evalRepository.GetAllAsync();
        var filtered = new List<Domain.Entities.EvaluationResult>();

        foreach (var result in results)
        {
            var pd = await _pdRepository.GetByPdNbrAsync(result.PdNbr);
            if (pd == null) continue;

            var matches = orgCodeList.Any(code =>
                string.Equals(pd.OrganizationCode, code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pd.BureauCode, code, StringComparison.OrdinalIgnoreCase));

            if (matches)
                filtered.Add(result);
        }

        return BuildSeriesStatus(filtered, stagedOnly: false);
    }

    public async Task<List<PdProcessingStatus>> GetProcessingStatusByPdNumbersAsync(IEnumerable<string> pdNumbers)
    {
        var pdNbrList = (pdNumbers ?? throw new ArgumentNullException(nameof(pdNumbers)))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var statuses = new List<PdProcessingStatus>();
        foreach (var pdNbr in pdNbrList)
        {
            var result = await _evalRepository.GetByPdAsync(pdNbr);
            if (result == null)
            {
                statuses.Add(new PdProcessingStatus(pdNbr, string.Empty, "NOT_FOUND", string.Empty));
                continue;
            }

            var status = ResolveStatus(result.Rating);
            statuses.Add(new PdProcessingStatus(result.PdNbr, result.Series.Code, status.ToString(), result.Rating));
        }

        return statuses;
    }

    private static Dictionary<OccupationalSeries, SeriesStatus> BuildSeriesStatus(
        IEnumerable<Domain.Entities.EvaluationResult> results,
        bool stagedOnly)
    {
        var grouped = results.GroupBy(r => r.Series.Code);
        var output = new Dictionary<OccupationalSeries, SeriesStatus>();

        foreach (var group in grouped)
        {
            var series = new OccupationalSeries(group.Key);
            var staged = 0;
            var inProgress = 0;
            var complete = 0;
            var failed = 0;

            foreach (var result in group)
            {
                var status = ResolveStatus(result.Rating);
                switch (status)
                {
                    case EvaluationStatus.Staged:
                        staged++;
                        break;
                    case EvaluationStatus.InProgress:
                        inProgress++;
                        break;
                    case EvaluationStatus.Complete:
                        complete++;
                        break;
                    case EvaluationStatus.Failed:
                        failed++;
                        break;
                }
            }

            if (stagedOnly)
            {
                output[series] = new SeriesStatus(series, staged: staged + inProgress + complete + failed);
            }
            else
            {
                output[series] = new SeriesStatus(series, staged, inProgress, complete, failed);
            }
        }

        return output;
    }

    private static EvaluationStatus ResolveStatus(string rating)
    {
        if (string.IsNullOrWhiteSpace(rating) || rating.Equals("PENDING", StringComparison.OrdinalIgnoreCase))
            return EvaluationStatus.Staged;

        if (rating.Equals("IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            return EvaluationStatus.InProgress;

        if (rating.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
            rating.Equals("GENERATION_FAILED", StringComparison.OrdinalIgnoreCase))
            return EvaluationStatus.Failed;

        return EvaluationStatus.Complete;
    }
}
