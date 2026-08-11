using System.Text.Json;

namespace SchedulePC;

public interface ISchedulePcMcpClient
{
    Task<JsonElement> CallToolAsync(string name, object arguments);
}