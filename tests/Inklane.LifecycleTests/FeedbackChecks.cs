using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Inklane.Core;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void ExportReport(WindowCase state, string? path)
    {
        state.Interaction.SavePaths.Enqueue(path);
        var menu = Call<ContextMenu>(state.Window,"EditorMenu");
        var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header,L.T("ExportFeedback")));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static void ImageFeedback()
    {
        using var state = new WindowCase("feedback-image");
        var path = state.Path("Example.png"); WriteImage(path);
        var w = state.Window; w.OpenFile(path);
        var page = Document(w).Pages[0];
        var picture = new ImageItem { PngData=File.ReadAllBytes(path), X=7, Y=8, Width=21, Height=24 };
        page.Images.Add(picture);
        page.Ink.Add(new InkData { Color="#FF0011",Width=3,Points=[new InkPoint { X=2,Y=3,Pressure=.75f }] });
        const string noteText = "A longer observation:\n\u00dcberpr\u00fcfen <script> & keep.";
        page.Items.Add(new TextItem { IsNote=true, IsExpanded=false, Title="Threshold", Text=noteText,X=14,Y=19,Width=120,Height=68 });
        AddText(w,"Placed text");
        var hash = DocumentStore.GetFileHash(path);
        var report = state.Path("Example.png.feedback.json");
        ExportReport(state,report);
        Assert(Dirty(w),"Export marked editable changes as saved.");
        using var json = JsonDocument.Parse(File.ReadAllBytes(report));
        var root = json.RootElement;
        Equal("inklane.feedback",root.GetProperty("format").GetString()); Equal(1,root.GetProperty("version").GetInt32());
        var source = root.GetProperty("source");
        Equal("Example.png",source.GetProperty("name").GetString()); Equal(hash.ToLowerInvariant(),source.GetProperty("sha256").GetString());
        Equal("image/png",source.GetProperty("mediaType").GetString()); Equal("top-left",root.GetProperty("coordinateSystem").GetString());
        var sheet = root.GetProperty("pages")[0]; Equal("image-pixel",sheet.GetProperty("unit").GetString());
        Equal(64,sheet.GetProperty("width").GetInt32()); Equal(64,sheet.GetProperty("height").GetInt32());
        var marks = sheet.GetProperty("annotations");
        Equal(4,marks.GetArrayLength()); Equal("picture",marks[0].GetProperty("type").GetString());
        Equal(picture.Id,marks[0].GetProperty("id").GetString()); Equal(hash.ToLowerInvariant(),marks[0].GetProperty("sha256").GetString());
        Equal(7,marks[0].GetProperty("x").GetInt32()); Equal(24,marks[0].GetProperty("height").GetInt32());
        Equal("ink",marks[1].GetProperty("type").GetString()); Equal(.75,marks[1].GetProperty("points")[0].GetProperty("pressure").GetDouble());
        Equal("note",marks[2].GetProperty("type").GetString()); Equal(noteText,marks[2].GetProperty("text").GetString());
        Equal("Threshold",marks[2].GetProperty("title").GetString()); Equal(14,marks[2].GetProperty("x").GetInt32());
        Equal("text",marks[3].GetProperty("type").GetString()); Equal("Placed text",marks[3].GetProperty("text").GetString());
        Assert(!marks[3].TryGetProperty("title",out _) && !marks[0].TryGetProperty("pngData",out _),"Export leaked internal object fields.");
        Assert(!File.ReadAllText(report).Contains("sourceImagePath"),"Export contained a private path field.");
        ExportReport(state,report); Assert(state.Interaction.Messages.Count==0,"Could not update this source’s report.");
        var reportHash = DocumentStore.GetFileHash(report);
        Assert(Call<bool>(w,"Save",false),"Cannot save image markup.");
        var sidecar = Field<string>(w,"_filePath"); var sidecarHash=DocumentStore.GetFileHash(sidecar);
        ExportReport(state,sidecar); ExportReport(state,path);
        Equal(hash,DocumentStore.GetFileHash(path)); Equal(sidecarHash,DocumentStore.GetFileHash(sidecar));
        var impostor=state.Path("other.feedback.json"); File.Copy(sidecar,impostor);
        ExportReport(state,impostor); Equal(sidecarHash,DocumentStore.GetFileHash(impostor));
        File.AppendAllText(path,"changed"); ExportReport(state,report);
        Equal(reportHash,DocumentStore.GetFileHash(report)); Equal(4,state.Interaction.Messages.Count);
    }

    private static void PdfFeedback()
    {
        using var state = new WindowCase("feedback-pdf");
        var file=state.Path("Lesson.pdf"); CreateSourcePdf(file);
        state.Window.OpenFile(file); Call(state.Window,"Navigate",1);
        Document(state.Window).Pages[1].Items.Add(new TextItem { IsNote=true, Title="Revisit",Text="Check the figure.",X=51,Y=75 });
        var report=state.Path("Lesson.feedback.json"); ExportReport(state,report);
        using var json=JsonDocument.Parse(File.ReadAllBytes(report));
        var root=json.RootElement;
        Equal("application/pdf",root.GetProperty("source").GetProperty("mediaType").GetString());
        Equal(DocumentStore.GetFileHash(file).ToLowerInvariant(),root.GetProperty("source").GetProperty("sha256").GetString());
        var pages=root.GetProperty("pages"); Equal(2,pages.GetArrayLength()); Equal(0,pages[0].GetProperty("annotations").GetArrayLength());
        Equal("dip-96",pages[0].GetProperty("unit").GetString()); Equal(400d,pages[0].GetProperty("width").GetDouble());
        Equal(2,pages[1].GetProperty("number").GetInt32()); Equal(75,pages[1].GetProperty("annotations")[0].GetProperty("y").GetInt32());
        var raw=File.ReadAllText(report);
        Assert(!raw.Contains("Elevations") && !raw.Contains("Survey detail"),"Source PDF contents were exported.");
    }

    private static void NotebookFeedback()
    {
        using var state=new WindowCase("feedback-notebook");
        var w=state.Window;
        AddText(w,"Handout");
        state.Interaction.Choices.Enqueue(1); Call(w,"ExportFeedback");
        Assert(Dirty(w),"Cancel before saving lost notebook changes.");
        var file=state.Path("Workbook.pdf"); var report=state.Path("Workbook.feedback.json");
        state.Interaction.Choices.Enqueue(0); state.Interaction.SavePaths.Enqueue(file);
        ExportReport(state,report);
        var hash=DocumentStore.GetFileHash(file);
        using var json=JsonDocument.Parse(File.ReadAllBytes(report));
        Equal(hash.ToLowerInvariant(),json.RootElement.GetProperty("source").GetProperty("sha256").GetString());
        Call(w,"AddPage"); state.Interaction.Choices.Enqueue(1); Call(w,"ExportFeedback");
        Equal(hash,DocumentStore.GetFileHash(file)); Assert(Dirty(w),"Cancel exported unbound page changes.");
        Call(w,"Undo"); File.AppendAllText(file,"changed");
        var reportHash=DocumentStore.GetFileHash(report); ExportReport(state,report);
        Equal(reportHash,DocumentStore.GetFileHash(report));
        Assert(state.Interaction.Messages.Any(m=>m.Icon==MessageBoxImage.Error),"Stale notebook identity was not explained.");
    }
}
