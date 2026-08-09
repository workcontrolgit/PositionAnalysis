using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Domain.Enums;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Infrastructure.Repositories;

namespace SchedulePCMcp.Application.Services;

/// <summary>
/// Builds staging and processing summaries grouped by occupational series.
/// </summary>
public class ReportingService : IReportingService
{
    private readonly ISchedulePCEvalRepository _evalRepository;

    public ReportingService(ISchedulePCEvalRepository evalRepository)
    {
        _evalRepository = evalRepository ?? throw new ArgumentNullException(nameof(evalRepository));
    }

    public async Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStagingReportAsync(string runId)
    {
        var counts = await _evalRepository.GetSeriesCountsAsync(runId);
        var output = new Dictionary<OccupationalSeries, SeriesStatus>();
        foreach (var item in counts)
        {
            var series = new OccupationalSeries(item.Series);
            output[series] = new SeriesStatus(series, item.Staged, item.InProgress, item.Complete, item.Failed);
        }

        return output;
    }

    public async Task<Dictionary<OccupationalSeries, SeriesStatus>> GetProcessingReportAsync(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run ID cannot be null or empty", nameof(runId));

        var results = await _evalRepository.GetByRunAsync(runId);
        return BuildSeriesStatus(results, stagedOnly: false);
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
