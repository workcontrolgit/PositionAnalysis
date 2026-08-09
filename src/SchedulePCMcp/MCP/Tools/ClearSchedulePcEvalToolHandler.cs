using System.Text.Json;
using SchedulePCMcp.Infrastructure.Repositories;

namespace SchedulePCMcp.MCP.Tools;

public class ClearSchedulePcEvalToolHandler : IMcpToolHandler
{
    private readonly ISchedulePCEvalRepository _evalRepository;

    public ClearSchedulePcEvalToolHandler(ISchedulePCEvalRepository evalRepository)
    {
        _evalRepository = evalRepository;
    }

    public string Name => "clear_schedule_pc_eval";

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
