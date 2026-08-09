namespace SchedulePCMcp.Application.Interfaces;

/// <summary>
/// Orchestrates export of evaluation results to Excel.
/// </summary>
public interface IExportOrchestrator
{
    Task ExportByRunAsync(string runId);
    Task ExportBySeriesAsync(string runId, IEnumerable<string> series);
    Task<(int Total, int Exported)> GetExportStatusAsync(string runId);
}