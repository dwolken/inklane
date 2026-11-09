using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Inklane.Core;

namespace Inklane.Controls;

public sealed partial class DocumentSurface
{
    internal const string ObjectFormat = "Inklane.EditableObjects.v1";
    internal Action? ValidateInsertedContent { get; set; }

    private static DocumentData ObjectDocument(PageData selection) => new() { Pages = [selection] };

    internal void CopyObjects()
    {
        if (!HasSelection) return;
        var selected = SelectionData();
        var bytes = Encoding.UTF8.GetBytes(DocumentStore.Serialize(ObjectDocument(selected)));
        var data = new DataObject();
        data.SetData(ObjectFormat, new MemoryStream(bytes, writable: false), false);
        if (selected.Items.Count > 0)
        {
            var text = string.Join(Environment.NewLine + Environment.NewLine,
                selected.Items.Select(i => string.IsNullOrWhiteSpace(i.Title) ? i.Text : i.Title + Environment.NewLine + i.Text));
            if (text.Length != 0) data.SetText(text);
        }
        if (selected.Ink.Count > 0)
        {
            var isf = new MemoryStream();
            new StrokeCollection(selected.Ink.Select(ToStroke)).Save(isf);
            isf.Position = 0;
            data.SetData(StrokeCollection.InkSerializedFormat, isf, false);
        }
        if (selected.Images.Count == 1 && selected.Items.Count == 0 && selected.Ink.Count == 0 && selected.Shapes.Count == 0)
        {
            var picture = selected.Images[0];
            data.SetData("PNG", new MemoryStream(picture.PngData, false), false);
            data.SetImage(_pictures[picture.Id].Source);
        }
        Clipboard.SetDataObject(data, true);
    }

    internal void CopyPagePicture() => CopyBitmap(RenderPage());

    internal static void CopyBitmap(BitmapSource source)
    {
        var data = new DataObject();
        data.SetImage(source);
        data.SetData("PNG", new MemoryStream(EncodePng(source), false), false);
        Clipboard.SetDataObject(data, true);
    }

    internal void DuplicateObjects()
    {
        if (!HasSelection) return;
        var selected = DocumentStore.Clone(ObjectDocument(SelectionData())).Pages[0];
        InsertObjects(selected, null, offset: true);
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var bytes = new MemoryStream();
        encoder.Save(bytes);
        return bytes.ToArray();
    }

    internal sealed record PasteContent(PageData Page, bool External);

