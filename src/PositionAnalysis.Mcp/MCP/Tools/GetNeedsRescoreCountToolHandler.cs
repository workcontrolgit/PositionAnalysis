using System.Text.Json;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Returns the count of PDs flagged needs_rescore = 'Y', optionally filtered by series or PD numbers.
/// </summary>
public class GetNeedsRescoreCountToolHandler : IMcpToolHandler
{
    private readonly IPositionAnalysisEvalRepository _evalRepository;

    public GetNeedsRescoreCountToolHandler(IPositionAnalysisEvalRepository evalRepository)
    {
        _evalRepository = evalRepository;
    }

    public string Name => "get_needs_rescore_count";

    public string Description => "Get the count of PDs flagged needs_rescore = 'Y', optionally filtered by series or PD numbers";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            series = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Optional list of 5-digit occupational series codes to filter by"
            },
            pdNumbers = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Optional list of PD numbers to filter by"
            }
        }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pdNumbers = arguments.GetStringList("pdNumbers");
        var series = arguments.GetStringList("series");

        int count;
        if (pdNumbers.Count > 0)
            count = (await _evalRepository.GetNeedsRescoreByPdNumbersAsync(pdNumbers)).Count;
        else if (series.Count > 0)
            count = (await _evalRepository.GetNeedsRescoreBySeriesAsync(series)).Count;
        else
            count = (await _evalRepository.GetNeedsRescoreAsync()).Count;

        return new { count };
    }
}
