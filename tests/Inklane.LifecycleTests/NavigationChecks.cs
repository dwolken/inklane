using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Inklane.Core;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void NotebookTitle()
    {
        using var state = new WindowCase("notebook-title");
        var file = state.Path("exchange.pdf");
        DocumentStore.SavePdf(Notebook("Keep this page"), file);
        var w = state.Window; w.OpenFile(file);
        var button = Field<Button>(w, "_titleButton");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var edit = Field<TextBox>(w, "_titleEditor");
        Equal("exchange", edit.Text);
        edit.Text = "Do not keep this"; Call(w, "FinishRenameNotebook", false);
        Equal("", Document(w).Title); Assert(!Dirty(w), "Cancel dirtied the notebook.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        edit.Text = "Nord / West · Maßkontrolle";
        Assert(Call<bool>(w, "Save", false), "Could not save while editing the header.");
        Equal(file, Field<string>(w, "_filePath"));
        Equal("Nord / West · Maßkontrolle", DocumentStore.LoadPdf(file).Title);
        Equal("Nord _ West · Maßkontrolle.pdf", Call<string>(w,"SuggestedPdfName"));
        w.OpenFile(file); Equal("Nord / West · Maßkontrolle", Field<TextBlock>(w,"_documentTitle").Text);
    }

    private static void ManagePages()
    {
        using var state = new WindowCase("reorder-pages");
        var file = state.Path("walkthrough.pdf");
        var notebook = Notebook("South");
        notebook.Pages.Add(new PageData { Pattern = PaperPattern.Squared, SpacingMm = 10,
            Items = [new TextItem { IsNote = true, Title = "Porch", Text = "Keep access clear" }] });
        var start = new ShapeItem { Id="node-start", X=80, Y=300, GroupId="diagram", Label="Start" };
        var end = new ShapeItem { Id="node-end", X=380, Y=300, GroupId="diagram", Kind=ShapeKind.Ellipse, Label="End" };
        notebook.Pages[1].Shapes.AddRange([start,end,new ShapeItem { Id="edge", Kind=ShapeKind.Arrow, StartId=start.Id, EndId=end.Id, GroupId="diagram" }]);
        DocumentStore.SavePdf(notebook,file);
        var w = state.Window; w.OpenFile(file); Call(w,"Navigate",1);
        var original = Document(w).Pages[1].Items.Single();
        Call(w,"DuplicatePage");
        var duplicate = Document(w).Pages[2].Items.Single();
        var copiedShapes = Document(w).Pages[2].Shapes;
        Assert(copiedShapes.All(s => notebook.Pages[1].Shapes.All(original => original.Id != s.Id && original.GroupId != s.GroupId)),"Page duplication reused graph identities.");
        var edge = copiedShapes.Single(s => s.Kind == ShapeKind.Arrow);
        Assert(copiedShapes.Any(s => s.Id == edge.StartId) && copiedShapes.Any(s => s.Id == edge.EndId),"Copied connector did not reference its copied nodes.");
        Assert(original.Id != duplicate.Id && original != duplicate, "Page duplicate aliases the original note.");
        Equal(original.Title,duplicate.Title); Equal(original.Text,duplicate.Text); Equal(10d,Document(w).Pages[2].SpacingMm);
        Call(w,"MovePage",-1);
        Equal(1,Field<int>(w,"_pageIndex")); Equal(duplicate.Id,Document(w).Pages[1].Items.Single().Id);
        Call(w,"Undo"); Equal(2,Field<int>(w,"_pageIndex")); Equal(duplicate.Id,Document(w).Pages[2].Items.Single().Id);
        Call(w,"Undo"); Equal(1,Field<int>(w,"_pageIndex")); Equal(2,Document(w).Pages.Count);
        Assert(!Dirty(w),"Undo to original page structure is still dirty.");
        Call(w,"Redo"); Call(w,"Redo");
        state.Interaction.Choices.Enqueue(1); Call(w,"RemovePage"); Equal(3,Document(w).Pages.Count);
        Assert(Call<bool>(w,"Save",false),"Could not save page order.");
        w.OpenFile(file); Equal(duplicate.Id,Document(w).Pages[1].Items.Single().Id);
        Equal(original.Id,Document(w).Pages[2].Items.Single().Id);
    }

    private static void LiveImageFolder()
    {
        using var state = new WindowCase("folder-navigation");
        var two = state.Path("Shot 2.png"); var ten = state.Path("Shot 10.png"); var three = state.Path("Shot 3.PNG");
        WriteImage(two); WriteImage(ten); File.WriteAllText(state.Path("Shot 1.txt"),"exclude");
        var w = state.Window; w.OpenFile(two);
        Equal(2, Field<string[]>(w,"_folderImages").Length);
        Assert(!Field<Button>(w,"_previous").IsEnabled, "Unexpected previous item before first image.");
        var sourceHash = DocumentStore.GetFileHash(two);
        AddText(w,"Keep this markup"); state.Interaction.Choices.Enqueue(2);
        Call(w,"Navigate",1); Equal(two,Document(w).SourceImagePath);
        state.Interaction.Choices.Enqueue(0); Call(w,"Navigate",1); Equal(ten,Document(w).SourceImagePath);
        Assert(!Field<Button>(w,"_next").IsEnabled, "Navigation wraps beyond last image.");
        Equal(sourceHash,DocumentStore.GetFileHash(two));
        WriteImage(three);
        PumpUntil(() => Field<string[]>(w,"_folderImages").Length == 3);
        Call(w,"Navigate",-1); Equal(three,Document(w).SourceImagePath);
        File.Delete(three);
        PumpUntil(() => Field<TextBlock>(w,"_pageLabel").Text == L.F("MissingFolderImage",2),
            () => $"Footer '{Field<TextBlock>(w,"_pageLabel").Text}', images {string.Join(", ",Field<string[]>(w,"_folderImages"))}, source {Document(w).SourceImagePath}.");
        Assert(Field<Button>(w,"_previous").IsEnabled && Field<Button>(w,"_next").IsEnabled,"Cannot leave externally removed middle image in both directions.");
        Call(w,"Navigate",-1); Equal(two,Document(w).SourceImagePath);
        Equal("Keep this markup",Document(w).Pages[0].Items.Single().Text);
    }

    private static void PumpUntil(Func<bool> predicate, Func<string>? diagnostic = null)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(20);
        }
        Assert(predicate(), "Timed out waiting for the filesystem change to reach the workspace. " + diagnostic?.Invoke());
    }
}
