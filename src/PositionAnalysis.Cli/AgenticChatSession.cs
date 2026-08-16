// src/PositionAnalysis.Cli/AgenticChatSession.cs
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
        "For expensive batch operations the tool returns a requiresConfirmation payload with " +
        "an estimated cost — tell the user the cost and ask them to confirm before re-calling " +
        "with { \"confirmed\": true }.";

    private readonly IChatClient _chatClient;
    private readonly McpToolRegistry _registry;
    private readonly string _modelName;
    private readonly List<ChatMessage> _history;

    public AgenticChatSession(IChatClient chatClient, McpToolRegistry registry, string modelName)
    {
        _chatClient = chatClient;
        _registry = registry;
        _modelName = modelName;
        _history = [new ChatMessage(ChatRole.System, SystemPrompt)];
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
            foreach (var call in calls)
            {
                AnsiConsole.MarkupLine($"[grey]  → calling [bold]{Markup.Escape(call.Name)}[/]…[/]");

                string resultJson;
                try
                {
                    resultJson = await _registry.CallToolAsync(
                        call.Name,
                        new Dictionary<string, object?>(call.Arguments ?? new Dictionary<string, object?>()),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    resultJson = $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
                    AnsiConsole.MarkupLine($"[red]  Tool error:[/] {Markup.Escape(ex.Message)}");
                }

                resultMessage.Contents.Add(
                    new FunctionResultContent(call.CallId, resultJson));
            }

            _history.Add(resultMessage);
            // Loop: feed results back to the LLM for its next response
        }
    }

    private static void LogTokenUsage(UsageDetails? usage)
    {
        if (usage is null) return;
        Log.Debug("Token usage — input: {Input}, output: {Output}",
            usage.InputTokenCount, usage.OutputTokenCount);
    }
}
