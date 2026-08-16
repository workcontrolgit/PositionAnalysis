using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Config;

namespace PositionAnalysis.Mcp.Application.Services;

public class CostGateService : ICostGateService
{
    private readonly CostGateSettings _settings;

    public CostGateService(IOptions<CostGateSettings> settings)
    {
        _settings = settings.Value;
    }

    public decimal ThresholdUsd => _settings.ThresholdUsd;

    public decimal Estimate(int pdCount) =>
        pdCount * _settings.EstimatedCostPerPdUsd;

    public bool RequiresConfirmation(decimal estimatedCost) =>
        estimatedCost > _settings.ThresholdUsd;
}
