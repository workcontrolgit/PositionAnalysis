using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Domain.ValueObjects;

namespace PositionAnalysis.Mcp.Application.Services;

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

    public Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStatusBySeriesAsync(IEnumerable<string> series)
    {
        return _reportingService.GetProcessingReportBySeriesAsync(series);
    }

    public Task<Dictionary<OccupationalSeries, SeriesStatus>> GetStatusByOrgCodesAsync(IEnumerable<string> orgCodes)
    {
        return _reportingService.GetProcessingReportByOrgCodesAsync(orgCodes);
    }

    public Task<List<PdProcessingStatus>> GetStatusByPdNumbersAsync(IEnumerable<string> pdNumbers)
    {
        return _reportingService.GetProcessingStatusByPdNumbersAsync(pdNumbers);
    }
}
