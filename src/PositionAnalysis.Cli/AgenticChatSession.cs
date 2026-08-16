// src/PositionAnalysis.Cli/AgenticChatSession.cs
using System.Text.Json;
using Microsoft.Extensions.AI;
using Serilog;
using Spectre.Console;

namespace PositionAnalysis.Cli;

/// <summary>
/// Interactive chat session where the LLM autonomously calls MCP tools.
/// Replaces the keyword-dispatch PositionAnalysisChatClient.
/// </summary>
public sealed class AgenticChatSession
{
    private const string SystemPrompt =
        "You are PositionAnalysis Assistant, helping evaluate federal positions under Schedule " +
        "Policy/Career authority. You have access to tools for staging, scoring, generating " +
        "evaluation documents, exporting results, and querying the Oracle database. " +
        "Call tools when the user requests workflow actions. " +
        "Cost-gate confirmations are handled automatically by the CLI — you do not need to ask " +
        "the user to confirm or re-call tools with confirmed:true.";

    private readonly IChatClient _chatClient;
    private readonly McpToolRegistry _registry;
    private readonly string _modelName;
    private readonly List<ChatMessage> _history;
    private readonly Action<bool>? _setSuppressProgress;

    public AgenticChatSession(IChatClient chatClient, McpToolRegistry registry, string modelName,
        Action<bool>? setSuppressProgress = null)
    {
        _chatClient = chatClient;
        _registry = registry;
        _modelName = modelName;
        _history = [new ChatMessage(ChatRole.System, SystemPrompt)];
        _setSuppressProgress = setSuppressProgress;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new Rule("[bold cyan]Schedule PC Position Evaluation[/]")
            .RuleStyle("cyan").LeftJustified());
        AnsiConsole.MarkupLine("[grey]Authority: EO Implementing Schedule Policy/Career[/]");
        AnsiConsole.MarkupLine($"[teal]Model:[/] [bold]{Markup.Escape(_modelName)}[/]");
        AnsiConsole.MarkupLine($"[grey]Tools available:[/] [bold]{_registry.Tools.Count}[/]\n");

        var options = new ChatOptions { Tools = [.. _registry.Tools] };

        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.Markup("[bold cyan]You[/] [grey]▶[/] ");
            var input = Console.ReadLine();

            if (input == null || cancellationToken.IsCancellationRequested)
                break;

