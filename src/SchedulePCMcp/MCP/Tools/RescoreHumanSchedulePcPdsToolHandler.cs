using System.Text.Json;
using SchedulePCMcp.Application.Interfaces;
using SchedulePCMcp.Infrastructure.Repositories;

namespace SchedulePCMcp.MCP.Tools;

/// <summary>
/// Rescores exactly the set of PDs that human reviewers flagged as Schedule P/C
/// (TEMP_PD_SCHED_PC.SCHEDULE_PC_IND = 'Y'), without touching the SCHEDULE_PC_EVAL
/// pending queue or any other staged/pending PDs. Refuses to run if the human-flagged
/// count is not exactly 90, so it never silently rescopes to a broader or narrower set.
/// </summary>
public class RescoreHumanSchedulePcPdsToolHandler : IMcpToolHandler
{
    private const int ExpectedHumanSchedulePcPdCount = 90;

    private readonly IPositionDescriptionRepository _positionDescriptionRepository;
    private readonly IScoringOrchestrator _scoringOrchestrator;

    public RescoreHumanSchedulePcPdsToolHandler(
        IPositionDescriptionRepository positionDescriptionRepository,
        IScoringOrchestrator scoringOrchestrator)
    {
        _positionDescriptionRepository = positionDescriptionRepository;
        _scoringOrchestrator = scoringOrchestrator;
    }

    public string Name => "rescore_human_schedule_pc_pds";

    public string Description => "Rescore exactly the 90 PDs that human reviewers flagged as Schedule P/C, without processing any other pending PDs";

    public object InputSchema => new
    {
        type = "object",
        properties = new { }
    };

    public async Task<object> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pdNumbers = await _positionDescriptionRepository.GetHumanSchedulePcPdNumbersAsync();

        if (pdNumbers.Count != ExpectedHumanSchedulePcPdCount)
        {
            return new
            {
                error = $"Expected exactly {ExpectedHumanSchedulePcPdCount} human-flagged Schedule P/C PDs but found {pdNumbers.Count}. No scoring was performed.",
                selectedCount = pdNumbers.Count,
                expectedCount = ExpectedHumanSchedulePcPdCount
            };
        }

        var failedPdNumbers = new List<string>();
        var scoredCount = 0;

        foreach (var pdNbr in pdNumbers)
        {
            try
            {
                await _scoringOrchestrator.ScoreAsync(pdNbr);
                scoredCount++;
            }
            catch (Exception)
            {
                failedPdNumbers.Add(pdNbr);
            }
        }

        return new
        {
            status = $"Rescore complete for {scoredCount} of {pdNumbers.Count} human-flagged Schedule P/C PDs.",
            selectedCount = pdNumbers.Count,
            scoredCount,
            failedCount = failedPdNumbers.Count,
            failedPdNumbers
        };
    }
}
