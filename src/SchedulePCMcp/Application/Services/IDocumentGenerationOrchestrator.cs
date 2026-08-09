namespace SchedulePCMcp.Application.Interfaces;

/// <summary>
/// Orchestrates Word document generation for evaluation forms.
/// </summary>
public interface IDocumentGenerationOrchestrator
{
    Task GenerateByRunAsync(string runId);
    Task GenerateBySeriesAsync(string runId, IEnumerable<string> series);
    Task<int> GetGenerationProgressAsync(string runId);
}