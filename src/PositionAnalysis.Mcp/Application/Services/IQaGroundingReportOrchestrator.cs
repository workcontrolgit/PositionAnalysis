namespace PositionAnalysis.Mcp.Application.Interfaces;

/// <summary>
/// Runs the Tier 1 (deterministic, no-LLM) QA evidence-grounding check described in the
/// Schedule P/C QA plan: flags any per-criterion evidence quote that cannot be located
/// (verbatim or via significant-word overlap) in the PD's own duty text.
/// </summary>
public interface IQaGroundingReportOrchestrator
{
    /// <summary>Runs the check across all Complete evaluation results and writes an Excel report.</summary>
    /// <returns>The full path of the generated report, plus summary counts.</returns>
    Task<QaGroundingReportSummary> RunTier1GroundingCheckAsync();
}

public sealed record QaGroundingReportSummary(
    string OutputPath,
    int TotalPds,
    int TotalCriteriaChecked,
    int UngroundedCount,
    int MissingNegativeFindingCount,
    int AllFourCriteriaMissingCount);
