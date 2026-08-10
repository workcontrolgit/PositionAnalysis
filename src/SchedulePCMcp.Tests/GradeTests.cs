using SchedulePCMcp.Domain.ValueObjects;
using Xunit;

namespace SchedulePCMcp.Tests;

public class GradeTests
{
    [Fact]
    public void Constructor_AcceptsZeroGradeFromOracle()
    {
        var grade = new Grade(0);

        Assert.Equal(0, grade.Value);
    }
}