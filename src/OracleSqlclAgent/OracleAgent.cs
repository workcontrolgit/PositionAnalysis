using Microsoft.Extensions.AI;
using Spectre.Console;

namespace OracleSqlclAgent;

public enum UiStyle { Structured, Minimal, Panels }

public sealed class OracleAgent(
    IChatClient chatClient,
    IList<AITool> tools,
    AgentSkillsProvider skills,
    UiStyle style = UiStyle.Structured,
    int? numCtx = null)
{
    private const string SystemPrompt = """
        You are an Oracle database assistant. The database connection is hr_local.
        Always call the connect tool before running any query.
        Format query results as markdown tables. Use **bold** for key values.
        Do not list your internal skill names. Do not introduce yourself with a menu.
        Wait for the user's question and answer it directly.
        """;

    private readonly List<ChatMessage> _history =
    [
        new(ChatRole.System, SystemPrompt)
    ];

    public async Task RunAsync(CancellationToken ct = default)
    {
        RenderWelcome();

        while (!ct.IsCancellationRequested)
        {
            var input = RenderUserPrompt();
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            _history.Add(new ChatMessage(ChatRole.User, input));

            var text = await RunToolLoopAsync(ct);
            RenderResponse(text);
        }
    }

    public async Task<string> AskAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        _history.Add(new ChatMessage(ChatRole.User, input));
        return await RunToolLoopAsync(ct);
    }

    // ── Manual tool call loop ────────────────────────────────────────────────

    private async Task<string> RunToolLoopAsync(CancellationToken ct)
    {
        var additional = new AdditionalPropertiesDictionary();
        if (numCtx.HasValue) additional["num_ctx"] = numCtx.Value;
        var options = new ChatOptions { Tools = tools, AdditionalProperties = additional };

        var response = await Spin("Thinking\u2026", _ =>
            chatClient.GetResponseAsync(_history, options, ct));

        while (true)
        {
            var toolCalls = response.Messages
                .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
                .ToList();

            if (toolCalls.Count == 0)
            {
                _history.AddMessages(response);
                return response.Text ?? string.Empty;
            }

            // Append assistant messages (with tool call requests) to history
            foreach (var msg in response.Messages)
                _history.Add(msg);

            // Execute each tool and append result to history
            foreach (var call in toolCalls)
            {
                var fn = tools.FirstOrDefault(t => t.Name == call.Name) as AIFunction;
                object? rawResult;

                if (fn is null)
                {
                    rawResult = $"Tool '{call.Name}' not found.";
                }
                else
                {
                    var fnArgs = call.Arguments is null ? null : new AIFunctionArguments(call.Arguments);
                    try { rawResult = await fn.InvokeAsync(fnArgs, ct); }
                    catch (Exception ex) { rawResult = $"Error: {ex.Message}"; }
                }

                _history.Add(new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(call.CallId ?? string.Empty, rawResult)]));
            }

            response = await Spin("Thinking\u2026", _ =>
                chatClient.GetResponseAsync(_history, options, ct));
        }
    }

    // ── Spinner ──────────────────────────────────────────────────────────────

    private static Task<T> Spin<T>(string status, Func<StatusContext, Task<T>> action) =>
        AnsiConsole.Status()
            .Spinner(Spectre.Console.Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"[blue]{status}[/]", action);

    // ── UI rendering ─────────────────────────────────────────────────────────

    private void RenderWelcome()
    {
        switch (style)
        {
            case UiStyle.Structured:
                AnsiConsole.Write(new Rule("[bold cyan]Oracle Assistant[/]").RuleStyle("cyan").LeftJustified());
                AnsiConsole.MarkupLine("[grey]Ask a database question. Type [bold]exit[/] to quit.[/]\n");
                break;
            case UiStyle.Minimal:
                AnsiConsole.MarkupLine("[teal]Oracle Assistant ready.[/] Ask a database question.");
                AnsiConsole.MarkupLine("[grey]Type [bold]exit[/] to quit.[/]\n");
                break;
            case UiStyle.Panels:
                AnsiConsole.Write(
                    new Panel("[bold]Oracle Assistant[/]\n[grey]Ask a database question.[/]")
                        .Header("[cyan]Ready[/]")
                        .BorderColor(Color.Cyan1)
                        .Padding(1, 0));
                AnsiConsole.MarkupLine("[grey]Type [bold]exit[/] to quit.[/]\n");
                break;
            default:
                throw new NotSupportedException($"Unknown UiStyle: {style}");
        }
    }

    private string RenderUserPrompt()
    {
        switch (style)
        {
            case UiStyle.Structured:
                AnsiConsole.Markup("[bold yellow]You \u203a[/] ");
                return Console.ReadLine() ?? string.Empty;
            case UiStyle.Minimal:
                AnsiConsole.Write(new Rule("[bold yellow]You[/]").RuleStyle("grey").LeftJustified());
                return Console.ReadLine() ?? string.Empty;
            case UiStyle.Panels:
                return AnsiConsole.Ask<string>("[bold yellow]You \u203a[/]");
            default:
                throw new NotSupportedException($"Unknown UiStyle: {style}");
        }
    }

    private void RenderResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            AnsiConsole.MarkupLine("[grey](No response)[/]");
            return;
        }

        switch (style)
        {
            case UiStyle.Structured:
                AnsiConsole.MarkupLine("\n[bold green]Assistant \u203a[/]");
                MarkdigSpectreRenderer.Render(text);
                AnsiConsole.Write(new Rule().RuleStyle("grey"));
                AnsiConsole.WriteLine();
                break;
            case UiStyle.Minimal:
                AnsiConsole.Write(new Rule("[bold green]Assistant[/]").RuleStyle("grey").LeftJustified());
                MarkdigSpectreRenderer.Render(text);
                AnsiConsole.Write(new Rule().RuleStyle("grey"));
                AnsiConsole.WriteLine();
                break;
            case UiStyle.Panels:
                AnsiConsole.Write(new Rule("[bold green]Assistant[/]").RuleStyle("aquamarine3").LeftJustified());
                MarkdigSpectreRenderer.Render(text);
                AnsiConsole.Write(new Rule().RuleStyle("aquamarine3"));
                AnsiConsole.WriteLine();
                break;
            default:
                throw new NotSupportedException($"Unknown UiStyle: {style}");
        }
    }
}
