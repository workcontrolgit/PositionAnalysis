using PositionAnalysis.Mcp.Infrastructure.Config;
using Xunit;

namespace PositionAnalysis.Mcp.Tests;

public class AzureOpenAiSettingsTests
{
    [Fact]
    public void DefaultsMaxCompletionTokensToScoringBudget()
    {
        var property = typeof(AzureOpenAiSettings).GetProperty("MaxCompletionTokens");

        Assert.NotNull(property);
        Assert.Equal(16384, property!.GetValue(new AzureOpenAiSettings()));
    }
}
