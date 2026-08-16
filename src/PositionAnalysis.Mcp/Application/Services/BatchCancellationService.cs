namespace PositionAnalysis.Mcp.Application.Services;

/// <summary>
/// Singleton that tracks the CancellationTokenSource for the currently running
/// ParallelBatchScorer so that the cancel_current_batch MCP tool can stop it
/// from the CLI without relying on notifications/cancelled.
/// </summary>
public sealed class BatchCancellationService
{
    private CancellationTokenSource? _currentCts;
    private readonly object _lock = new();

    /// <summary>
    /// Called by ParallelBatchScorer before starting a batch.
    /// Returns a new CTS linked to <paramref name="outer"/> that is also
    /// cancelled when <see cref="CancelCurrent"/> is called.
    /// The caller is responsible for disposing the returned CTS.
    /// </summary>
    public CancellationTokenSource Register(CancellationToken outer)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outer);
        lock (_lock) { _currentCts = linked; }
        return linked;
    }

    /// <summary>Cancels the currently registered batch (if any).</summary>
    public void CancelCurrent()
    {
        CancellationTokenSource? cts;
        lock (_lock) { cts = _currentCts; }
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
