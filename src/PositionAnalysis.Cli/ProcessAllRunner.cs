using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Serilog;

namespace PositionAnalysis.Cli;

public sealed class ProcessAllRunner
{
    private readonly McpClient _paClient;
    private readonly TimeSpan _pollInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ILogger _logger;

    public ProcessAllRunner(
        McpClient paClient,
        TimeSpan pollInterval,
        Func<TimeSpan, Task> delayAsync,
        ILogger? logger = null)
        : this(paClient, pollInterval, (delay, _) => delayAsync(delay), logger)
    {
    }

    public ProcessAllRunner(
        McpClient paClient,
        TimeSpan pollInterval,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        ILogger? logger = null)
    {
        _paClient = paClient;
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
            await CallAndParseAsync("run_unattended_scoring", null, cancellationToken);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await CallAndParseAsync("get_unattended_queue_status", null, cancellationToken);
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

    private async Task<JsonElement> CallAndParseAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? args,
        CancellationToken cancellationToken)
    {
        // Note: McpClient.CallToolAsync(string, IReadOnlyDictionary?, IProgress?, RequestOptions?) does not
        // accept CancellationToken directly. Cancellation is guarded by ThrowIfCancellationRequested() in callers.
        var result = await _paClient.CallToolAsync(toolName, args, progress: null);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "{}";
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static QueueStatusResponse ParseQueueStatus(JsonElement response)
    {
        var pending = GetRequiredInt32(response, "pending");
        var inProgress = GetRequiredInt32(response, "inProgress");
        var isDrained = GetRequiredBoolean(response, "isDrained");
        var calculatedDrained = pending == 0 && inProgress == 0;
        if (isDrained != calculatedDrained)
            throw new InvalidOperationException("get_unattended_queue_status response has an isDrained field inconsistent with pending and inProgress.");

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
            throw new InvalidOperationException($"get_unattended_queue_status response is missing a valid integer '{propertyName}' field.");
        return value;
    }

    private static bool GetRequiredBoolean(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"get_unattended_queue_status response is missing a valid boolean '{propertyName}' field.");
        return property.GetBoolean();
    }

    private static string? GetRequiredNullableString(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
            throw new InvalidOperationException($"get_unattended_queue_status response is missing a valid string or null '{propertyName}' field.");
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