            var trimmed = input.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed is "exit" or "quit" or "bye")
            {
                AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
                break;
            }

            _history.Add(new ChatMessage(ChatRole.User, trimmed));

            try
            {
                await RunAgenticTurnAsync(options, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during agentic turn");
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            }
        }
    }

    private async Task RunAgenticTurnAsync(ChatOptions options, CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await _chatClient.GetResponseAsync(_history, options, cancellationToken);
            _history.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (calls.Count == 0)
            {
                // No tool calls — print the text response and return to the prompt
                var text = string.Join("", response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<TextContent>()
                    .Select(t => t.Text ?? ""));

                if (!string.IsNullOrWhiteSpace(text))
                {
                    AnsiConsole.MarkupLine("\n[bold cyan]Assistant[/] [grey]▶[/]");
                    MarkdigSpectreRenderer.Render(text);
                    LogTokenUsage(response.Usage);
                }
                return;
            }

            // Execute all tool calls and feed results back
            var resultMessage = new ChatMessage { Role = ChatRole.Tool };
            bool wasCancelled = false;
            foreach (var call in calls)
            {
                AnsiConsole.MarkupLine($"[grey]  → calling [bold]{Markup.Escape(call.Name)}[/]…[/]");

                string resultJson;
                try
                {
                    var args = new Dictionary<string, object?>(call.Arguments ?? new Dictionary<string, object?>());
                    resultJson = await CallToolWithProgressUiAsync(call.Name, args, cancellationToken);

                    // Intercept cost-gate confirmation before the LLM sees it
                    resultJson = await HandleCostGateAsync(call.Name, args, resultJson, cancellationToken);
                }
                catch (Exception ex)
                {
                    resultJson = $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
                    AnsiConsole.MarkupLine($"[red]  Tool error:[/] {Markup.Escape(ex.Message)}");
                }

                if (IsCancelledResult(resultJson))
                    wasCancelled = true;

                resultMessage.Contents.Add(
                    new FunctionResultContent(call.CallId, resultJson));
            }

            _history.Add(resultMessage);

            // If the user cancelled the batch, return to the You ▶ prompt immediately
            // instead of feeding the result to the LLM (which would try another tool call
            // against the still-busy single-threaded MCP server).
            if (wasCancelled)
                return;

            // Loop: feed results back to the LLM for its next response
        }
    }

    /// <summary>
    /// Runs a tool call in the background. Fast tools (&lt;2 s) return transparently.
    /// Slow tools show an Esc-to-cancel hint and hand off to <see cref="WaitWithEscCancelAsync"/>.
    /// </summary>
    private async Task<string> CallToolWithProgressUiAsync(
        string name,
        Dictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        _setSuppressProgress?.Invoke(false); // re-enable progress for this tool call
        using var toolCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var toolTask = _registry.CallToolAsync(name, args, toolCts.Token);

        // Fast path: tool finished within 2 seconds — no UI needed
        if (await Task.WhenAny(toolTask, Task.Delay(2000, CancellationToken.None)) == toolTask)
            return await toolTask;

        AnsiConsole.MarkupLine("[grey]  (Running… press [bold]Esc[/] to cancel)[/]");
        return await WaitWithEscCancelAsync(toolTask, toolCts, BuildEscCallback());
    }

    /// <summary>
    /// If <paramref name="resultJson"/> is a cost-gate payload, shows a 1/2 confirmation prompt
    /// and either re-calls the tool with confirmed:true or returns a cancellation message.
    /// Otherwise returns <paramref name="resultJson"/> unchanged.
    /// </summary>
    private async Task<string> HandleCostGateAsync(
        string toolName,
        Dictionary<string, object?> originalArgs,
        string resultJson,
        CancellationToken cancellationToken)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(resultJson); }
        catch { return resultJson; }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("requiresConfirmation", out var flag) || !flag.GetBoolean())
                return resultJson;

            var count = root.TryGetProperty("pendingCount", out var c) ? c.GetInt32() : 0;
            var cost  = root.TryGetProperty("estimatedCostUsd", out var e) ? e.GetDecimal() : 0m;

            AnsiConsole.WriteLine();
            var panel = new Panel(
                    $"[yellow]Records to process:[/] [bold]{count:N0}[/]\n" +
                    (cost > 0
                        ? $"[yellow]Estimated cost:    [/] [bold]${cost:F2} USD[/]"
                        : "[yellow]Estimated cost:    [/] [bold]$0.00 (free — local model)[/]"))
                .Header("[bold yellow]⚠  Confirmation Required[/]")
                .BorderColor(Color.Yellow);
            AnsiConsole.Write(panel);

            AnsiConsole.MarkupLine("  [bold]1[/] — Yes, proceed");
            AnsiConsole.MarkupLine("  [bold]2[/] — No, cancel");
            AnsiConsole.WriteLine();

            string? choice;
            do
            {
                AnsiConsole.Markup("[bold yellow]Enter choice (1 or 2):[/] ");
                choice = Console.ReadLine()?.Trim();
            }
            while (choice is not "1" and not "2");

            if (choice == "2")
            {
                AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                return "{\"cancelled\":true,\"message\":\"User cancelled the batch operation.\"}";
            }

            // User confirmed — re-call with confirmed:true, show the Batch Running panel
            AnsiConsole.MarkupLine($"[grey]  → re-calling [bold]{Markup.Escape(toolName)}[/] (confirmed)…[/]");
            var confirmedArgs = new Dictionary<string, object?>(originalArgs) { ["confirmed"] = true };

            using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(
                    $"[grey]Scoring [bold]{count:N0}[/] PDs.\n" +
                    "Progress updates appear above.\n" +
                    "Press [bold]Esc[/] to cancel without exiting the CLI.[/]")
                .Header("[bold cyan]Batch Running[/]")
                .BorderColor(Color.Cyan1));
            AnsiConsole.WriteLine();

            _setSuppressProgress?.Invoke(false); // ensure progress is enabled for the batch
            var batchTask = _registry.CallToolAsync(toolName, confirmedArgs, batchCts.Token);
            return await WaitWithEscCancelAsync(batchTask, batchCts, BuildEscCallback());
        }
    }

    /// <summary>
    /// Waits for <paramref name="toolTask"/> while listening for Esc on a dedicated background thread.
    /// <para>
    /// If Esc is pressed: cancels <paramref name="toolCts"/>, waits up to 5 seconds for the MCP
    /// client to acknowledge, then abandons the task if it still hasn't completed.
    /// </para>
    /// <para>
    /// If the outer CancellationToken fires (Ctrl+C): <paramref name="toolTask"/> is cancelled via
    /// the linked <paramref name="toolCts"/> and an OperationCanceledException propagates normally.
    /// </para>
    /// </summary>
    /// <summary>
    /// Returns a callback that suppresses progress output AND sends cancel_current_batch
    /// to the MCP server so the server-side batch stops when ESC is pressed.
    /// </summary>
    private Func<Task> BuildEscCallback() => async () =>
    {
        _setSuppressProgress?.Invoke(true);
        try
        {
            // Fire cancel_current_batch with CancellationToken.None so it isn't affected
            // by the already-cancelled toolCts. The server's free read loop handles it
            // immediately while the batch runs in a background task.
            await _registry.CallToolAsync(
                "cancel_current_batch", new Dictionary<string, object?>(), CancellationToken.None);
        }
        catch { /* best-effort — don't block the ESC path if the tool fails */ }
    };

    private static async Task<string> WaitWithEscCancelAsync(
        Task<string> toolTask,
        CancellationTokenSource toolCts,
        Func<Task>? onEscAsync = null)
    {
        // Signal that ESC was pressed (true) or reader exited without ESC (false)
        var escTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Dedicated thread: Console.ReadKey is blocking and works reliably only on its own thread.
        // We avoid Console.ReadKey() unless KeyAvailable is true to prevent blocking when the tool
        // completes — the thread exits on the next 50ms sleep after readerCts is cancelled.
        using var readerCts = new CancellationTokenSource();
        var readerThread = new Thread(() =>
        {
            try
            {
                while (!readerCts.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            escTcs.TrySetResult(true);
                            return;
                        }
                    }
                    Thread.Sleep(50);
                }
            }
            catch { /* ignore console exceptions (e.g. redirected stdin) */ }
            escTcs.TrySetResult(false);
        }) { IsBackground = true, Name = "EscReader" };
        readerThread.Start();

        // Wait for whichever happens first: tool completes or ESC pressed
        var winner = await Task.WhenAny(toolTask, escTcs.Task);
        readerCts.Cancel(); // signal reader thread to exit (exits within ~50 ms)

        if (winner == escTcs.Task && await escTcs.Task)
        {
            Console.Error.WriteLine(); // end the \r progress line
            AnsiConsole.MarkupLine("[yellow]  Cancellation requested… please wait.[/]");

            // Send cancel_current_batch and confirm the server received the signal.
            if (onEscAsync is not null)
                try { await onEscAsync(); } catch { /* best-effort */ }

            // Do NOT cancel toolCts here — that would throw OCE client-side immediately,
            // making toolTask complete before the server finishes its in-flight records.
            // Instead wait for toolTask naturally: the server will send its response once
            // all in-flight LLM calls finish (after the batch CTS was cancelled above).
            AnsiConsole.MarkupLine("[grey]  Waiting for in-flight records to finish…[/]");

            if (await Task.WhenAny(toolTask, Task.Delay(60_000, CancellationToken.None)) != toolTask)
            {
                // Server didn't respond within 60 s — force cancel and abandon.
                await toolCts.CancelAsync();
                _ = toolTask.ContinueWith(_ => { }, CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
            }
            else
            {
                // Observe the task to suppress UnobservedTaskException.
                _ = toolTask.ContinueWith(_ => { }, CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
            }

            AnsiConsole.MarkupLine("[yellow]  Cancelled.[/]");
            return "{\"cancelled\":true,\"message\":\"Batch cancelled by user (Esc).\"}";
        }

        Console.Error.WriteLine(); // ensure progress line is terminated

        try
        {
            var result = await toolTask;
            AnsiConsole.MarkupLine("[green]  Done.[/]");
            return result;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]  Cancelled. CLI is ready.[/]");
            return "{\"cancelled\":true,\"message\":\"Operation cancelled.\"}";
        }
    }

    private static bool IsCancelledResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("cancelled", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
    }

    private static void LogTokenUsage(UsageDetails? usage)
    {
        if (usage is null) return;
        Log.Debug("Token usage — input: {Input}, output: {Output}",
            usage.InputTokenCount, usage.OutputTokenCount);
    }
}
