using System.IO;
using System.Text;
using Inklane.Core;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void PortableNotebookJourney()
    {
        using var state = new WindowCase("portable-notebook");
        var window = state.Window;
        var imported = state.Path("Browser.inklane");
        File.Copy(Path.Combine(AppContext.BaseDirectory,"browser-notebook.inklane"), imported);
        var initial = DocumentStore.GetFileHash(imported);
        window.OpenFile(imported);
        var doc = Document(window);
        Equal(DocumentKind.Notebook, doc.Kind);
        Equal("Field corrections", doc.Title);
        Equal(2,doc.Pages.Count);
        Equal(PaperPattern.Squared,doc.Pages[1].Pattern);
        Equal(7.5,doc.Pages[0].SpacingMm);
        Assert(Math.Abs(Units.DipToMm(doc.Pages[0].Width)-148)<.001,"The A5 width changed.");
        Equal("Use a lighter outline",doc.Pages[0].Items.Single().Text);
        Equal("Keep the scale here.",doc.Pages[1].Items.Single().Text);
        AddText(window, "Reviewed on Windows");
        var copy = state.Path("Windows.inklane");
        state.Interaction.SavePaths.Enqueue(copy);
        Call(window,"ExportPortableNotebook");
        Assert(File.Exists(copy),"The Windows portable copy was not written.");
        var reopened = PortableNotebook.Read(copy);
        Equal(DocumentStore.Serialize(doc),DocumentStore.Serialize(reopened));
        Equal(initial, DocumentStore.GetFileHash(imported));
        Assert(Dirty(window),"Exporting a copy marked the open notebook saved.");
        // An explicit Save on an imported notebook uses the normal editable PDF destination.
        var pdf = state.Path("Imported.pdf");
        state.Interaction.SavePaths.Enqueue(pdf);
        Assert(Call<bool>(window,"Save",false),"The imported notebook did not save as an editable PDF.");
        Equal("Reviewed on Windows",DocumentStore.LoadPdf(pdf).Pages[0].Items.Last().Text);
        Equal(initial,DocumentStore.GetFileHash(imported));
        // A portable copy is always a new file, even when a picker returns an occupied path.
        var unrelated = state.Path("Occupied.inklane");File.WriteAllText(unrelated,"Keep my file.");
        state.Interaction.SavePaths.Enqueue(unrelated);Call(window,"ExportPortableNotebook");
        Equal("Keep my file.",File.ReadAllText(unrelated));
        var previous=State(window);
        var invalid=state.Path("Invalid.inklane");File.Copy(imported,invalid);
        using(var extra=File.Open(invalid,FileMode.Append)) extra.WriteByte(42);
        window.OpenFile(invalid);
        Equal(previous,State(window));
        Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(copy)).Contains(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase),"Portable metadata exposed a local directory.");
    }
}
