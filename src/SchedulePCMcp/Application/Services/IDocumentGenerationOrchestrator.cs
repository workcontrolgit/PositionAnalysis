namespace SchedulePCMcp.Application.Interfaces;

/// <summary>
/// Orchestrates Word document generation for evaluation forms.
/// </summary>
public interface IDocumentGenerationOrchestrator
{
    Task<(int Succeeded, int Failed)> GenerateAllAsync();
    Task<(int Succeeded, int Failed)> GenerateBySeriesAsync(IEnumerable<string> series);
    Task<(int Succeeded, int Failed)> GenerateByPdNumbersAsync(IEnumerable<string> pdNumbers);
    Task<(int Succeeded, int Failed)> GenerateByOrgCodesAsync(IEnumerable<string> orgCodes);
    Task<int> GetGenerationProgressAsync();
}
