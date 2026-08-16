using Microsoft.Extensions.Options;
using PositionAnalysis.Mcp.Application.Services;
using PositionAnalysis.Mcp.Infrastructure.Config;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class CostGateServiceTests
{
    private static CostGateService MakeService(decimal threshold = 5.00m, decimal costPerPd = 0.05m)
    {
        var settings = Options.Create(new CostGateSettings
        {
            ThresholdUsd = threshold,
            EstimatedCostPerPdUsd = costPerPd
        });
        return new CostGateService(settings);
    }

    [Fact]
    public void Estimate_MultipliesPdCountByCostPerPd()
    {
        var svc = MakeService(costPerPd: 0.05m);
        Assert.Equal(5.00m, svc.Estimate(100));
    }

    [Fact]
    public void RequiresConfirmation_ReturnsFalse_WhenCostBelowThreshold()
    {
        var svc = MakeService(threshold: 5.00m);
        Assert.False(svc.RequiresConfirmation(4.99m));
    }

    [Fact]
    public void RequiresConfirmation_ReturnsFalse_WhenCostEqualsThreshold()
    {
        var svc = MakeService(threshold: 5.00m);
        Assert.False(svc.RequiresConfirmation(5.00m));
    }

    [Fact]
    public void RequiresConfirmation_ReturnsTrue_WhenCostExceedsThreshold()
    {
        var svc = MakeService(threshold: 5.00m);
        Assert.True(svc.RequiresConfirmation(5.01m));
    }

    [Fact]
    public void ThresholdUsd_ReturnsConfiguredValue()
    {
        var svc = MakeService(threshold: 10.00m);
        Assert.Equal(10.00m, svc.ThresholdUsd);
    }
}
