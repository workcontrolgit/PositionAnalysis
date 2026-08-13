using PositionAnalysis.Mcp.Domain.Entities;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class EvaluationResultTests
{
    [Fact]
    public void ContainsSourcePdSequenceNumber()
    {
        Assert.NotNull(typeof(EvaluationResult).GetProperty("PdSeqNum"));
    }
}
