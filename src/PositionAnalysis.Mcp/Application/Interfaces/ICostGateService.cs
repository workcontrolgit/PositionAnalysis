namespace PositionAnalysis.Mcp.Application.Interfaces;

public interface ICostGateService
{
    /// <summary>Estimated total cost in USD for scoring <paramref name="pdCount"/> PDs.</summary>
    decimal Estimate(int pdCount);

    /// <summary>True when <paramref name="estimatedCost"/> exceeds the configured threshold.</summary>
    bool RequiresConfirmation(decimal estimatedCost);

    decimal ThresholdUsd { get; }
}
