using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Inklane.Core;

namespace Inklane;

/// <summary>Presentation exports always include the body of collapsed remarks.</summary>
internal static class ReviewPresentation
{
    internal static FlowDocument Notes(IEnumerable<(int Number, PageData Page)> pages, Size size)
    {
        var document = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 16,
            Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new Thickness(0),
            ColumnWidth = size.Width, ColumnGap = 0, PageWidth = size.Width, PageHeight = size.Height };
        foreach (var (number, page) in pages)
        {
            var notes = page.Items.Where(item => item.IsNote).ToArray();
            if (notes.Length == 0) continue;
            document.Blocks.Add(new Paragraph(new Run(string.Format(WorkflowText.T("ReviewPageNotes"), number)))
                { FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 20), KeepWithNext = true });
            for (int index = 0; index < notes.Length; index++)
            {
                var note = notes[index];
                document.Blocks.Add(new Paragraph(new Run($"{index + 1}. {note.Title}".TrimEnd()))
                    { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8), KeepWithNext = true });
                var body = StructuredText.CreateDocument(note);
                var section = new Section { FontFamily = body.FontFamily, FontSize = body.FontSize,
                    FontWeight = body.FontWeight, FontStyle = body.FontStyle, TextAlignment = body.TextAlignment,
                    // Notes are explanation content here, not translucent marks over the source.
                    Foreground = Brushes.Black, Margin = new Thickness(0, 0, 0, 18) };
                while (body.Blocks.FirstBlock is { } block) { body.Blocks.Remove(block); section.Blocks.Add(block); }
                if (note.Underline && note.ContentFormat == "Plain")
                    new TextRange(section.ContentStart, section.ContentEnd).ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Underline);
                document.Blocks.Add(section);
            }
        }
        return document;
    }

    internal static BitmapSource WithNotes(BitmapSource source, PageData page, int pageNumber)
    {
        if (!page.Items.Any(item => item.IsNote)) return source;
        const double gutter = 380, gap = 28, inset = 24, segmentHeight = 1000;
        var document = Notes([(pageNumber, page)], new Size(gutter, segmentHeight));
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();
        if (paginator.PageCount > 30) throw new InvalidDataException(WorkflowText.T("ImageNotesTooLong"));
        var notes = new List<DrawingGroup>();
        double noteHeight = 0;
        for (int index = 0; index < paginator.PageCount; index++)
        {
            using var part = paginator.GetPage(index);
            var drawing = Snapshot(part.Visual);
            notes.Add(drawing);
            noteHeight += index + 1 == paginator.PageCount ? Math.Min(segmentHeight, Math.Max(1, drawing.Bounds.Bottom)) : segmentHeight;
        }
        double pageWidth = source.PixelWidth * 96 / source.DpiX, pageHeight = source.PixelHeight * 96 / source.DpiY;
        double width = pageWidth + gap + gutter + inset * 2, height = Math.Max(pageHeight, noteHeight + inset * 2);
        int pixelWidth = checked((int)Math.Ceiling(width * source.DpiX / 96)), pixelHeight = checked((int)Math.Ceiling(height * source.DpiY / 96));
        if (pixelWidth > 32767 || pixelHeight > 32767 || (long)pixelWidth * pixelHeight > 48_000_000)
            throw new InvalidDataException(WorkflowText.T("ImageNotesTooLong"));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(0, 0, pageWidth, pageHeight));
            context.DrawLine(new Pen(Brushes.LightGray, 1), new Point(pageWidth + gap / 2, 0), new Point(pageWidth + gap / 2, height));
            for (int index = 0; index < notes.Count; index++)
            {
                context.PushTransform(new TranslateTransform(pageWidth + gap + inset, inset + index * segmentHeight));
                context.DrawDrawing(notes[index]); context.Pop();
            }
        }
        var result = new RenderTargetBitmap(pixelWidth, pixelHeight, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }

    private static DrawingGroup Snapshot(Visual visual)
    {
        var result = new DrawingGroup();
        var transform = new TransformGroup();
        if (VisualTreeHelper.GetTransform(visual) is { } own) transform.Children.Add(own);
        var offset = VisualTreeHelper.GetOffset(visual); transform.Children.Add(new TranslateTransform(offset.X, offset.Y));
        result.Transform = transform;
        if (VisualTreeHelper.GetDrawing(visual) is { } drawing) result.Children.Add(drawing);
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            if (VisualTreeHelper.GetChild(visual, index) is Visual child) result.Children.Add(Snapshot(child));
        return result;
    }

    internal static DocumentPaginator AppendNotes(DocumentPaginator pages, IEnumerable<(int Number, PageData Page)> model, Size sheet, Rect printable)
    {
        var selected = model.ToArray();
        if (!selected.Any(pair => pair.Page.Items.Any(item => item.IsNote))) return pages;
        var content = new Rect(printable.X + 24, printable.Y + 24, Math.Max(80, printable.Width - 48), Math.Max(80, printable.Height - 48));
        return new NotesPaginator(pages, Notes(selected, content.Size), sheet, content);
    }

    private sealed class NotesPaginator : DocumentPaginator
    {
        private readonly DocumentPaginator source, notes;
        private readonly Rect content;
        private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        public NotesPaginator(DocumentPaginator source, FlowDocument notes, Size sheet, Rect content)
        {
            this.source = source; this.notes = ((IDocumentPaginatorSource)notes).DocumentPaginator;
            this.content = content; PageSize = sheet; source.ComputePageCount(); this.notes.ComputePageCount();
        }
        public override bool IsPageCountValid => true;
        public override int PageCount => source.PageCount + notes.PageCount;
        public override Size PageSize { get; set; }
        public override IDocumentPaginatorSource? Source => null;
        public override DocumentPage GetPage(int pageNumber)
        {
            if (!dispatcher.CheckAccess()) return dispatcher.Invoke(() => GetPage(pageNumber));
            if (pageNumber < 0 || pageNumber >= PageCount) return DocumentPage.Missing;
            if (pageNumber < source.PageCount) return source.GetPage(pageNumber);
            var note = notes.GetPage(pageNumber - source.PageCount);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(PageSize));
                context.PushTransform(new TranslateTransform(content.X, content.Y));
                context.DrawDrawing(Snapshot(note.Visual)); context.Pop();
            }
            return new DocumentPage(visual, PageSize, new Rect(PageSize), content);
        }
    }
}