    internal PasteContent ReadClipboardObjects(PageData destination)
    {
        static byte[] ReadStream(string format, int limit)
        {
            if (!Clipboard.TryGetData<MemoryStream>(format, out var stream) || stream == null || stream.Length > limit)
                throw new InvalidDataException("Clipboard data is invalid or too large.");
            return stream.ToArray();
        }
        if (Clipboard.ContainsData(ObjectFormat))
        {
            var bytes = ReadStream(ObjectFormat, DocumentStore.MaxJsonBytes);
            var doc = DocumentStore.Deserialize(new UTF8Encoding(false, true).GetString(bytes));
            if (doc.Pages.Count != 1 || doc.Kind != DocumentKind.Notebook) throw new InvalidDataException();
            var page = doc.Pages[0];
            if (page.Ink.Count + page.Items.Count + page.Images.Count + page.Shapes.Count == 0) throw new InvalidDataException();
            return new(page, false);
        }
        byte[]? png = null;
        if (Clipboard.ContainsData("PNG")) png = ReadStream("PNG", EmbeddedPng.MaxBytes);
        else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } source)
        {
            if ((long)source.PixelWidth * source.PixelHeight > EmbeddedPng.MaxPixels) throw new InvalidDataException();
            // Legacy CF_BITMAP has no alpha contract. Apps that need transparency publish PNG.
            var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            png = EncodePng(opaque);
        }
        var result = new PageData { MarginMm = 0 };
        if (png != null)
        {
            var source = EmbeddedPng.Decode(png);
            var scale = Math.Min(1, Math.Min(destination.Width * .85 / source.PixelWidth, destination.Height * .85 / source.PixelHeight));
            result.Images.Add(new ImageItem
            {
                PngData = png, Width = Math.Max(1, source.PixelWidth * scale), Height = Math.Max(1, source.PixelHeight * scale)
            });
        }
        else if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (text.Length is 0 or > 1_000_000) throw new InvalidDataException();
            var width = Math.Min(320, destination.Width);
            var size = Math.Clamp(TextSize, 8, 96);
            var layout = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), size, Brushes.Black, 1) { MaxTextWidth = width };
            result.Items.Add(new TextItem { Text = text, Width = width, Height = Math.Max(32, Math.Ceiling(layout.Height + size * .4)),
                FontSize = size, Color = InkColor });
        }
        else if (Clipboard.ContainsData(StrokeCollection.InkSerializedFormat))
        {
            var bytes = ReadStream(StrokeCollection.InkSerializedFormat, DocumentStore.MaxJsonBytes);
            using var input = new MemoryStream(bytes, false);
            result.Ink = new StrokeCollection(input).Select(FromStroke).ToList();
            if (result.Ink.Count == 0) throw new InvalidDataException();
        }
        else throw new NotSupportedException();
        DocumentStore.Validate(ObjectDocument(result));
        return new(result, true);
    }

    internal void PasteObjects(PasteContent content, Point? position)
    {
        if (_page == null) return;
        if (content.External && position == null)
        {
            var topLeft = _scroll.TranslatePoint(new Point(24, 24), _pageHost);
            position = new Point(Math.Max(24, topLeft.X), Math.Max(24, topLeft.Y));
        }
        InsertObjects(content.Page, position, offset: !content.External);
    }

    private void InsertObjects(PageData content, Point? position, bool offset)
    {
        if (_page == null) return;
        var bounds = Bounds(content);
        if (bounds.IsEmpty) throw new InvalidDataException();
        if (position == null && offset && _scroll.ViewportWidth > 0)
        {
            var visible = new Rect(_scroll.TranslatePoint(default, _pageHost),
                new Size(_scroll.ViewportWidth / _zoom, _scroll.ViewportHeight / _zoom));
            if (!visible.IntersectsWith(bounds))
                position = new Point(Math.Max(16, visible.Left + 24), Math.Max(16, visible.Top + 24));
        }
        PositionPastedObjects(content, _page, bounds, position ?? bounds.TopLeft + new Vector(16,16));
        var strokes = new StrokeCollection(content.Ink.Select(ToLiveStroke));
        // Check the combined destination, including its other pages, before modifying the visual tree.
        _page.Items.AddRange(content.Items);
        _page.Ink.AddRange(content.Ink);
        _page.Images.AddRange(content.Images);
        _page.Shapes.AddRange(content.Shapes);
        try
        {
            if (ValidateInsertedContent != null) ValidateInsertedContent();
            else DocumentStore.Serialize(ObjectDocument(_page));
        }
        catch
        {
            _page.Items.RemoveRange(_page.Items.Count - content.Items.Count, content.Items.Count);
            _page.Ink.RemoveRange(_page.Ink.Count - content.Ink.Count, content.Ink.Count);
            _page.Images.RemoveRange(_page.Images.Count - content.Images.Count, content.Images.Count);
            _page.Shapes.RemoveRange(_page.Shapes.Count - content.Shapes.Count, content.Shapes.Count);
            throw;
        }
        EnterSelectionTool();
        foreach (var item in content.Images) AddPicture(item);
        foreach (var item in content.Items) AddItemVisual(item);
        foreach (var item in content.Shapes) AddShapeVisual(item);
        _ink.Strokes.Add(strokes);
        NumberNotes();
        SelectObjects(content.Items.Select(i => i.Id).Concat(content.Images.Select(i => i.Id)).Concat(content.Shapes.Select(i => i.Id)), strokes);
        NotifyChanged();
    }

    internal static void PositionPastedObjects(PageData content, PageData destination, Point topLeft) =>
        PositionPastedObjects(content, destination, Bounds(content), topLeft);

    private static void PositionPastedObjects(PageData content, PageData destination, Rect bounds, Point topLeft)
    {
        if (bounds.IsEmpty) throw new InvalidDataException();
        var delta = topLeft - bounds.TopLeft;
        var dx = Constrain(delta.X, bounds.Left, bounds.Right, destination.Width);
        var dy = Constrain(delta.Y, bounds.Top, bounds.Bottom, destination.Height);
        PageObjects.RegenerateIds(content);
        foreach (var shape in content.Shapes) { shape.X += dx; shape.Y += dy; }
        foreach (var ink in content.Ink)
        {
            foreach (var point in ink.Points) { point.X += dx; point.Y += dy; }
        }
        foreach (var item in content.Items) { item.X += dx; item.Y += dy; }
        foreach (var item in content.Images) { item.X += dx; item.Y += dy; }
    }
}
