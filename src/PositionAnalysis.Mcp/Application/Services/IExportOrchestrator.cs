namespace PositionAnalysis.Mcp.Application.Interfaces;

/// <summary>
/// Orchestrates export of evaluation results to Excel.
/// </summary>
public interface IExportOrchestrator
{
    Task ExportAllAsync();
    Task ExportBySeriesAsync(IEnumerable<string> series);
    Task ExportByPdNumbersAsync(IEnumerable<string> pdNumbers);
    Task ExportByOrgCodesAsync(IEnumerable<string> orgCodes);
    Task<(int Total, int Exported)> GetExportStatusAsync();
    Task<int> CountAllAsync();
    Task<int> CountBySeriesAsync(IEnumerable<string> series);
    Task<int> CountByPdNumbersAsync(IEnumerable<string> pdNumbers);
    Task<int> CountByOrgCodesAsync(IEnumerable<string> orgCodes);
}
