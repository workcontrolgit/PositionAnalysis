using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Interfaces;
using PositionAnalysis.Mcp.Infrastructure.Config;

namespace PositionAnalysis.Mcp.Application.Services;

public class CostGateService : ICostGateService
{
    private readonly CostGateSettings _settings;
    private readonly bool _isFreeProvider;

    public CostGateService(IOptions<CostGateSettings> settings, IOptions<AiSettings> aiSettings)
    {
        _settings = settings.Value;
        _isFreeProvider = aiSettings.Value.Type.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
    }

    public decimal ThresholdUsd => _settings.ThresholdUsd;

    public decimal Estimate(int pdCount) =>
        _isFreeProvider ? 0m : pdCount * _settings.EstimatedCostPerPdUsd;

    public bool RequiresConfirmation(decimal estimatedCost) =>
        estimatedCost > _settings.ThresholdUsd;
}
