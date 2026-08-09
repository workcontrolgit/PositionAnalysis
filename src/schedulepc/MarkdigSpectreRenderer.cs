using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using System.Text;

namespace SchedulePC;

/// <summary>
/// Renders a markdown string to the console using Spectre.Console widgets.
/// Uses Markdig to parse the AST so all standard markdown elements are handled
/// consistently: headings, paragraphs, bullet/ordered lists, code blocks, tables, links.
/// </summary>
public static class MarkdigSpectreRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static void Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;
        var doc = Markdown.Parse(markdown, Pipeline);
        RenderBlocks(doc);
    }

    // ── Block rendering ───────────────────────────────────────────────────────

    private static void RenderBlocks(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
            RenderBlock(block);
    }

    private static void RenderBlock(Block block)
    {
        try
        {
            switch (block)
            {
                case HeadingBlock h:        RenderHeading(h);        break;
                case ParagraphBlock p:      RenderParagraph(p);      break;
                case ListBlock list:        RenderList(list, 0);     break;
                case FencedCodeBlock code:  RenderCodeBlock(code);   break;
                case CodeBlock code:        RenderCodeBlock(code);   break;
                case Markdig.Extensions.Tables.Table table: RenderTable(table); break;
                case ThematicBreakBlock:
                    AnsiConsole.Write(new Rule().RuleStyle("grey"));
                    break;
                case QuoteBlock quote:
                    AnsiConsole.MarkupLine("[grey]│[/]");
                    RenderBlocks(quote);
                    break;
            }
        }
        catch
        {
            // Fall back to plain text for this block only
            if (block is LeafBlock leaf)
                Console.WriteLine(leaf.Lines.ToString());
        }
    }

    private static void RenderHeading(HeadingBlock h)
    {
        var text = ExtractPlainText(h.Inline);
        var escaped = Markup.Escape(text);
        if (h.Level == 1)
            AnsiConsole.Write(new Rule($"[bold cyan]{escaped}[/]").RuleStyle("cyan").LeftJustified());
        else if (h.Level == 2)
            AnsiConsole.MarkupLine($"\n[bold]{escaped}[/]");
        else
            AnsiConsole.MarkupLine($"[bold dim]{escaped}[/]");
    }

    private static void RenderParagraph(ParagraphBlock p)
    {
        var markup = InlinesToMarkup(p.Inline);
        try { AnsiConsole.MarkupLine(markup); }
        catch (InvalidOperationException) { AnsiConsole.WriteLine(ExtractPlainText(p.Inline)); }
        AnsiConsole.WriteLine();
    }

    private static void RenderList(ListBlock list, int depth)
    {
        var indent = new string(' ', depth * 2 + 2);
        int orderedIndex = 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var prefix = list.IsOrdered ? $"{orderedIndex++}." : "•";

            var firstPara = item.OfType<ParagraphBlock>().FirstOrDefault();
            if (firstPara is not null)
            {
                var markup = InlinesToMarkup(firstPara.Inline);
                try { AnsiConsole.MarkupLine($"{indent}[grey]{prefix}[/] {markup}"); }
                catch (InvalidOperationException) { AnsiConsole.WriteLine($"{indent}{prefix} {ExtractPlainText(firstPara.Inline)}"); }
            }

            foreach (var nested in item.OfType<ListBlock>())
                RenderList(nested, depth + 1);
        }

        if (depth == 0) AnsiConsole.WriteLine();
    }

    private static void RenderCodeBlock(LeafBlock code)
    {
        var text = code.Lines.ToString().TrimEnd();
        AnsiConsole.Write(new Panel(Markup.Escape(text))
            .BorderColor(Color.Grey)
            .Padding(1, 0));
        AnsiConsole.WriteLine();
    }

    private static void RenderTable(Markdig.Extensions.Tables.Table table)
    {
        var spectreTable = new Spectre.Console.Table().BorderColor(Color.Teal).Expand();
        bool headerDone = false;

        foreach (var row in table.OfType<Markdig.Extensions.Tables.TableRow>())
        {
            // Use plain text for cells to avoid Spectre markup errors from model output
            var cells = row.OfType<Markdig.Extensions.Tables.TableCell>()
                .Select(c =>
                {
                    var para = c.OfType<ParagraphBlock>().FirstOrDefault();
                    return para is not null ? Markup.Escape(ExtractPlainText(para.Inline)) : string.Empty;
                })
                .ToArray();

            if (!headerDone)
            {
                foreach (var cell in cells)
                    spectreTable.AddColumn(new TableColumn($"[bold cyan]{cell}[/]"));
                headerDone = true;
            }
            else if (!row.IsHeader)
            {
                var padded = cells.Length < spectreTable.Columns.Count
                    ? cells.Concat(Enumerable.Repeat(string.Empty, spectreTable.Columns.Count - cells.Length)).ToArray()
                    : cells.Take(spectreTable.Columns.Count).ToArray();
                spectreTable.AddRow(padded);
            }
        }

        if (spectreTable.Columns.Count > 0)
            AnsiConsole.Write(spectreTable);
        AnsiConsole.WriteLine();
    }

    // ── Inline rendering ──────────────────────────────────────────────────────

    private static string InlinesToMarkup(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var node in inline)
            AppendInline(sb, node);
        return sb.ToString();
    }

    private static void AppendInline(StringBuilder sb, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sb.Append(Markup.Escape(lit.Content.ToString()));
                break;

            case EmphasisInline em:
                var tag = em.DelimiterCount == 2 ? "bold" : "italic";
                sb.Append($"[{tag}]");
                foreach (var child in em) AppendInline(sb, child);
                sb.Append($"[/{tag}]");
                break;

            case CodeInline code:
                sb.Append($"[grey on black] {Markup.Escape(code.Content)} [/]");
                break;

            case LineBreakInline:
                sb.Append('\n');
                break;

            case LinkInline link:
                // Render link text only — URLs are not useful in a terminal
                foreach (var child in link) AppendInline(sb, child);
                break;

            case Markdig.Syntax.Inlines.HtmlInline html:
                // Strip HTML tags — show nothing (e.g. <br>, <em>)
                break;

            case Markdig.Syntax.Inlines.HtmlEntityInline entity:
                sb.Append(Markup.Escape(entity.Transcoded.ToString()));
                break;

            default:
                // For any remaining inline types, just iterate children
                if (inline is ContainerInline container)
                    foreach (var child in container)
                        AppendInline(sb, child);
                break;
        }
    }

    // ── Plain text extraction ──────────────────────────────────────────────────

    public static string ExtractPlainText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var node in inline)
            ExtractInlineText(sb, node);
        return sb.ToString();
    }

    private static void ExtractInlineText(StringBuilder sb, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sb.Append(lit.Content);
                break;

            case CodeInline code:
                sb.Append(code.Content);
                break;

            case LineBreakInline:
                sb.Append(' ');
                break;

            default:
                if (inline is ContainerInline container)
                    foreach (var child in container)
                        ExtractInlineText(sb, child);
                break;
        }
    }
}
