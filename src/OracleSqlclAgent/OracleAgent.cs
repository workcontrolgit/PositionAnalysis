using Microsoft.Agents.AI;
using Spectre.Console;

namespace OracleSqlclAgent;

public enum UiStyle { Structured, Minimal, Panels }

public sealed class OracleAgent(AIAgent agent, UiStyle style = UiStyle.Structured)
{
    private AgentSession? _session;

    public async Task RunAsync(CancellationToken ct = default)
    {
        RenderWelcome();

        while (!ct.IsCancellationRequested)
        {
            var input = RenderUserPrompt();
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            var text = await RunAgentAsync(input, ct);
            RenderResponse(text);
        }
    }

    public async Task<string> AskAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return await RunAgentAsync(input, ct);
    }

    // ── MAF agent invocation ─────────────────────────────────────────────────

    private async Task<string> RunAgentAsync(string input, CancellationToken ct)
    {
        _session ??= await agent.CreateSessionAsync(ct);
        var response = await Spin("Thinking\u2026", _ =>
            agent.RunAsync(input, _session, null, ct));

        return response.Text ?? string.Empty;
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
