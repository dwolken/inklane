using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Exceptions;
using Block = System.Windows.Documents.Block;
using Inline = System.Windows.Documents.Inline;
using Paragraph = System.Windows.Documents.Paragraph;
using Table = System.Windows.Documents.Table;

namespace Inklane.Core;

/// <summary>Bounded local Markdown presentation. Source, math and code remain editable document data.</summary>
public static class StructuredText
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseMathematics().UseEmphasisExtras().Build();
    public static FlowDocument CreateDocument(TextItem item)
    {
        var color = (Color)ColorConverter.ConvertFromString(item.Color);
        var doc = new FlowDocument { FontFamily = new FontFamily(item.FontFamily), FontSize = item.FontSize,
            Foreground = new SolidColorBrush(color), Background = Brushes.Transparent, PagePadding = new Thickness(0),
            TextAlignment = Enum.Parse<TextAlignment>(item.Alignment), FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal, ColumnGap = 0, ColumnWidth = Math.Max(1, item.Width),
            IsHyphenationEnabled = false };
        if (item.ContentFormat != "Markdown") { doc.Blocks.Add(new Paragraph(new Run(item.Text))); return doc; }
        // Very large input remains available in the source editor, without unbounded layout work.
        if (item.Text.Length > 100000) { doc.Blocks.Add(new Paragraph(new Run(item.Text))); return doc; }
        foreach (var block in Markdown.Parse(item.Text, Pipeline)) foreach (var rendered in Blocks(block, item.FontSize)) doc.Blocks.Add(rendered);
        return doc;
    }

    private static IEnumerable<Block> Blocks(Markdig.Syntax.Block block, double size)
    {
        if (block is MathBlock math)
        {
            yield return new Paragraph(Formula(math.Lines.ToString(), size * 1.15)) { Margin = new Thickness(0, 6, 0, 10) }; yield break;
        }
        if (block is CodeBlock code)
        {
            var paragraph = new Paragraph { FontFamily = new FontFamily("Consolas"), FontSize = size * .88,
                Background = new SolidColorBrush(Color.FromRgb(241, 242, 245)), Foreground = Brushes.Black,
                Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 10) };
            var language = (code as FencedCodeBlock)?.Info ?? "";
            Highlight(paragraph.Inlines, code.Lines.ToString(), language); yield return paragraph; yield break;
        }
        if (block is Markdig.Extensions.Tables.Table table)
        {
            var result = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 10) };
            var rows = new TableRowGroup(); result.RowGroups.Add(rows);
            foreach (var row in table.OfType<Markdig.Extensions.Tables.TableRow>())
            {
                var line = new System.Windows.Documents.TableRow(); rows.Rows.Add(line);
                foreach (var cell in row.OfType<Markdig.Extensions.Tables.TableCell>())
                {
                    var output = new System.Windows.Documents.TableCell { BorderThickness = new Thickness(.5), BorderBrush = Brushes.Gray, Padding = new Thickness(5), FontWeight = row.IsHeader ? FontWeights.Bold : FontWeights.Normal };
                    foreach (var child in cell) foreach (var content in Blocks(child, size)) output.Blocks.Add(content);
                    line.Cells.Add(output);
                }
            }
            yield return result; yield break;
        }
        if (block is ListBlock list)
        {
            var output = new System.Windows.Documents.List { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(0, 4, 0, 8), Padding = new Thickness(size * 1.4, 0, 0, 0) };
            foreach (var entry in list.OfType<ListItemBlock>())
            {
                var item = new ListItem(); foreach (var child in entry) foreach (var content in Blocks(child, size)) item.Blocks.Add(content); output.ListItems.Add(item);
            }
            yield return output; yield break;
        }
        if (block is ContainerBlock container)
        {
            var section = new Section { Margin = new Thickness(8, 0, 0, 6) };
            foreach (var child in container) foreach (var output in Blocks(child, size)) section.Blocks.Add(output);
            yield return section; yield break;
        }
        if (block is LeafBlock leaf)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, size * .45) };
            if (leaf is HeadingBlock heading) { paragraph.FontSize = size * (heading.Level <= 1 ? 1.7 : heading.Level == 2 ? 1.4 : 1.15); paragraph.FontWeight = FontWeights.SemiBold; }
            if (leaf.Inline != null) AddInlines(paragraph.Inlines, leaf.Inline, size);
            else paragraph.Inlines.Add(new Run(leaf.Lines.ToString()));
            yield return paragraph;
        }
    }

    private static void AddInlines(InlineCollection result, ContainerInline container, double size)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal: result.Add(new Run(literal.Content.ToString())); break;
                case CodeInline code: result.Add(new Run(code.Content) { FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromRgb(239, 240, 243)) }); break;
                case MathInline math: result.Add(Formula(math.Content.ToString(), size)); break;
                case LineBreakInline line: if (line.IsHard) result.Add(new LineBreak()); else result.Add(new Run(" ")); break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterChar == '+' ? new Underline() : emphasis.DelimiterCount == 2 ? new Bold() : new Italic();
                    AddInlines(span.Inlines, emphasis, size); result.Add(span); break;
                case LinkInline link:
                    var hyperlink = new Hyperlink(); AddInlines(hyperlink.Inlines, link, size);
                    if (!link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto")
                    {
                        hyperlink.NavigateUri = uri;
                        hyperlink.RequestNavigate += (_, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } e.Handled = true; };
                    }
                    result.Add(hyperlink); break;
                case HtmlInline html: result.Add(new Run(html.Tag)); break;
                case ContainerInline children: AddInlines(result, children, size); break;
            }
        }
    }

    private static Inline Formula(string source, double size)
    {
        if (source.Length > 4000 || source.Count(c => c == '{') > 100) return new Run(source) { FontFamily = new FontFamily("Consolas") };
        try
        {
            var formula = WpfTeXFormulaParser.Instance.Parse(source);
            var geometry = formula.RenderToGeometry(WpfTeXEnvironment.Create(TexStyle.Display, size, "Segoe UI"), size);
            var bounds = geometry.Bounds;
            var drawing = new DrawingGroup(); using (var dc = drawing.Open()) { dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y)); dc.DrawGeometry(Brushes.Black, null, geometry); }
            var image = new Image { Source = new DrawingImage(drawing), Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height), ToolTip = source };
            return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center };
        }
        catch (Exception ex) when (ex is TexException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        { return new Run(source) { FontFamily = new FontFamily("Consolas") }; }
    }

    private static void Highlight(InlineCollection output, string code, string language)
    {
        if (code.Length > 20000 || language.Length == 0) { output.Add(new Run(code)); return; }
        var pattern = @"(?<comment>//[^\r\n]*|\#[^\r\n]*)|(?<str>""[^""\r\n]*""|'[^'\r\n]*')|(?<keyword>\b(?:class|public|private|static|void|var|let|const|function|return|if|else|for|while|def|import|from|true|false|null|None|new|using|async|await)\b)|(?<number>\b\d+(?:\.\d+)?\b)";
        int cursor = 0;
        foreach (Match token in Regex.Matches(code, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            if (token.Index > cursor) output.Add(new Run(code[cursor..token.Index]));
            output.Add(new Run(token.Value) { Foreground = token.Groups["comment"].Success ? Brushes.DarkGreen : token.Groups["str"].Success ? Brushes.Brown : token.Groups["number"].Success ? Brushes.DarkCyan : Brushes.DarkBlue });
            cursor = token.Index + token.Length;
        }
        if (cursor < code.Length) output.Add(new Run(code[cursor..]));
    }

    private static Drawing? VisibleDrawing(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
        {
            var copy = new DrawingGroup { Transform = group.Transform, Opacity = group.Opacity, ClipGeometry = group.ClipGeometry };
            foreach (var child in group.Children) if (VisibleDrawing(child) is { } kept) copy.Children.Add(kept);
            return copy.Children.Count == 0 ? null : copy;
        }
        if (drawing is GeometryDrawing geometry && geometry.Pen == null &&
            (geometry.Brush == null || geometry.Brush.Opacity == 0 || geometry.Brush is SolidColorBrush { Color.A: 0 })) return null;
        return drawing;
    }

    /// <summary>Vector snapshot of one bounded rich text layout, used by image and PDF exporters.</summary>
    public static DrawingGroup CreateDrawing(TextItem item)
    {
        var document = CreateDocument(item);
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = new Size(Math.Max(40, item.Width), 100000);
        var page = paginator.GetPage(0);
        var group = new DrawingGroup { Opacity = item.Opacity };
        void Visit(Visual visual, DrawingGroup parent)
        {
            var branch = new DrawingGroup();
            var offset = VisualTreeHelper.GetOffset(visual); var transforms = new TransformGroup();
            if (VisualTreeHelper.GetTransform(visual) is { } transform) transforms.Children.Add(transform);
            transforms.Children.Add(new TranslateTransform(offset.X, offset.Y)); branch.Transform = transforms;
            var drawing = VisualTreeHelper.GetDrawing(visual);
            if (drawing != null && VisibleDrawing(drawing) is { } visible) branch.Children.Add(visible);
            for (int n = 0; n < VisualTreeHelper.GetChildrenCount(visual); n++) if (VisualTreeHelper.GetChild(visual, n) is Visual child) Visit(child, branch);
            parent.Children.Add(branch);
        }
        if (page.Visual is Visual root) Visit(root, group);
        return group;
    }
}
