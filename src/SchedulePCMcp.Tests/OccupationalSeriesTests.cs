using SchedulePCMcp.Domain.ValueObjects;
using Xunit;

namespace SchedulePCMcp.Tests;

public class OccupationalSeriesTests
{
    [Fact]
    public void Constructor_PreservesFiveDigitOracleSeries()
    {
        var series = new OccupationalSeries("00130");

        Assert.Equal("00130", series.Code);
    }
}