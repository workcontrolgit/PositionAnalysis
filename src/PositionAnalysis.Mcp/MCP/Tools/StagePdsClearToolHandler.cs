using System.Text.Json;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

public class StagePdsClearToolHandler : IMcpToolHandler
{
    private readonly IPositionAnalysisEvalRepository _evalRepository;

    public StagePdsClearToolHandler(IPositionAnalysisEvalRepository evalRepository)
    {
        _evalRepository = evalRepository;
    }

    public string Name => "stage_pds_clear";

    public string Description => "Delete all records from SCHEDULE_PC_EVAL";

    public object InputSchema => new
    {
        type = "object",
        required = Array.Empty<string>(),
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var deletedCount = await _evalRepository.DeleteAllAsync();

        return new
        {
            status = "cleared",
            deletedCount
        };
    }
}
