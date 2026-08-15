using System.Text.Json;

namespace PositionAnalysis.Cli;

public interface ISchedulePcMcpClient
{
    Task<JsonElement> CallToolAsync(string name, object arguments);

    /// <summary>
    /// Call a tool and receive interleaved progress notifications.
    /// </summary>
    /// <param name="name">Tool name.</param>
    /// <param name="arguments">Tool arguments (serialised as the JSON <c>arguments</c> field).</param>
    /// <param name="onProgress">
    ///   Invoked for each <c>notifications/progress</c> message received before
    ///   the final tool result arrives.  Parameters: (current, total).
    ///   Called on the read-loop thread — keep the callback fast and non-blocking.
    /// </param>
    Task<JsonElement> CallToolWithProgressAsync(string name, object arguments, Action<double, double> onProgress);
}
