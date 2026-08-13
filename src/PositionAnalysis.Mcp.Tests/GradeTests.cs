using PositionAnalysis.Mcp.Domain.ValueObjects;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class GradeTests
{
    [Fact]
    public void Constructor_AcceptsZeroGradeFromOracle()
    {
        var grade = new Grade(0);

        Assert.Equal(0, grade.Value);
    }

    [Fact]
    public void ToString_ReturnsOnlyTheTwoDigitGrade()
    {
        var grade = new Grade(14);

        Assert.Equal("14", grade.ToString());
    }
}
