using System.Text.Json;
using SchedulePCMcp.Infrastructure.Repositories;

namespace SchedulePCMcp.MCP.Tools;

public class GetLatestRunToolHandler : IMcpToolHandler
{
    private readonly ISchedulePCEvalRepository _evalRepository;

    public GetLatestRunToolHandler(ISchedulePCEvalRepository evalRepository)
    {
        _evalRepository = evalRepository;
    }

    public string Name => "get_latest_run";

    public string Description => "Get the most recent run id from Schedule PC evaluation history";

    public object InputSchema => new
    {
        type = "object",
        required = Array.Empty<string>(),
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var runId = await _evalRepository.GetLatestRunIdAsync();
        return new
        {
            runId = runId ?? string.Empty,
            found = !string.IsNullOrWhiteSpace(runId)
        };
    }
}
