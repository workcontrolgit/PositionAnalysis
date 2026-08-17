using System.Text.Json;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Repositories;

namespace PositionAnalysis.Mcp.MCP.Tools;

/// <summary>
/// Forces a fresh LLM rescore of only the PDs with a human Schedule P/C baseline determination
/// (TEMP_PD_SCHED_PC.SCHEDULE_PC_IND = 'Y'), overwriting any existing result regardless of status.
/// </summary>
public class RescoreConfirmedSchedulePcToolHandler : IMcpStreamingToolHandler
{
    private readonly ParallelBatchScorer _scorer;
    private readonly IPositionDescriptionRepository _pdRepository;
    private readonly ICostGateService _costGate;

    public RescoreConfirmedSchedulePcToolHandler(
        ParallelBatchScorer scorer,
        IPositionDescriptionRepository pdRepository,
        ICostGateService costGate)
    {
        _scorer = scorer;
        _pdRepository = pdRepository;
        _costGate = costGate;
    }

    public string Name => "rescore_confirmed_schedule_pc";

    public string Description =>
        "Force a fresh LLM rescore, in parallel, of only the PDs with a human-confirmed Schedule P/C baseline determination " +
        "(SCHEDULE_PC_IND = 'Y'), overwriting any existing result regardless of current status. " +
        "Returns a requiresConfirmation payload first — re-call with confirmed: true to proceed.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            confirmed = new
            {
                type = "boolean",
                description = "Set to true to approve execution after the confirmation prompt."
            }
        }
    };

    public Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
        => InvokeStreamingAsync(arguments, null, null, cancellationToken);

    public async Task<object> InvokeStreamingAsync(
        JsonElement arguments,
        string? progressToken,
        Func<double, double, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var pds = await _pdRepository.GetHumanSchedulePcPdNumbersAsync();
        if (pds.Count == 0)
            return new { error = "No PDs found with SCHEDULE_PC_IND = 'Y'" };

        var confirmed = arguments.TryGetProperty("confirmed", out var c) &&
                        c.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            var estimatedCost = _costGate.Estimate(pds.Count);
            return new
            {
                requiresConfirmation = true,
                pendingCount = pds.Count,
                estimatedCostUsd = estimatedCost,
                thresholdUsd = _costGate.ThresholdUsd
            };
        }

        var result = await _scorer.RunAsync(pds, reportProgressAsync, cancellationToken);

        return new
        {
            pds,
            result.Total,
            result.Completed,
            result.Failed,
            status = $"{result.Message} Run generate_documents_by_pd to regenerate Word forms."
        };
    }
}
