using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Domain.ValueObjects;

namespace SchedulePCMcp.Application.Services;

/// <summary>
/// Provides live processing status by delegating to reporting aggregates.
/// </summary>
public class ProcessingStatusService : IProcessingStatusService
{
    private readonly IReportingService _reportingService;

    public ProcessingStatusService(IReportingService reportingService)
    {
        _reportingService = reportingService ?? throw new ArgumentNullException(nameof(reportingService));
    }

    public Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStatusAsync()
    {
        return _reportingService.GetProcessingReportAsync();
    }

    public async Task<SeriesStatus?> GetStatusBySeriesAsync(OccupationalSeries series)
    {
        var all = await _reportingService.GetProcessingReportAsync();
        return all.TryGetValue(series, out var status) ? status : null;
    }
}
