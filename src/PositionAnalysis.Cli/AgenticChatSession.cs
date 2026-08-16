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
        "the user to confirm or re-call tools with confirmed:true. " +
        "When a tool result contains {\"displayed\":true}, the data was already rendered as a table " +
        "in the terminal. Respond with one brief sentence only — do NOT re-list or summarize the data.";

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
        AnsiConsole.Write(new Rule("[bold cyan]Position Analysis Tool[/]")
            .RuleStyle("cyan").LeftJustified());
        AnsiConsole.MarkupLine("[grey]Purpose: EO Implementing Schedule Policy/Career[/]");
        AnsiConsole.MarkupLine($"[teal]Model:[/] [bold]{Markup.Escape(_modelName)}[/]");
        AnsiConsole.MarkupLine($"[grey]Tools available:[/] [bold]{_registry.Tools.Count}[/]");

        var options = new ChatOptions { Tools = [.. _registry.Tools] };

        // Show menu once at startup
        AnsiConsole.WriteLine();
        RenderMenu();
        AnsiConsole.MarkupLine("[grey]Type [bold]?[/] at any time to show this menu again.[/]");

        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[bold cyan]You[/] [grey]▶[/] ");
            var input = Console.ReadLine();

            if (input == null || cancellationToken.IsCancellationRequested)
                break;

            var trimmed = input.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed is "exit" or "quit" or "bye" or "q" or "Q")
            {
                AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
                break;
            }

            // On-demand menu
            if (trimmed is "?" or "menu" or "help")
            {
                RenderMenu();
                continue;
            }

            // Resolve menu shortcut → natural-language prompt (may prompt for params)
            var prompt = ResolveMenuShortcut(trimmed);
            if (prompt == null)
                continue; // invalid shortcut — re-prompt

            _history.Add(new ChatMessage(ChatRole.User, prompt));

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

    private static void RenderMenu()
    {
        var grid = new Grid().AddColumns(3);

        // Row 1: Status | Process | Generate Word Docs
        grid.AddRow(
            new Panel(
                    "[bold] 1[/] All series\n" +
                    "[bold] 2[/] By series\n" +
                    "[bold] 3[/] By org code\n" +
                    "[bold] 4[/] By PD number")
                .Header("[bold yellow] Report Status [/]")
                .BorderColor(Color.Yellow)
                .Padding(1, 0),

            new Panel(
                    "[bold] 5[/] All pending\n" +
                    "[bold] 6[/] By series\n" +
                    "[bold] 7[/] By org code\n" +
                    "[bold] 8[/] By PD number")
                .Header("[bold green] Evaluate [/]")
                .BorderColor(Color.Green)
                .Padding(1, 0),

            new Panel(
                    "[bold] 9[/] All\n" +
                    "[bold]10[/] By series\n" +
                    "[bold]11[/] By org code\n" +
                    "[bold]12[/] By PD number")
                .Header("[bold cyan] Generate Word [/]")
                .BorderColor(Color.Cyan1)
                .Padding(1, 0));

        // Row 2: Export to Excel | Manage | (hint)
        grid.AddRow(
            new Panel(
                    "[bold]13[/] All\n" +
                    "[bold]14[/] By series\n" +
                    "[bold]15[/] By org code\n" +
                    "[bold]16[/] By PD number")
                .Header("[bold magenta] Export Excel [/]")
                .BorderColor(Color.Magenta1)
                .Padding(1, 0),

            new Panel(
                    "[bold]17[/] Stage PDs\n" +
                    "[bold]18[/] Clear staged PDs\n" +
                    "[bold]19[/] Reset failed → staged\n" +
                    "[bold]20[/] Run unattended scoring\n" +
                    "[bold]21[/] Unattended queue status\n" +
                    "[bold]22[/] Re-score by PD number\n" +
                    "[bold]23[/] Re-score by series")
                .Header("[bold blue] Manage [/]")
                .BorderColor(Color.Blue)
                .Padding(1, 0),

            new Panel(
                    "[grey]Type a number or ask a\nquestion in plain English.\n\n[bold]?[/] — show menu\n[bold]Q[/] — quit[/]")
                .Header("[grey] Help [/]")
                .BorderColor(Color.Grey)
                .Padding(1, 0));

        AnsiConsole.Write(grid);
    }

    /// <summary>
    /// Maps a menu shortcut number to a natural-language prompt for the LLM.
    /// Prompts for required parameters (series, org code, PD numbers) inline.
    /// Returns the free-text input unchanged if it is not a recognised shortcut.
    /// Returns null if the user entered an invalid number.
    /// </summary>
    private static string? ResolveMenuShortcut(string input)
    {
        return input switch
        {
            // ── Status ───────────────────────────────────────────────────────────
            "1" => "Show processing status for all occupational series.",
            "2" => PromptParam("Series codes (comma-separated, e.g. 00301,00560)",
                       v => $"Show processing status for series {v}."),
            "3" => PromptParam("Org/bureau codes (comma-separated, e.g. 1500,1530)",
                       v => $"Show processing status for org codes {v}."),
            "4" => PromptParam("PD numbers (comma-separated, e.g. 201921,201881)",
                       v => $"Show processing status for PD numbers {v}."),

            // ── Process ───────────────────────────────────────────────────────────
            "5" => "Process all pending PDs.",
            "6" => PromptParam("Series codes (comma-separated, e.g. 00301,00560)",
                       v => $"Process batch for series {v}."),
            "7" => PromptParam("Org/bureau codes (comma-separated, e.g. 1500,1530)",
                       v => $"Process batch for org codes {v}."),
            "8" => PromptParam("PD numbers (comma-separated, e.g. 201921,201881)",
                       v => $"Process batch for PD numbers {v}."),

            // ── Generate Word Docs ────────────────────────────────────────────────
            "9"  => "Generate Word evaluation documents for all evaluated PDs.",
            "10" => PromptParam("Series codes (comma-separated, e.g. 00301,00560)",
                        v => $"Generate Word evaluation documents for series {v}."),
            "11" => PromptParam("Org/bureau codes (comma-separated, e.g. 1500,1530)",
                        v => $"Generate Word evaluation documents for org codes {v}."),
            "12" => PromptParam("PD numbers (comma-separated, e.g. 201921,201881)",
                        v => $"Generate Word evaluation documents for PD numbers {v}."),

            // ── Export to Excel ───────────────────────────────────────────────────
            "13" => "Export all evaluation results to Excel.",
            "14" => PromptParam("Series codes (comma-separated, e.g. 00301,00560)",
                        v => $"Export evaluation results to Excel for series {v}."),
            "15" => PromptParam("Org/bureau codes (comma-separated, e.g. 1500,1530)",
                        v => $"Export evaluation results to Excel for org codes {v}."),
            "16" => PromptParam("PD numbers (comma-separated, e.g. 201921,201881)",
                        v => $"Export evaluation results to Excel for PD numbers {v}."),

            // ── Manage ────────────────────────────────────────────────────────────
            "17" => "Stage PDs for evaluation.",
            "18" => "Clear all staged PDs.",
            "19" => "Reset all failed evaluations back to staged for retry.",
            "20" => "Run unattended scoring for all staged pending PDs.",
            "21" => "Show unattended queue status.",
            "22" => PromptParam("PD numbers to re-score (comma-separated, e.g. 201921,201881)",
                        v => $"Re-score PD numbers {v}."),
            "23" => PromptParam("Series codes to re-score (comma-separated, e.g. 00301,00560)",
                        v => $"Re-score all PDs in series {v}."),

            // ── Free-text or unknown ──────────────────────────────────────────────
            _ when input.All(char.IsDigit) => null, // numeric but not a valid menu item
            _ => input                               // treat as free-form chat
        };
    }

    /// <summary>
    /// Prompts the user for a parameter value then builds a prompt via <paramref name="buildPrompt"/>.
    /// Returns null if the user enters nothing.
    /// </summary>
    private static string? PromptParam(string label, Func<string, string> buildPrompt)
    {
        AnsiConsole.Markup($"[grey]  {Markup.Escape(label)}:[/] ");
        var value = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            AnsiConsole.MarkupLine("[grey]  (cancelled)[/]");
            return null;
        }
        return buildPrompt(value);
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

                    // Render status results as a table; replace JSON so LLM doesn't re-narrate
                    if (TryRenderStatusTable(call.Name, resultJson))
                        resultJson = "{\"displayed\":true,\"message\":\"Results shown in table above.\"}";
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

            var hasCount = root.TryGetProperty("pendingCount", out var countEl);
            var count = hasCount ? countEl.GetInt32() : 0;
            var cost  = root.TryGetProperty("estimatedCostUsd", out var e) ? e.GetDecimal() : 0m;

            var recordLabel = toolName.StartsWith("generate_", StringComparison.OrdinalIgnoreCase)
                ? "Documents to generate:"
                : toolName.StartsWith("export_", StringComparison.OrdinalIgnoreCase)
                    ? "Records to export:    "
                    : toolName == "stage_pds_clear"
                        ? "Records to delete:    "
                        : toolName == "reset_failed_to_staged"
                            ? "Failed records:       "
                            : "Records to process:   ";

            AnsiConsole.WriteLine();
            var countLine = hasCount ? $"[yellow]{recordLabel}[/] [bold]{count:N0}[/]" : "[yellow]This action cannot be undone.[/]";
            var panelContent = countLine +
                (cost > 0 ? $"\n[yellow]Estimated cost:       [/] [bold]${cost:F2} USD[/]" : "");
            var panel = new Panel(panelContent)
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

            var actionLabel = toolName.StartsWith("generate_", StringComparison.OrdinalIgnoreCase)
                ? $"Generating Word documents for [bold]{count:N0}[/] PDs."
                : toolName.StartsWith("export_", StringComparison.OrdinalIgnoreCase)
                    ? $"Exporting [bold]{count:N0}[/] records to Excel."
                    : $"Scoring [bold]{count:N0}[/] PDs.";

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(
                    $"[grey]{actionLabel}\nProgress updates appear above.\nPress [bold]Esc[/] to cancel without exiting the CLI.[/]")
                .Header("[bold cyan]Running[/]")
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

    private static bool TryRenderStatusTable(string toolName, string resultJson)
    {
        if (!toolName.StartsWith("get_processing_status", StringComparison.OrdinalIgnoreCase))
            return false;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(resultJson); }
        catch { return false; }

        using (doc)
        {
            var root = doc.RootElement;

            // Series-level: { series: [{ series, staged, inProgress, complete, failed, percentComplete }] }
            if (root.TryGetProperty("series", out var seriesArr) && seriesArr.ValueKind == JsonValueKind.Array)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("[grey]Series[/]")
                    .AddColumn(new TableColumn("[grey]Staged[/]").RightAligned())
                    .AddColumn(new TableColumn("[grey]In Progress[/]").RightAligned())
                    .AddColumn(new TableColumn("[grey]Complete[/]").RightAligned())
                    .AddColumn(new TableColumn("[grey]Failed[/]").RightAligned())
                    .AddColumn(new TableColumn("[grey]% Done[/]").RightAligned());

                foreach (var item in seriesArr.EnumerateArray())
                {
                    var series   = item.TryGetProperty("series",          out var s)  ? s.GetString() ?? ""  : "";
                    var staged   = item.TryGetProperty("staged",          out var st) ? st.GetInt32()        : 0;
                    var inProg   = item.TryGetProperty("inProgress",      out var ip) ? ip.GetInt32()        : 0;
                    var complete = item.TryGetProperty("complete",        out var c)  ? c.GetInt32()         : 0;
                    var failed   = item.TryGetProperty("failed",          out var f)  ? f.GetInt32()         : 0;
                    var pct      = item.TryGetProperty("percentComplete", out var p)  ? p.GetDouble()        : 0.0;

                    var pctColor    = pct >= 100 ? "green" : pct > 0 ? "yellow" : "grey";
                    var failedText  = failed > 0 ? $"[red]{failed}[/]" : "0";
                    var inProgText  = inProg > 0 ? $"[yellow]{inProg}[/]" : "0";

                    table.AddRow(
                        series,
                        staged.ToString(),
                        inProgText,
                        complete.ToString(),
                        failedText,
                        $"[{pctColor}]{pct:F1}%[/]");
                }

                AnsiConsole.WriteLine();
                AnsiConsole.Write(table);
                return true;
            }

            // PD-level: { results: [{ pdNbr, title, orgCode, payPlan, series, grade, status, criteriaMet, rating, score }] }
            if (root.TryGetProperty("results", out var resultsArr) && resultsArr.ValueKind == JsonValueKind.Array)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("[grey]PD Number[/]")
                    .AddColumn("[grey]Position Title[/]")
                    .AddColumn("[grey]Org Code[/]")
                    .AddColumn("[grey]Pay Plan[/]")
                    .AddColumn("[grey]Series[/]")
                    .AddColumn(new TableColumn("[grey]Grade[/]").RightAligned())
                    .AddColumn("[grey]Status[/]")
                    .AddColumn(new TableColumn("[grey]Criteria Met[/]").RightAligned())
                    .AddColumn("[grey]Rating[/]")
                    .AddColumn(new TableColumn("[grey]AI Score[/]").RightAligned());

                foreach (var item in resultsArr.EnumerateArray())
                {
                    var pdNbr       = item.TryGetProperty("pdNbr",       out var p)   ? p.GetString()   ?? "" : "";
                    var title       = item.TryGetProperty("title",       out var t)   ? t.GetString()   ?? "" : "";
                    var orgCode     = item.TryGetProperty("orgCode",     out var o)   ? o.GetString()   ?? "" : "";
                    var payPlan     = item.TryGetProperty("payPlan",     out var pp)  ? pp.GetString()  ?? "" : "";
                    var series      = item.TryGetProperty("series",      out var s)   ? s.GetString()   ?? "" : "";
                    var grade       = item.TryGetProperty("grade",       out var g)   ? g.GetString()   ?? "" : "";
                    var status      = item.TryGetProperty("status",      out var st)  ? st.GetString()  ?? "" : "";
                    var criteriaMet = item.TryGetProperty("criteriaMet", out var cm)  ? cm.GetInt32()        : 0;
                    var rating      = item.TryGetProperty("rating",      out var r)   ? r.GetString()   ?? "" : "";
                    var score       = item.TryGetProperty("score",       out var sc)  ? sc.GetDecimal()      : 0m;

                    var statusColor = status.ToLowerInvariant() switch
                    {
                        "complete"               => "green",
                        "inprogress" or "staged" => "yellow",
                        "failed"                 => "red",
                        _                        => "grey"
                    };
                    var ratingColor = rating.ToUpperInvariant() switch
                    {
                        "HIGH"   => "green",
                        "MEDIUM" => "yellow",
                        "LOW"    => "orange1",
                        _        => "grey"
                    };
                    var scoreText = score > 0 ? $"{score:F1}" : "[grey]—[/]";

                    table.AddRow(
                        pdNbr,
                        Markup.Escape(title),
                        orgCode,
                        payPlan,
                        series,
                        grade,
                        $"[{statusColor}]{Markup.Escape(status)}[/]",
                        criteriaMet > 0 ? criteriaMet.ToString() : "[grey]0[/]",
                        string.IsNullOrEmpty(rating) ? "[grey]—[/]" : $"[{ratingColor}]{Markup.Escape(rating)}[/]",
                        scoreText);
                }

                AnsiConsole.WriteLine();
                AnsiConsole.Write(table);
                return true;
            }

            return false;
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
