using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class RunQaTier1GroundingCheckToolHandler : IMcpToolHandler
{
    private readonly IQaGroundingReportOrchestrator _qaGroundingReportOrchestrator;

    public RunQaTier1GroundingCheckToolHandler(IQaGroundingReportOrchestrator qaGroundingReportOrchestrator)
    {
        _qaGroundingReportOrchestrator = qaGroundingReportOrchestrator;
    }

    public string Name => "run_qa_tier1_grounding_check";

    public string Description => "Runs the Tier 1 (deterministic, no-LLM, $0 cost) QA evidence-grounding check across all Complete evaluation results: flags per-criterion evidence quotes that cannot be located in the PD's own duty text, and separately flags criteria left with blank evidence (missing the required verbatim quote or explicit negative finding). Writes an Excel report to reports/schedule-pc/qa-reports, including a dedicated 'All 4 Criteria Missing' sheet listing PDs where every criterion has blank evidence. Does not call any LLM (see Tier 2 in the QA plan for the LLM-as-judge follow-up).";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var summary = await _qaGroundingReportOrchestrator.RunTier1GroundingCheckAsync();
        return new
        {
            outputPath = summary.OutputPath,
            totalPds = summary.TotalPds,
            totalCriteriaChecked = summary.TotalCriteriaChecked,
            ungroundedCount = summary.UngroundedCount,
            missingNegativeFindingCount = summary.MissingNegativeFindingCount,
            allFourCriteriaMissingCount = summary.AllFourCriteriaMissingCount
        };
    }
}
