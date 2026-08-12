namespace SchedulePCMcp.Application.Interfaces;

/// <summary>
/// Orchestrates Word document generation for evaluation forms.
/// </summary>
public interface IDocumentGenerationOrchestrator
{
    Task GenerateAllAsync();
    Task GenerateBySeriesAsync(IEnumerable<string> series);
    Task GenerateByPdNumbersAsync(IEnumerable<string> pdNumbers);
    Task GenerateByOrgCodesAsync(IEnumerable<string> orgCodes);
    Task<int> GetGenerationProgressAsync();
}
