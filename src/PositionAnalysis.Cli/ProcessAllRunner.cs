using System.Text.Json;
using Serilog;

namespace PositionAnalysis.Cli;

public sealed class ProcessAllRunner
{
    private readonly ISchedulePcMcpClient _mcpClient;
    private readonly TimeSpan _pollInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ILogger _logger;

    public ProcessAllRunner(
        ISchedulePcMcpClient mcpClient,
        TimeSpan pollInterval,
        Func<TimeSpan, Task> delayAsync,
        ILogger? logger = null)
        : this(mcpClient, pollInterval, (delay, _) => delayAsync(delay), logger)
    {
    }

    public ProcessAllRunner(
        ISchedulePcMcpClient mcpClient,
        TimeSpan pollInterval,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        ILogger? logger = null)
    {
        _mcpClient = mcpClient;
        _pollInterval = pollInterval;
        _delayAsync = delayAsync;
        _logger = logger ?? Log.Logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Information("Starting unattended Schedule PC scoring run");
            cancellationToken.ThrowIfCancellationRequested();
            await _mcpClient.CallToolAsync("run_unattended_scoring", new { });

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await _mcpClient.CallToolAsync("get_queue_status", new { });
                var status = ParseQueueStatus(response);

                _logger.Information(
                    "Schedule PC queue status: Pending={Pending}, InProgress={InProgress}, Complete={Complete}, Failed={Failed}, IsDrained={IsDrained}, RunFailed={RunFailed}",
                    status.Pending,
                    status.InProgress,
                    status.Complete,
                    status.Failed,
                    status.IsDrained,
                    status.RunFailed);

                if (status.RunFailed)
                {
                    _logger.Error("Schedule PC unattended scoring run failed: {RunError}", status.RunError);
                    return 1;
                }

                if (status.IsDrained)
                {
                    var exitCode = status.Failed == 0 ? 0 : 1;
                    _logger.Information("Schedule PC unattended scoring run finished with exit code {ExitCode}", exitCode);
                    return exitCode;
                }

                await _delayAsync(_pollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Information("Schedule PC unattended scoring run was cancelled");
            return 2;
        }
    }

    private static QueueStatusResponse ParseQueueStatus(JsonElement response)
    {
        var pending = GetRequiredInt32(response, "pending");
        var inProgress = GetRequiredInt32(response, "inProgress");
        var isDrained = GetRequiredBoolean(response, "isDrained");
        var calculatedDrained = pending == 0 && inProgress == 0;
        if (isDrained != calculatedDrained)
            throw new InvalidOperationException("get_queue_status response has an isDrained field inconsistent with pending and inProgress.");

        return new QueueStatusResponse(
            pending,
            inProgress,
            GetRequiredInt32(response, "complete"),
            GetRequiredInt32(response, "failed"),
            isDrained,
            GetRequiredBoolean(response, "runFailed"),
            GetRequiredNullableString(response, "runError"));
    }

    private static int GetRequiredInt32(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value))
            throw new InvalidOperationException($"get_queue_status response is missing a valid integer '{propertyName}' field.");

        return value;
    }

    private static bool GetRequiredBoolean(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) || property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"get_queue_status response is missing a valid boolean '{propertyName}' field.");

        return property.GetBoolean();
    }

    private static string? GetRequiredNullableString(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) || property.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
            throw new InvalidOperationException($"get_queue_status response is missing a valid string or null '{propertyName}' field.");

        return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
    }

    private sealed record QueueStatusResponse(
        int Pending,
        int InProgress,
        int Complete,
        int Failed,
        bool IsDrained,
        bool RunFailed,
        string? RunError);
}
