using System.Text.Json;

namespace PositionAnalysis.Cli;

public interface ISchedulePcMcpClient
{
    Task<JsonElement> CallToolAsync(string name, object arguments);
}
