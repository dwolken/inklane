using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane.SurfaceTests;

internal static partial class Program
{
    private static void RichTextLifecycle()
    {
        var text = new TextItem { X = 70, Y = 90, Width = 440, Height = 650, ContentFormat = "Markdown", FontSize = 18,
            Text = "# A clearer explanation\n\n**Bold**, *italic* and ++underlined++.\n\n- First point\n- Second point\n\n| Quantity | Value |\n| --- | --- |\n| Radius | 4 |\n\n```js\nconst area = Math.PI * 16;\n```\n\n$$\n\\frac{a}{b} = \\sqrt{x^2 + 1}\n$$" };
        var page = new PageData { Items = [text] }; var surface = Surface(page);
        var drawing = StructuredText.CreateDrawing(text); Assert(!drawing.Bounds.IsEmpty && drawing.Bounds.Height > 180 && drawing.Bounds.Height < 2000, $"Invalid rich layout {drawing.Bounds}.");
        Save(surface.RenderPage(), "rich-text-math.png");
        var path = Path.Combine(_fixtures,"rich-text.pdf"); DocumentStore.SavePdf(new DocumentData { Pages = [page] },path);
        var reopened = DocumentStore.LoadPdf(path); Equal(text.Text,reopened.Pages[0].Items[0].Text); Equal("Markdown",reopened.Pages[0].Items[0].ContentFormat);
        Call(surface,"SelectObject",text.Id); Call(surface,"EditSelectedContent"); var editor = Editor(surface,"Text"); editor.Text += "\n\nAn editable follow-up.";
        surface.CommitEdits(); Assert(text.Text.EndsWith("An editable follow-up."),"Rich source did not stay editable.");
        var browser = Environment.GetEnvironmentVariable("INKLANE_CROSS_READER_FIXTURE");
        if (!string.IsNullOrEmpty(browser))
        {
            var portable = PortableWorkspace.Read(browser); var output = Path.Combine(_output,"native-browser-roundtrip.inklane");
            using (var stream = File.Create(output)) PortableWorkspace.Write(stream,portable.Document,"native-browser-roundtrip.inklane");
            surface.LoadPage(portable.Document.Pages[0]); Save(surface.RenderPage(),"browser-document-native.png");
            DocumentStore.SavePdf(portable.Document,Path.Combine(_output,"browser-document-native.pdf"));
        }
    }

    private static void TextResizeAndStyle()
    {
        var text = new TextItem { X = 90, Y = 110, Width = 280, Height = 100, FontSize = 24, Text = "Text stays editable and wraps when its box becomes narrower." };
        var page = new PageData { Items = [text] }; var surface = Surface(page); surface.Tool = EditorTool.Select;
        Call(surface, "SelectObject", text.Id); Layout(surface);
        var handle = Descendants(surface).OfType<Thumb>().Single(t => AutomationProperties.GetAutomationId(t) == $"TextResize:{text.Id}:1:1");
        handle.RaiseEvent(new DragStartedEventArgs(0, 0)); handle.RaiseEvent(new DragDeltaEventArgs(-100, 80)); handle.RaiseEvent(new DragCompletedEventArgs(-100, 80, false));
        Near(180, text.Width); Near(180, text.Height); Near(24, text.FontSize);
        Call(surface, "ApplyTextFormatting", (Action<TextItem>)(t => { t.FontFamily = "Georgia"; t.Bold = true; t.Italic = true; t.Underline = true; t.Alignment = "Right"; }));
        Call(surface, "ApplySelectedColor", "#F28C28", .7, false);
        var path = Path.Combine(_fixtures, "resized-styled.pdf"); DocumentStore.SavePdf(new DocumentData { Pages = [page] }, path);
        var reopened = DocumentStore.LoadPdf(path).Pages[0]; surface.LoadPage(reopened); surface.Tool = EditorTool.Select; Call(surface, "SelectObject", reopened.Items[0].Id);
        Call(surface, "ApplyTextFormatting", (Action<TextItem>)(t => t.FontSize = 31)); Equal(31d, reopened.Items[0].FontSize); Equal("Georgia", reopened.Items[0].FontFamily); Equal("#F28C28", reopened.Items[0].Color);
        Layout(surface); Save(surface.RenderPage(), "resized-styled-text.png");
        var before = State(reopened); handle = Descendants(surface).OfType<Thumb>().Single(t => AutomationProperties.GetAutomationId(t) == $"TextResize:{text.Id}:-1:0");
        handle.RaiseEvent(new DragStartedEventArgs(0, 0)); handle.RaiseEvent(new DragDeltaEventArgs(20, 0)); handle.RaiseEvent(new DragCompletedEventArgs(20, 0, true)); Equal(before, State(reopened));
    }

    private static void DiagramLifecycle()
    {
        var page = new PageData(); var surface = Surface(page);
        var first = (ShapeItem)Call(surface, "AddShape", new Point(100, 100), ShapeKind.Rectangle)!;
        var second = (ShapeItem)Call(surface, "AddShape", new Point(400, 260), ShapeKind.Ellipse)!;
        Call(surface, "SelectAllObjects"); Call(surface, "ConnectSelectedShapes"); Equal(3, page.Shapes.Count);
        var connector = page.Shapes.Single(s => s.Kind == ShapeKind.Arrow); var before = ShapeGeometry.Endpoints(page, connector);
        Call(surface, "SelectObject", first.Id); Call(surface, "NudgeSelection", Key.Right, true);
        Assert(ShapeGeometry.Endpoints(page, connector).Start.X > before.Start.X, "Connector lost its source relation.");
        Call(surface, "SelectAllObjects"); Call(surface, "GroupSelection", false); Call(surface, "DuplicateObjects"); Equal(6, page.Shapes.Count);
        var copy = page.Shapes.Last(); Assert(copy.StartId != first.Id && copy.EndId != second.Id, "Duplication retained original node IDs.");
        Assert(page.Shapes.Any(s => s.Id == copy.StartId && s.GroupId == copy.GroupId), "Duplicate group cannot resolve connector.");
        surface.DeleteSelection(); Equal(3, page.Shapes.Count);
        Call(surface, "SelectObject", first.Id); Call(surface, "GroupSelection", true); Call(surface, "ClearObjectSelection"); Call(surface, "SelectObject", first.Id); surface.DeleteSelection();
        Equal(null, connector.StartId); DocumentStore.Validate(new DocumentData { Pages = [page] });
        Save(surface.RenderPage(), "editable-diagram.png");
    }

    private static void ColorLifecycle()
    {
        var page = Sample(); page.Ink[0].Opacity = .5;
        var surface = Surface(page); Equal(.5, page.Ink[0].Opacity);
        var before = State(page); surface.CommitEdits(); Equal(before, State(page));
        Call(surface, "SelectAllObjects"); Call(surface, "ApplySelectedColor", "#F4CC31", .42, false); surface.CommitEdits();
        foreach (var text in page.Items) { Equal("#F4CC31", text.Color); Near(.42, text.Opacity); }
        Equal("#F4CC31", page.Ink[0].Color); Near(.42, page.Ink[0].Opacity);
        var document = DocumentStore.Clone(new DocumentData { Pages = [page] }); surface.LoadPage(document.Pages[0]); Call(surface, "SelectAllObjects");
        Call(surface, "ApplySelectedColor", "#F28C28", .85, false); Near(.85, document.Pages[0].Ink[0].Opacity);
    }
}
