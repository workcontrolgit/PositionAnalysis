using SchedulePCMcp.Domain.Entities;
using Xunit;

namespace SchedulePCMcp.Tests;

public class EvaluationResultTests
{
    [Fact]
    public void ContainsSourcePdSequenceNumber()
    {
        Assert.NotNull(typeof(EvaluationResult).GetProperty("PdSeqNum"));
    }
}