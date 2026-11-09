using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;

namespace Inklane.Core;

public enum PdfReadProblem { Encrypted, NotebookChanged, NotebookFormat }

public sealed class PdfReadException(PdfReadProblem problem, string message) : NotSupportedException(message)
{
    public PdfReadProblem Problem { get; } = problem;
}

/// <summary>Versioned, bounded persistence. Writes are committed by a same-directory atomic rename.</summary>
public static class DocumentStore
{
    public const int MaxJsonBytes = 32 * 1024 * 1024;
    public const int MaxPdfBytes = 128 * 1024 * 1024;
    public const int MaxTitleLength = 200;
    private const string Format = "Inklane-1";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions(history: false);
    private static readonly JsonSerializerOptions HistoryOptions = CreateOptions(history: true);

    private static JsonSerializerOptions CreateOptions(bool history)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            if (history && type.Type == typeof(TextItem))
                type.Properties.Single(p => p.Name == "isExpanded").ShouldSerialize = (item, _) => !((TextItem)item).IsNote;
        });
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 32,
            TypeInfoResolver = resolver, Converters = { new JsonStringEnumConverter() }
        };
    }

    public static string Serialize(DocumentData data) => Serialize(data, JsonOptions);

    // Reading a note changes its presentation. Explicit saves still retain that presentation.
    public static string SerializeForHistory(DocumentData data) => Serialize(data, HistoryOptions);

    private static string Serialize(DocumentData data, JsonSerializerOptions options)
    {
        Validate(data);
        var result = JsonSerializer.Serialize(data, options);
        if (Encoding.UTF8.GetByteCount(result) > MaxJsonBytes)
            throw new InvalidDataException("The annotation data exceeds the 32 MiB limit.");
        return result;
    }

    public static DocumentData Deserialize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new InvalidDataException("The annotation data exceeds the 32 MiB limit.");
        try
        {
            var data = JsonSerializer.Deserialize<DocumentData>(json, JsonOptions)
                ?? throw new InvalidDataException("The annotation data is empty.");
            Validate(data, decodeImages: true);
            return data;
        }
        catch (JsonException ex) { throw new InvalidDataException("The annotation data is invalid.", ex); }
    }

    public static DocumentData Clone(DocumentData data) => Deserialize(Serialize(data));
    public static string GetFileHash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    public static void SaveJson(DocumentData data, string path)
    {
        if (data.Kind == DocumentKind.Pdf && !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PDF markup must be saved to a JSON annotation file.");
        var stored = data;
        if (data.Kind == DocumentKind.Image && data.SourceImagePath is { } source && Path.IsPathFullyQualified(source) &&
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(source)),Path.GetDirectoryName(Path.GetFullPath(path)),StringComparison.OrdinalIgnoreCase))
            stored = data.WithSourceName(Path.GetFileName(source));
        var bytes = Encoding.UTF8.GetBytes(Serialize(stored));
        ProtectSourceImage(data, path);
        AtomicWrite(path, temporary => File.WriteAllBytes(temporary, bytes));
    }

    public static DocumentData LoadJson(string path)
    {
        CheckSize(path, MaxJsonBytes);
        try { return Deserialize(File.ReadAllText(path, StrictUtf8)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("The annotation file contains invalid text encoding.", ex); }
    }

    // The sidecar hash used for conflict detection must describe the bytes just parsed.
    public static DocumentData LoadJson(string path, out string hash)
    {
        using var file = File.OpenRead(path);
        var model = LoadJson(path);
        hash = Convert.ToHexString(SHA256.HashData(file));
        return model;
    }

    public static void SavePdf(DocumentData data, string path)
    {
        if (data.Kind == DocumentKind.Pdf)
            throw new NotSupportedException("An external PDF and its editable annotation file must be kept together.");
        var json = Encoding.UTF8.GetBytes(Serialize(data));
        ProtectSourceImage(data, path);
        using var pdf = new PdfDocument();
        pdf.Info.Title = string.IsNullOrWhiteSpace(data.Title) ? Path.GetFileNameWithoutExtension(path) : data.Title;
        pdf.Info.Creator = "Inklane";
        pdf.Info.Subject = "Editable Inklane notebook";
        pdf.Version = 17;
        pdf.Internals.Catalog.Elements.SetString("/InklaneFormat", Format);
        pdf.AddEmbeddedFile("inklane.json", new MemoryStream(json, writable: false));
        var metadata = new PdfDictionary(pdf);
        metadata.CreateStream(json);
        pdf.Internals.AddObject(metadata);
        pdf.Internals.Catalog.Elements["/InklaneData"] = metadata.Reference!;

        XImage? sourceImage = null;
        try
        {
            if (data.Kind == DocumentKind.Image)
            {
                if (string.IsNullOrWhiteSpace(data.SourceImagePath))
                    throw new InvalidDataException("The original image path is missing.");
                if (data.SourceImageSha256 is not null &&
                    !StringComparer.OrdinalIgnoreCase.Equals(GetFileHash(data.SourceImagePath), data.SourceImageSha256))
                    throw new InvalidDataException("The original image has changed since the annotations were created.");
                sourceImage = XImage.FromFile(data.SourceImagePath);
            }
            foreach (var model in data.Pages)
            {
                var page = pdf.AddPage();
                page.Width = XUnit.FromPoint(Units.DipToPoint(model.Width));
                page.Height = XUnit.FromPoint(Units.DipToPoint(model.Height));
                using (var graphics = XGraphics.FromPdfPage(page))
                {
                    graphics.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
                    if (sourceImage is not null)
                        graphics.DrawImage(sourceImage, 0, 0, page.Width.Point, page.Height.Point);
                    else DrawPaper(graphics, model);
                    foreach (var item in model.Images)
                    {
                        using var stream = new MemoryStream(item.PngData, 0, item.PngData.Length, writable: false, publiclyVisible: true);
                        using var image = XImage.FromStream(stream);
                        graphics.DrawImage(image, Units.DipToPoint(item.X), Units.DipToPoint(item.Y),
                            Units.DipToPoint(item.Width), Units.DipToPoint(item.Height));
                    }
                }
                foreach (var stroke in model.Ink) AddInk(pdf, page, stroke);
                foreach (var item in model.Items) AddText(pdf, page, item);
            }
            AtomicWrite(path, temporary =>
            {
                pdf.Save(temporary);
                // PDFsharp finalizes content streams during Save; hash the serialized representation.
                using var finalized = PdfReader.Open(temporary, PdfDocumentOpenMode.Modify);
                finalized.Internals.Catalog.Elements.SetInteger("/InklaneContentHashVersion", 2);
                finalized.Internals.Catalog.Elements.SetString("/InklaneContentHash", ContentHash(finalized));
                finalized.Save(temporary);
                CheckSize(temporary, MaxPdfBytes);
            });
        }
        finally { sourceImage?.Dispose(); }
    }

    public static DocumentData LoadPdf(string path)
    {
        using var input = File.OpenRead(path);
        return ReadNotebook(input) ?? throw new NotSupportedException("This PDF has no editable Inklane notebook model.");
    }

    // Null identifies a foreign PDF only after checking the catalog and encryption.
    // Failures in a claimed Inklane notebook must never fall back to a different editing mode.
    public static DocumentData? ReadNotebook(Stream input)
    {
        if (input.Length > MaxPdfBytes) throw new InvalidDataException("The PDF exceeds 128 MiB.");
        try { return LoadPdfCore(input); }
        catch (Exception ex) when (ex is PdfSharp.PdfSharpException or FormatException or
            ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException || ex.GetType() == typeof(Exception))
        {
            throw new InvalidDataException("The PDF is damaged, encrypted, or uses unsupported PDF features.", ex);
        }
        catch (NotImplementedException ex)
        {
            throw new NotSupportedException("This PDF uses a feature that Inklane cannot read.", ex);
        }
    }

    private static DocumentData? LoadPdfCore(Stream input)
    {
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Import, _ => throw EncryptedPdf());
        // PDFsharp decrypts imported objects and then reports IsEncrypted=false. The parsed
        // security-handler object still identifies an encrypted source, including an empty user password.
        if (pdf.SecuritySettings.IsEncrypted || pdf.Internals.GetAllObjects().Any(o => o is PdfSharp.Pdf.Security.PdfStandardSecurityHandler))
            throw EncryptedPdf();
        var catalog = pdf.Internals.Catalog;
        if (catalog.Elements.GetString("/InklaneFormat") != Format)
        {
            if (catalog.Elements.Keys.Any(k => k.StartsWith("/Inklane", StringComparison.Ordinal)) || pdf.Info.Creator == "Inklane")
                throw new PdfReadException(PdfReadProblem.NotebookFormat,"The Inklane notebook format cannot be verified.");
            return null;
        }
        var metadata = catalog.Elements.GetDictionary("/InklaneData");
        if (metadata?.Stream is null)
            throw new InvalidDataException("The editable Inklane data is missing from this PDF.");
        // Our stream is intentionally uncompressed. Reject compressed replacements before decompression.
        if (metadata.Elements.ContainsKey("/Filter"))
            throw new PdfReadException(PdfReadProblem.NotebookChanged,"The editable data has been transformed by another PDF application.");
        var bytes = metadata.Stream.Value;
        if (bytes.Length > MaxJsonBytes) throw new InvalidDataException("The editable data exceeds the size limit.");
        DocumentData data;
        try { data = Deserialize(StrictUtf8.GetString(bytes)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("The embedded annotation text is not valid UTF-8.", ex); }
        if (data.Kind == DocumentKind.Pdf) throw new InvalidDataException("A notebook cannot contain an external PDF sidecar.");
        if (!string.IsNullOrWhiteSpace(data.Title) && pdf.Info.Title != data.Title)
            throw new PdfReadException(PdfReadProblem.NotebookChanged,"The PDF title was changed outside Inklane.");
        if (data.Pages.Count != pdf.PageCount)
            throw new InvalidDataException("The PDF pages no longer match the editable Inklane data.");
        for (var i = 0; i < data.Pages.Count; i++)
        {
            if (Math.Abs(pdf.Pages[i].Width.Point - Units.DipToPoint(data.Pages[i].Width)) > .02 ||
                Math.Abs(pdf.Pages[i].Height.Point - Units.DipToPoint(data.Pages[i].Height)) > .02)
                throw new InvalidDataException("The PDF page dimensions no longer match the editable data.");
        }
        if (catalog.Elements.GetInteger("/InklaneContentHashVersion") != 2)
            throw new PdfReadException(PdfReadProblem.NotebookFormat,"This PDF uses a development format that cannot be safely verified.");
        if (catalog.Elements.GetString("/InklaneContentHash") != ContentHash(pdf))
            throw new PdfReadException(PdfReadProblem.NotebookChanged,"This PDF was modified outside Inklane. Reopening it for editing could discard those changes.");
        return data;
    }

    private static PdfReadException EncryptedPdf() =>
        new(PdfReadProblem.Encrypted,"Encrypted PDFs cannot be edited by Inklane without losing their security settings.");

    private static string ContentHash(PdfDocument pdf)
        => PdfContentFingerprint.Compute(pdf);

    private static void DrawPaper(XGraphics graphics, PageData page)
    {
        if (page.Pattern == PaperPattern.Blank) return;
        var margin = Units.MmToPoint(page.MarginMm);
        var spacing = Units.MmToPoint(page.SpacingMm);
        var width = Units.DipToPoint(page.Width);
        var height = Units.DipToPoint(page.Height);
        var pen = new XPen(XColor.FromArgb(219, 226, 232), .45);
        for (var y = margin; y <= height - margin + .001; y += spacing)
            graphics.DrawLine(pen, margin, y, width - margin, y);
        if (page.Pattern == PaperPattern.Squared)
            for (var x = margin; x <= width - margin + .001; x += spacing)
                graphics.DrawLine(pen, x, margin, x, height - margin);
    }

    private static void AddInk(PdfDocument pdf, PdfPage page, InkData ink)
    {
        if (ink.Points.Count == 0) return;
        var padding = ink.Width * 2 + 1;
        var left = Units.DipToPoint(ink.Points.Min(p => p.X) - padding);
        var top = Units.DipToPoint(ink.Points.Min(p => p.Y) - padding);
        var width = Units.DipToPoint(ink.Points.Max(p => p.X) - ink.Points.Min(p => p.X) + padding * 2);
        var height = Units.DipToPoint(ink.Points.Max(p => p.Y) - ink.Points.Min(p => p.Y) + padding * 2);
        var annotation = new StandardAnnotation(pdf, "/Ink")
        {
            Rectangle = new PdfRectangle(new XRect(left, page.Height.Point - top - height, width, height))
        };
        annotation.Elements.SetString("/NM", ink.Id);
        annotation.Elements.SetInteger("/F", 4); // Print
        annotation.Elements["/C"] = ColorArray(pdf, ink.Color);
        var border = new PdfDictionary(pdf);
        border.Elements.SetReal("/W", Units.DipToPoint(ink.Width));
        annotation.Elements["/BS"] = border;
        var path = new PdfArray(pdf);
        foreach (var point in ink.Points)
        {
            path.Elements.Add(new PdfReal(Units.DipToPoint(point.X)));
            path.Elements.Add(new PdfReal(page.Height.Point - Units.DipToPoint(point.Y)));
        }
        var paths = new PdfArray(pdf);
        paths.Elements.Add(path);
        annotation.Elements["/InkList"] = paths;
        SetAppearance(pdf, annotation, width, height, graphics =>
        {
            // Preserve the actual WPF pen-pressure outline, rather than approximating its width curve.
            var points = new System.Windows.Input.StylusPointCollection(ink.Points.Select(p =>
                new System.Windows.Input.StylusPoint(p.X, p.Y, p.Pressure)));
            var stroke = new System.Windows.Ink.Stroke(points,
                new System.Windows.Ink.DrawingAttributes { Width = ink.Width, Height = ink.Width, FitToCurve = false });
            var geometry = stroke.GetGeometry().GetFlattenedPathGeometry();
            var outline = new XGraphicsPath { FillMode = XFillMode.Winding };
            foreach (var figure in geometry.Figures)
            {
                var polygon = new List<XPoint> { new(Units.DipToPoint(figure.StartPoint.X) - left,
                    Units.DipToPoint(figure.StartPoint.Y) - top) };
                foreach (var segment in figure.Segments)
                    if (segment is System.Windows.Media.PolyLineSegment line)
                        polygon.AddRange(line.Points.Select(p => new XPoint(Units.DipToPoint(p.X) - left, Units.DipToPoint(p.Y) - top)));
                    else if (segment is System.Windows.Media.LineSegment single)
                        polygon.Add(new XPoint(Units.DipToPoint(single.Point.X) - left, Units.DipToPoint(single.Point.Y) - top));
                if (polygon.Count > 2) outline.AddPolygon(polygon.ToArray());
            }
            graphics.DrawPath(new XSolidBrush(ParseColor(ink.Color)), outline);
        });
        page.Annotations.Add(annotation);
    }

    private static void AddText(PdfDocument pdf, PdfPage page, TextItem item)
    {
        var left = Units.DipToPoint(item.X);
        var top = Units.DipToPoint(item.Y);
        var width = Units.DipToPoint(item.IsNote ? 26 : item.Width);
        var height = Units.DipToPoint(item.IsNote ? 26 : item.Height);
        var annotation = new StandardAnnotation(pdf, item.IsNote ? "/Text" : "/FreeText")
        {
            Rectangle = new PdfRectangle(new XRect(left, page.Height.Point - top - height, width, height)),
            Contents = item.Text,
            Title = "Inklane"
        };
        annotation.Elements.SetString("/NM", item.Id);
        annotation.Elements.SetInteger("/F", 4);
        annotation.Elements["/C"] = ColorArray(pdf, item.IsNote ? "#E7AA32" : item.Color);
        if (item.IsNote)
        {
            if (item.Title.Length > 0) annotation.Elements.SetString("/Subj", item.Title);
            annotation.Elements.SetName("/Name", "/Note");
            annotation.Elements.SetBoolean("/Open", false);
        }
        else
        {
            annotation.Elements.SetString("/DA", $"/Helv {Number(Units.DipToPoint(item.FontSize))} Tf 0 g");
            annotation.Elements.SetInteger("/Q", 0);
        }
        SetAppearance(pdf, annotation, width, height, graphics =>
        {
            if (item.IsNote)
            {
                graphics.DrawRoundedRectangle(new XPen(XColor.FromArgb(168, 112, 18), .7),
                    new XSolidBrush(XColor.FromArgb(255, 221, 134)), 1, 1, width - 2, height - 2, 4, 4);
                var pen = new XPen(XColor.FromArgb(114, 75, 10), .9);
                for (var y = 6d; y < height - 4; y += 3.5) graphics.DrawLine(pen, 5, y, width - 5, y);
            }
            else
            {
                var font = new XFont("Segoe UI", Units.DipToPoint(item.FontSize), XFontStyleEx.Regular);
                var formatter = new XTextFormatter(graphics);
                formatter.DrawString(item.Text, font, new XSolidBrush(ParseColor(item.Color)),
                    new XRect(0, 0, width, height), XStringFormats.TopLeft);
            }
        });
        page.Annotations.Add(annotation);
    }

    // PDFsharp's public graphics API writes a complete vector appearance with embedded font resources.
    // The temporary page is removed after its content/resources have become the annotation's Form XObject.
    private static void SetAppearance(PdfDocument pdf, PdfAnnotation annotation, double width, double height, Action<XGraphics> draw)
    {
        var appearancePage = pdf.AddPage();
        appearancePage.Width = XUnit.FromPoint(width);
        appearancePage.Height = XUnit.FromPoint(height);
        using (var graphics = XGraphics.FromPdfPage(appearancePage)) draw(graphics);
        var appearance = new PdfDictionary(pdf);
        appearance.Elements.SetName("/Type", "/XObject");
        appearance.Elements.SetName("/Subtype", "/Form");
        appearance.Elements["/BBox"] = new PdfArray(pdf, new PdfInteger(0), new PdfInteger(0), new PdfReal(width), new PdfReal(height));
        appearance.Elements["/Resources"] = appearancePage.Elements["/Resources"];
        using var content = new MemoryStream();
        for (var i = 0; i < appearancePage.Contents.Elements.Count; i++)
        {
            var stream = appearancePage.Contents.Elements.GetDictionary(i)!.Stream.UnfilteredValue;
            content.Write(stream);
            content.WriteByte((byte)'\n');
        }
        appearance.CreateStream(content.ToArray());
        pdf.Internals.AddObject(appearance);
        var ap = new PdfDictionary(pdf);
        ap.Elements["/N"] = appearance.Reference!;
        annotation.Elements["/AP"] = ap;
        pdf.Pages.Remove(appearancePage);
    }

    private sealed class StandardAnnotation : PdfAnnotation
    {
        public StandardAnnotation(PdfDocument document, string subtype) : base(document)
            => Elements.SetName("/Subtype", subtype);
    }

    private static PdfArray ColorArray(PdfDocument pdf, string hex)
    {
        var color = ParseColor(hex);
        return new PdfArray(pdf, new PdfReal(color.R / 255d), new PdfReal(color.G / 255d), new PdfReal(color.B / 255d));
    }
    private static XColor ParseColor(string hex)
    {
        var value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return XColor.FromArgb((int)((value >> 16) & 255), (int)((value >> 8) & 255), (int)(value & 255));
    }
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static void CheckPageCapacity(PageData page)
    {
        if (page.Ink is null || page.Items is null || page.Images is null || page.Images.Count > 1000 ||
            page.Ink.Count > 100000 || page.Items.Count > 10000)
            throw new InvalidDataException("The page contains too many annotations or missing collections.");
    }

    public static void Validate(DocumentData data, bool decodeImages = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Kind == DocumentKind.Pdf ? data.SchemaVersion != 3 : data.SchemaVersion is not (1 or 2))
            throw new NotSupportedException("This Inklane document version is not supported.");
        if (data.Kind == DocumentKind.Pdf)
        {
            var name = data.SourcePdfName;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !Path.GetExtension(name).Equals(".pdf",StringComparison.OrdinalIgnoreCase) ||
                data.SourcePdfSha256 is not { Length: 64 } identity || !identity.All(Uri.IsHexDigit) ||
                data.SourceImagePath != null || data.SourceImageSha256 != null)
                throw new InvalidDataException("The annotation file does not identify a valid source PDF.");
        }
        else if (data.SourcePdfName != null || data.SourcePdfSha256 != null)
            throw new InvalidDataException("Unexpected PDF source information.");
        CheckTitle(data.Title);
        if (!Enum.IsDefined(data.Kind)) throw new InvalidDataException("The document kind is invalid.");
        if (data.Pages is null || data.Pages.Count is < 1 or > 1000)
            throw new InvalidDataException("A document must have between 1 and 1,000 pages.");
        if (data.Kind == DocumentKind.Image && data.Pages.Count != 1)
            throw new InvalidDataException("Image annotations must have exactly one page.");
        if (data.SourceImagePath?.Length > 32767 || data.SourceImageSha256 is { } hash &&
            (hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
            throw new InvalidDataException("The original image identity is invalid.");
        long pointCount = 0;
        long pixels = 0;
        long imageBytes = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in data.Pages)
        {
            if (page is null) throw new InvalidDataException("A page is missing.");
            if (data.Kind == DocumentKind.Pdf && (page.Pattern != PaperPattern.Blank || page.MarginMm != 0))
                throw new InvalidDataException("PDF markup must use the source page geometry.");
            Range(page.Width, 1, 100000, "Page width");
            Range(page.Height, 1, 100000, "Page height");
            if (!Enum.IsDefined(page.Pattern)) throw new InvalidDataException("The paper pattern is invalid.");
            Range(page.SpacingMm, 1, 100, "Grid spacing");
            Range(page.MarginMm, 0, data.Kind == DocumentKind.Image ? 1000 : Units.DipToMm(Math.Min(page.Width, page.Height)) / 2, "Margin");
            CheckPageCapacity(page);
            foreach (var image in page.Images)
            {
                if (image is null) throw new InvalidDataException("An embedded image is missing.");
                Id(image.Id, ids);
                Range(image.X, -100000, 200000, "Image X");
                Range(image.Y, -100000, 200000, "Image Y");
                Range(image.Width, 1, 100000, "Image width");
                Range(image.Height, 1, 100000, "Image height");
                pixels += EmbeddedPng.CheckHeader(image.PngData);
                imageBytes += image.PngData.Length;
                if (pixels > 64_000_000 || imageBytes > MaxJsonBytes * 3L / 4)
                    throw new InvalidDataException("The embedded images exceed the document size limit.");
            }
            foreach (var ink in page.Ink)
            {
                if (ink is null || ink.Points is null) throw new InvalidDataException("An ink stroke is invalid.");
                Id(ink.Id, ids);
                Color(ink.Color);
                Range(ink.Width, .1, 1000, "Stroke width");
                pointCount += ink.Points.Count;
                if (pointCount > 2000000) throw new InvalidDataException("The document contains too many ink points.");
                foreach (var point in ink.Points)
                {
                    if (point is null) throw new InvalidDataException("An ink point is missing.");
                    Range(point.X, -100000, 200000, "Point X");
                    Range(point.Y, -100000, 200000, "Point Y");
                    Range(point.Pressure, 0, 1, "Pen pressure");
                }
            }
            foreach (var item in page.Items)
            {
                if (item is null || item.Text is null || item.Text.Length > 1000000)
                    throw new InvalidDataException("A text annotation is invalid or too long.");
                Id(item.Id, ids);
                CheckTitle(item.Title);
                Color(item.Color);
                Range(item.X, -100000, 200000, "Text X");
                Range(item.Y, -100000, 200000, "Text Y");
                Range(item.Width, 1, 100000, "Text width");
                Range(item.Height, 1, 100000, "Text height");
                Range(item.FontSize, 1, 1000, "Font size");
            }
        }
        // Establish aggregate bounds for every page before allocating any decoded images.
        if (decodeImages)
            foreach (var image in data.Pages.SelectMany(page => page.Images))
                EmbeddedPng.Decode(image.PngData);
    }
    private static void CheckTitle(string title)
    {
        if (title is null || title.Length > MaxTitleLength || title.Any(char.IsControl))
            throw new InvalidDataException("A title must be a single line of up to 200 characters.");
    }
    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"{name} is outside the supported range.");
    }
    private static void Id(string value, HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !ids.Add(value))
            throw new InvalidDataException("Annotation identifiers must be unique and nonempty.");
    }
    private static void Color(string value)
    {
        if (value is null || value.Length != 7 || value[0] != '#' || !value.AsSpan(1).ToArray().All(Uri.IsHexDigit))
            throw new InvalidDataException("An annotation color is invalid.");
    }
    private static void CheckSize(string path, long limit)
    {
        if (new FileInfo(path).Length > limit) throw new InvalidDataException("The file exceeds the supported size limit.");
    }
    private static void ProtectSourceImage(DocumentData data, string destination)
    {
        if (data.SourceImagePath is { Length: > 0 } source &&
            string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Annotation data cannot replace the original image.");
    }
    private static void AtomicWrite(string path, Action<string> write)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The destination folder does not exist.");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            throw new UnauthorizedAccessException("The destination file is read-only.");
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            write(temporary);
            using (var flush = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) flush.Flush(true);
            if (File.Exists(path)) ReplaceWithRetry(temporary, path);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ReplaceWithRetry(string temporary, string path)
    {
        // Windows indexers/scanners can briefly open a freshly written target without delete sharing.
        // Keep File.Replace's ACL preservation; permanent locks still fail with the original intact.
        for (var attempt = 0; ; attempt++)
        {
            try { File.Replace(temporary, path, null); return; }
            catch (IOException ex) when (attempt < 3 && (ex.HResult & 0xffff) is 32 or 33 or 1175)
            {
                Thread.Sleep(40 * (1 << attempt));
            }
        }
    }
}
