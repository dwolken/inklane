using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Inklane.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Inklane.Tests;

internal static class Program
{
    private static int failures;
    private static int checks;
    private static string output = "";

    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.FirstOrDefault() ?? ".scratch/test-output");
        Directory.CreateDirectory(output);
        Run("JSON round trip retains multiline note, pressure and coordinates", JsonRoundTrip);
        Run("PDF round trip retains two editable pages and full Unicode text", PdfRoundTrip);
        Run("A4 and A5 media boxes match physical dimensions", PageGeometry);
        Run("PDF standard Ink, FreeText and Text annotations have appearances", StandardAnnotations);
        Run("Named notes and placed PNGs retain metadata, geometry and standard PDF content", TitledNotebook);
        Run("Legacy files remain readable and extended data uses a guarded schema", SchemaCompatibility);
        Run("Oversized, corrupt and duplicate embedded image data is rejected", InvalidImages);
        Run("Atomic overwrite replaces complete document", AtomicOverwrite);
        Run("Failed locked-file save preserves original bytes", FailedSave);
        Run("Read-only save fails without modifying original", ReadOnlySave);
        Run("Source image cannot be overwritten by annotations", ProtectImage);
        Run("Changed source image blocks PDF export", ChangedImage);
        Run("Malformed and future documents are rejected", InvalidDocuments);
        Run("Foreign PDFs are rejected without changes", ForeignPdf);
        Run("Page mutation outside Inklane is detected", ChangedPdf);
        Run("Invalid, truncated and encrypted PDFs produce safe errors", InvalidPdfs);
        Run("PDF resource, rotation and additional annotation edits are detected", ChangedPdfObjectGraph);
        Console.WriteLine($"{checks - failures}/{checks} checks passed. Artifacts: {output}");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        checks++;
        try { test(); Console.WriteLine($"PASS {name}"); }
        catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}\n{ex}"); }
    }

    private static DocumentData Sample() => new()
    {
        Kind = DocumentKind.Notebook,
        Pages =
        [
            new PageData
            {
                Pattern = PaperPattern.Squared, SpacingMm = 5, MarginMm = 15,
                Ink = [new InkData { Color = "#2175C5", Width = 3, Points =
                [new InkPoint { X = 90, Y = 100, Pressure = .2f }, new InkPoint { X = 150, Y = 130, Pressure = .8f },
                    new InkPoint { X = 210, Y = 100, Pressure = .5f }] }],
                Items =
                [
                    new TextItem { X = 80, Y = 160, Width = 420, Height = 110, Text = "Inklane — editable text\nGrüße aus Österreich: ä ö ü ß", FontSize = 18 },
                    new TextItem { X = 420.125, Y = 220.375, IsNote = true, IsExpanded = false,
                        Text = "A contextual note.\n" + string.Join("\n", Enumerable.Range(1, 40).Select(i => $"Line {i}: Grüße, long multiline text.")) }
                ]
            },
            new PageData { Width = Units.MmToDip(210), Height = Units.MmToDip(148), Pattern = PaperPattern.Ruled, SpacingMm = 10 }
        ]
    };

    private static void JsonRoundTrip()
    {
        var data = Sample();
        var path = Path.Combine(output, "roundtrip.inklane.json");
        DocumentStore.SaveJson(data, path);
        Equal(DocumentStore.Serialize(data), DocumentStore.Serialize(DocumentStore.LoadJson(path)));
        var clone = DocumentStore.Clone(data);
        clone.Pages[0].Items[0].Text = "different";
        Assert(data.Pages[0].Items[0].Text != "different", "Clone aliases original data.");
    }

    private static void PdfRoundTrip()
    {
        var data = Sample();
        var path = Path.Combine(output, "notebook.pdf");
        DocumentStore.SavePdf(data, path);
        Equal(DocumentStore.Serialize(data), DocumentStore.Serialize(DocumentStore.LoadPdf(path)));
    }

    private static void PageGeometry()
    {
        var data = Sample();
        var path = Path.Combine(output, "geometry.pdf");
        DocumentStore.SavePdf(data, path);
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Near(595.2755905511812, pdf.Pages[0].Width.Point, .001);
        Near(841.8897637795276, pdf.Pages[0].Height.Point, .001);
        Near(595.2755905511812, pdf.Pages[1].Width.Point, .001);
        Near(419.52755905511816, pdf.Pages[1].Height.Point, .001);
        // Inspect actual emitted page operators, using independent numerical constants (not Units).
        var first = Encoding.ASCII.GetString(pdf.Pages[0].Contents.Elements.GetDictionary(0)!.Stream.UnfilteredValue);
        File.WriteAllText(Path.Combine(output, "grid-page-1.txt"), first);
        var firstMoves = ReadMoveOperators(first);
        Near(42.5196850394, firstMoves[0].X, .001);
        Near(799.3700787402, firstMoves[0].Y, .001);
        Near(14.1732283465, firstMoves[0].Y - firstMoves[1].Y, .001);
        var second = Encoding.ASCII.GetString(pdf.Pages[1].Contents.Elements.GetDictionary(0)!.Stream.UnfilteredValue);
        var secondMoves = ReadMoveOperators(second);
        Near(28.3464566929, secondMoves[0].Y - secondMoves[1].Y, .001);
        File.WriteAllText(Path.Combine(output, "grid-page-1.txt"), first);
        File.WriteAllText(Path.Combine(output, "grid-page-2.txt"), second);
    }

    private static List<(double X, double Y)> ReadMoveOperators(string content)
        => Regex.Matches(content, @"(?m)^([-\d.]+) ([-\d.]+) m\r?$")
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToList();

    private static void StandardAnnotations()
    {
        using var pdf = PdfReader.Open(Path.Combine(output, "notebook.pdf"), PdfDocumentOpenMode.Import);
        var annotations = pdf.Pages[0].Elements.GetArray("/Annots")!;
        Assert(annotations.Elements.Count == 3, "Wrong annotation count.");
        var subtypes = new List<string>();
        foreach (var index in Enumerable.Range(0, annotations.Elements.Count))
        {
            var item = annotations.Elements.GetDictionary(index)!;
            subtypes.Add(item.Elements.GetName("/Subtype"));
            Assert(item.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N")?.Stream.Value.Length > 0,
                "Annotation appearance is missing.");
            Assert(item.Elements.GetInteger("/F") == 4, "Annotation print flag is missing.");
        }
        Assert(subtypes.SequenceEqual(new[] { "/Ink", "/FreeText", "/Text" }), "Missing standard annotation types.");
        Equal(Sample().Pages[0].Items[1].Text, annotations.Elements.GetDictionary(2)!.Elements.GetString("/Contents"));
        Assert(pdf.Internals.Catalog.Elements.GetDictionary("/Names")?.Elements.GetDictionary("/EmbeddedFiles") is not null,
            "Machine-readable attachment missing.");
    }

    private static void AtomicOverwrite()
    {
        var path = Path.Combine(output, "overwrite.json");
        var data = Sample();
        DocumentStore.SaveJson(data, path);
        data.Pages.RemoveAt(1);
        data.Pages[0].Items[0].Text = "Saved replacement";
        DocumentStore.SaveJson(data, path);
        Equal("Saved replacement", DocumentStore.LoadJson(path).Pages[0].Items[0].Text);
        Assert(Directory.GetFiles(output, ".overwrite.json.*.tmp").Length == 0, "Temporary files remain.");
    }

    private static void FailedSave()
    {
        var path = Path.Combine(output, "locked.json");
        DocumentStore.SaveJson(Sample(), path);
        var original = File.ReadAllBytes(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Throws<IOException>(() => DocumentStore.SaveJson(Sample(), path));
        Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Failed save changed existing bytes.");
        Assert(Directory.GetFiles(output, ".locked.json.*.tmp").Length == 0, "Failed save left temporary files.");
    }

    private static void ReadOnlySave()
    {
        var path = Path.Combine(output, "readonly.json");
        DocumentStore.SaveJson(Sample(), path);
        var hash = DocumentStore.GetFileHash(path);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try { Throws<UnauthorizedAccessException>(() => DocumentStore.SaveJson(Sample(), path)); }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        Equal(hash, DocumentStore.GetFileHash(path));
    }

    private static DocumentData ImageSample()
    {
        var path = Path.Combine(output, "sample.png");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.LightSteelBlue, null, new Rect(0, 0, 640, 480));
            dc.DrawEllipse(Brushes.Coral, null, new Point(320, 240), 100, 100);
        }
        var bitmap = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
        return new DocumentData { Kind = DocumentKind.Image, SourceImagePath = path, SourceImageSha256 = DocumentStore.GetFileHash(path),
            Pages = [new PageData { Width = 640, Height = 480 }] };
    }

    private static void ProtectImage()
    {
        var data = ImageSample();
        var hash = DocumentStore.GetFileHash(data.SourceImagePath!);
        Throws<IOException>(() => DocumentStore.SaveJson(data, data.SourceImagePath!));
        Throws<IOException>(() => DocumentStore.SavePdf(data, data.SourceImagePath!));
        Equal(hash, DocumentStore.GetFileHash(data.SourceImagePath!));
        DocumentStore.SaveJson(data, data.SourceImagePath + ".inklane.json");
        DocumentStore.SavePdf(data, Path.Combine(output, "image-export.pdf"));
        Equal(hash, DocumentStore.GetFileHash(data.SourceImagePath!));
    }

    private static void ChangedImage()
    {
        var data = ImageSample();
        File.AppendAllText(data.SourceImagePath!, "changed");
        Throws<InvalidDataException>(() => DocumentStore.SavePdf(data, Path.Combine(output, "changed-image.pdf")));
        ImageSample(); // Restore the generated fixture, never a user document.
    }

    private static void InvalidDocuments()
    {
        Throws<InvalidDataException>(() => DocumentStore.Deserialize("{}"));
        Throws<InvalidDataException>(() => DocumentStore.Deserialize("{oops"));
        Throws<NotSupportedException>(() => DocumentStore.Deserialize("{\"schemaVersion\":3}"));
        Throws<NotSupportedException>(() => DocumentStore.Deserialize("""{"schemaVersion":3,"title":"Future data"}"""));
        var badText = Path.Combine(output,"invalid-utf8.json");
        var validBytes = Encoding.UTF8.GetBytes(DocumentStore.Serialize(Sample()));
        var badBytes = (byte[])validBytes.Clone();
        badBytes[badBytes.AsSpan().IndexOf("A contextual note."u8)] = 0xff;
        foreach (var prefix in new[] { Array.Empty<byte>(), Encoding.UTF8.GetPreamble() })
        {
            File.WriteAllBytes(badText,prefix.Concat(badBytes).ToArray());
            Throws<InvalidDataException>(() => DocumentStore.LoadJson(badText));
            File.WriteAllBytes(badText,prefix.Concat(validBytes).ToArray());
            Equal("A contextual note.",DocumentStore.LoadJson(badText).Pages[0].Items[1].Text.Split('\n')[0]);
        }
        var data = Sample();
        data.Pages[0].SpacingMm = 0;
        Throws<InvalidDataException>(() => DocumentStore.Serialize(data));
        data = Sample(); data.Pages[0].Ink[0].Points[0].X = double.NaN;
        Throws<InvalidDataException>(() => DocumentStore.Serialize(data));
        data = Sample(); data.Pages[0].Items[0].Color = "#INVALID";
        Throws<InvalidDataException>(() => DocumentStore.Serialize(data));
        data = Sample(); data.Pages[0].Items[1].Id = data.Pages[0].Items[0].Id;
        Throws<InvalidDataException>(() => DocumentStore.Serialize(data));
        var json = JsonNode.Parse(DocumentStore.Serialize(Sample()))!;
        json["pages"]![0]!["ink"] = null;
        Throws<InvalidDataException>(() => DocumentStore.Deserialize(json.ToJsonString()));
    }

    private static void TitledNotebook()
    {
        var notebook = Sample();
        notebook.Title = "Field book · Süd";
        var page = notebook.Pages[0];
        var note = page.Items.Single(item => item.IsNote);
        note.Title = "Maueranschluss — α";
        var placed = new ImageItem { X = 142.5, Y = 315, Width = 320, Height = 240,
            PngData = File.ReadAllBytes(ImageSample().SourceImagePath!) };
        page.Images.Add(placed);
        var path = Path.Combine(output, "placed-photo.pdf");
        DocumentStore.SavePdf(notebook,path);
        var reopened = DocumentStore.LoadPdf(path);
        Equal(DocumentStore.Serialize(notebook), DocumentStore.Serialize(reopened));
        using var pdf = PdfReader.Open(path,PdfDocumentOpenMode.Import);
        Equal(notebook.Title, pdf.Info.Title);
        var comment = pdf.Pages[0].Elements.GetArray("/Annots")!.Elements.GetDictionary(2)!;
        Equal(note.Title,comment.Elements.GetString("/Subj"));
        Equal(note.Text,comment.Elements.GetString("/Contents"));
        var objects = pdf.Pages[0].Resources.Elements.GetDictionary("/XObject")!;
        var image = objects.Elements.GetDictionary(objects.Elements.Keys.Single())!;
        Equal("/Image",image.Elements.GetName("/Subtype"));
        Equal(640,image.Elements.GetInteger("/Width"));
        Equal(480,image.Elements.GetInteger("/Height"));
        var content = Encoding.ASCII.GetString(pdf.Pages[0].Contents.Elements.GetDictionary(0)!.Stream.UnfilteredValue);
        Assert(Regex.IsMatch(content,@"240 0 0 180 106\.875 [\d.]+ cm\s*/\w+ Do"),"Placed PNG lacks the expected PDF drawing transform.");
        var png = EmbeddedPng.Decode(reopened.Pages[0].Images.Single().PngData);
        Equal(640,png.PixelWidth); Equal(480,png.PixelHeight);
    }

    private static void SchemaCompatibility()
    {
        const string legacy = """{"schemaVersion":1,"kind":"Notebook","pages":[{"items":[{"id":"existing","isNote":true,"text":"Legacy note"}]}]}""";
        var data = DocumentStore.Deserialize(legacy);
        Equal("",data.Title); Equal("",data.Pages[0].Items.Single().Title); Equal(0,data.Pages[0].Images.Count);
        Equal(1,JsonNode.Parse(DocumentStore.Serialize(data))!["schemaVersion"]!.GetValue<int>());
        data.Pages[0].Items[0].Title = "Named";
        Equal(2,JsonNode.Parse(DocumentStore.Serialize(data))!["schemaVersion"]!.GetValue<int>());
        Equal("Named",DocumentStore.Deserialize(DocumentStore.Serialize(data)).Pages[0].Items[0].Title);
        data.Title = "Invalid\nmultiline title";
        Throws<InvalidDataException>(() => DocumentStore.Serialize(data));
    }

    private static void InvalidImages()
    {
        var data = Sample();
        var good = File.ReadAllBytes(ImageSample().SourceImagePath!);
        var image = new ImageItem { Width = 20, Height = 20, PngData = (byte[])good.Clone() };
        data.Pages[0].Images.Add(image);
        Array.Fill(image.PngData, (byte)255, 16, 8);
        Throws<InvalidDataException>(() => DocumentStore.Serialize(data));
        image.PngData = good;
        var json = JsonNode.Parse(DocumentStore.Serialize(data))!;
        var invalid = good[..33].Concat(good[^12..]).ToArray(); // Valid dimensions but no IDAT.
        json["pages"]![0]!["images"]![0]!["pngData"] = Convert.ToBase64String(invalid);
        Throws<InvalidDataException>(() => DocumentStore.Deserialize(json.ToJsonString()));
        image.Id = data.Pages[0].Ink[0].Id;
        Throws<InvalidDataException>(() => DocumentStore.Validate(data));
    }

    private static void ForeignPdf()
    {
        var path = Path.Combine(output, "foreign.pdf");
        using (var pdf = new PdfDocument()) { pdf.AddPage(); pdf.Save(path); }
        var before = DocumentStore.GetFileHash(path);
        Throws<NotSupportedException>(() => DocumentStore.LoadPdf(path));
        Equal(before, DocumentStore.GetFileHash(path));
    }

    private static void ChangedPdf()
    {
        var path = Path.Combine(output, "changed.pdf");
        DocumentStore.SavePdf(Sample(), path);
        using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify)) { pdf.Pages.RemoveAt(1); pdf.Save(path); }
        Throws<InvalidDataException>(() => DocumentStore.LoadPdf(path));
        var annotationPath = Path.Combine(output, "changed-annotation.pdf");
        DocumentStore.SavePdf(Sample(), annotationPath);
        using (var pdf = PdfReader.Open(annotationPath, PdfDocumentOpenMode.Modify))
        {
            pdf.Pages[0].Elements.GetArray("/Annots")!.Elements.GetDictionary(2)!.Elements.SetString("/Contents", "External edit");
            pdf.Save(annotationPath);
        }
        Throws<NotSupportedException>(() => DocumentStore.LoadPdf(annotationPath));
    }

    private static void InvalidPdfs()
    {
        var invalid = Path.Combine(output, "invalid.pdf");
        File.WriteAllText(invalid, "not a PDF");
        Throws<InvalidDataException>(() => DocumentStore.LoadPdf(invalid));
        File.WriteAllText(invalid, "%PDF-1.7\n1 0 obj\n<< /Type /Catalog");
        Throws<InvalidDataException>(() => DocumentStore.LoadPdf(invalid));
        var encrypted = Path.Combine(output, "encrypted.pdf");
        using (var pdf = new PdfDocument())
        {
            pdf.AddPage();
            pdf.SecuritySettings.UserPassword = "test-only-password";
            pdf.SecuritySettings.OwnerPassword = "test-only-owner-password";
            pdf.Save(encrypted);
        }
        Throws<InvalidDataException>(() => DocumentStore.LoadPdf(encrypted));
        var ownerOnly = Path.Combine(output, "owner-encrypted.pdf");
        DocumentStore.SavePdf(Sample(), ownerOnly);
        using (var pdf = PdfReader.Open(ownerOnly, PdfDocumentOpenMode.Modify))
        {
            pdf.SecuritySettings.OwnerPassword = "test-only-owner-password";
            pdf.SecuritySettings.PermitModifyDocument = false;
            pdf.Save(ownerOnly);
        }
        var protectedHash = DocumentStore.GetFileHash(ownerOnly);
        Throws<NotSupportedException>(() => DocumentStore.LoadPdf(ownerOnly));
        Equal(protectedHash, DocumentStore.GetFileHash(ownerOnly));
    }

    private static void ChangedPdfObjectGraph()
    {
        void Mutate(string name, Action<PdfDocument> change, DocumentData? data = null)
        {
            var path = Path.Combine(output, name + ".pdf");
            DocumentStore.SavePdf(data ?? Sample(), path);
            using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify)) { change(pdf); pdf.Save(path); }
            var modifiedHash = DocumentStore.GetFileHash(path);
            Throws<NotSupportedException>(() => DocumentStore.LoadPdf(path));
            Equal(modifiedHash, DocumentStore.GetFileHash(path));
        }
        static PdfDictionary Annotation(PdfDocument pdf, int index)
            => pdf.Pages[0].Elements.GetArray("/Annots")!.Elements.GetDictionary(index)!;
        static PdfDictionary TextFont(PdfDocument pdf)
        {
            var fonts = Annotation(pdf, 1).Elements.GetDictionary("/AP")!.Elements.GetDictionary("/N")!
                .Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
            return fonts.Elements.GetDictionary(fonts.Elements.Keys.First())!;
        }
        Mutate("changed-rotation", pdf => pdf.Pages[0].Elements.SetInteger("/Rotate", 180));
        Mutate("changed-parent-rotation", pdf => pdf.Pages[0].Elements.GetDictionary("/Parent")!.Elements.SetInteger("/Rotate", 180));
        Mutate("changed-annotation-opacity", pdf => Annotation(pdf, 0).Elements.SetReal("/CA", .4));
        Mutate("changed-rich-note", pdf => Annotation(pdf, 2).Elements.SetString("/RC", "<p>An external rich-text edit</p>"));
        var named = Sample(); named.Title = "Field book";
        Mutate("changed-notebook-title", pdf => pdf.Info.Title = "External rename", named);
        Mutate("changed-note-subject", pdf => Annotation(pdf, 2).Elements.SetString("/Subj", "An external rename"));
        Mutate("changed-font-resource", pdf => TextFont(pdf).Elements.SetName("/BaseFont", "/Courier"));
        Mutate("changed-font-stream", pdf =>
        {
            var fontFile = TextFont(pdf).Elements.GetDictionary("/FontDescriptor")!.Elements.GetDictionary("/FontFile2")!;
            var bytes = (byte[])fontFile.Stream.Value.Clone(); bytes[^1] ^= 1; fontFile.Stream.Value = bytes;
        });
        Mutate("changed-cyclic-annotation", pdf =>
        {
            var note = Annotation(pdf, 2); note.Elements["/IRT"] = note.Reference!;
        });
        Mutate("changed-image-resource", pdf =>
        {
            var images = pdf.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
            var image = images.Elements.GetDictionary(images.Elements.Keys.First())!;
            var bytes = (byte[])image.Stream.Value.Clone(); bytes[^1] ^= 1; image.Stream.Value = bytes;
        }, ImageSample());
        Mutate("changed-catalog-view", pdf => pdf.Internals.Catalog.Elements.SetName("/PageLayout", "/TwoColumnLeft"));

        // Rewriting the same graph through PDFsharp does not invalidate the hash because of
        // object number allocation, dictionary enumeration order or stream lengths.
        var unchanged = Path.Combine(output, "unchanged-rewritten.pdf");
        var original = Sample(); DocumentStore.SavePdf(original, unchanged);
        using (var pdf = PdfReader.Open(unchanged, PdfDocumentOpenMode.Modify)) { pdf.Save(unchanged); }
        Equal(DocumentStore.Serialize(original), DocumentStore.Serialize(DocumentStore.LoadPdf(unchanged)));
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, actual {actual}.");
    private static void Near(double expected, double actual, double tolerance)
        => Assert(Math.Abs(expected - actual) <= tolerance, $"Expected {expected}, actual {actual}.");
}
