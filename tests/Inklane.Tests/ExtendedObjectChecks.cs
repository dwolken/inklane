using System.IO;
using System.Windows;
using Inklane.Core;
using PdfSharp.Pdf.IO;

namespace Inklane.Tests;

internal static partial class Program
{
    private static void ExtendedObjectRoundTrip()
    {
        var first = new ShapeItem { X = 70, Y = 75, Kind = ShapeKind.Ellipse, Color = "#1F9D81", FillColor = "#FFF5C8", Label = "Input", GroupId = "diagram" };
        var second = new ShapeItem { X = 370, Y = 175, Kind = ShapeKind.Diamond, Color = "#A16ACE", Rotation = 12, GroupId = "diagram" };
        var arrow = new ShapeItem { Kind = ShapeKind.Arrow, StartId = first.Id, EndId = second.Id, X = 100, Y = 100, Width = 250, Height = 80, Opacity = .65, GroupId = "diagram" };
        var text = new TextItem { X = 70, Y = 330, Width = 400, Height = 140, Text = "Resizable text — Grüße", FontFamily = "Georgia", FontSize = 22, Bold = true, Italic = true, Underline = true, Alignment = "Center", Color = "#E88713", Opacity = .75 };
        var data = new DocumentData { Pages = [new PageData { Shapes = [first, second, arrow], Items = [text] }] };
        Equal(4, data.SchemaVersion);
        var json = DocumentStore.Serialize(data); Equal(json, DocumentStore.Serialize(DocumentStore.Deserialize(json)));
        var path = Path.Combine(output, "extended-objects.pdf"); DocumentStore.SavePdf(data, path);
        Equal(json, DocumentStore.Serialize(DocumentStore.LoadPdf(path)));
        using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import))
        {
            Equal(4, pdf.Pages[0].Annotations.Count);
            for (int i = 0; i < pdf.Pages[0].Annotations.Count; i++) Assert(pdf.Pages[0].Annotations[i].Elements.GetDictionary("/AP") != null, "Missing shape or text PDF appearance.");
        }
        var endpoints = ShapeGeometry.Endpoints(data.Pages[0], arrow);
        second.X += 90;
        Assert(ShapeGeometry.Endpoints(data.Pages[0], arrow).End.X > endpoints.End.X, "Attached endpoint did not follow moved node.");
        arrow.EndId = "missing"; Throws<InvalidDataException>(() => DocumentStore.Serialize(data)); arrow.EndId = second.Id;
        first.Opacity = double.NaN; Throws<InvalidDataException>(() => DocumentStore.Serialize(data)); first.Opacity = 1;
        text.Alignment = "Unknown"; Throws<InvalidDataException>(() => DocumentStore.Serialize(data)); text.Alignment = "Center";
        data.SchemaVersion = 99; Throws<NotSupportedException>(() => DocumentStore.Serialize(data));
        var legacy = new DocumentData { Pages = [new PageData()] }; Equal(1, legacy.SchemaVersion);
    }
}
