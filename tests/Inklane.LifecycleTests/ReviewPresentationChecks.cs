using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using System.Xml.Linq;
using Inklane.Core;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void CompletePresentationRemarks()
    {
        using var state = new WindowCase("complete-remark-output");
        var page = Document(state.Window).Pages[0]; page.Width = 500; page.Height = 420;
        page.Items.Add(new TextItem { IsNote = true, IsExpanded = false, X = 60, Y = 80, Title = "Clearance",
            ContentFormat = "Markdown", Text = "## Keep the route clear\n\n**Review** the entrance.\n\n- Measure the opening\n- Check $a^2+b^2=c^2$\n\n```js\nconst width = 120;\n```", FontSize = 16 });
        page.Items.Add(new TextItem { IsNote = true, IsExpanded = false, X = 240, Y = 210, Title = "Complete explanation", FontSize = 16,
            Text = string.Concat(Enumerable.Repeat("Every paragraph remains available to the recipient, even when the card is collapsed.\n", 45)) + "LAST EXPLANATION REMAINS VISIBLE" });
        Call(state.Window,"ShowPage",false); Call(state.Window,"DocumentChanged");
        var before = DocumentStore.Serialize(Document(state.Window));
        var png = state.Path("complete-remarks.png"); state.Interaction.SavePaths.Enqueue(png); Call(state.Window,"ExportImage");
        Assert(File.Exists(png), "Full remark PNG export failed: " + string.Join("; ",state.Interaction.Messages.Select(m=>m.Text)));
        var bitmap = BitmapFrame.Create(new Uri(png),BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
        Assert(bitmap.Width > page.Width + 350 && bitmap.Height > page.Height * 2,"Notes were not laid out beyond the source page.");
        Equal(before,DocumentStore.Serialize(Document(state.Window)));

        var source = new FixedDocument(); source.Pages.Add(new PageContent { Child = new FixedPage { Width = 600, Height = 800 } });
        var presentation = typeof(Inklane.MainWindow).Assembly.GetType("Inklane.ReviewPresentation")!;
        var paginator = (DocumentPaginator)presentation.GetMethod("AppendNotes",BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [source.DocumentPaginator,new[] {(1,page)},new Size(600,800),new Rect(20,20,560,760)])!;
        Assert(paginator.PageCount > 2,"Long remarks were not paginated for printing.");
        var xps = state.Path("complete-remarks.xps");
        using(var spool = new XpsDocument(xps,FileAccess.Write)) XpsDocument.CreateXpsDocumentWriter(spool).Write(paginator);
        using(var zip = ZipFile.OpenRead(xps))
        {
            var text = string.Join(" ",zip.Entries.Where(entry=>entry.FullName.EndsWith(".fpage",StringComparison.OrdinalIgnoreCase)).SelectMany(entry=>
            { using var input = entry.Open(); return XDocument.Load(input).Descendants().Where(node=>node.Name.LocalName=="Glyphs").Select(node=>(string?)node.Attribute("UnicodeString") ?? "").ToArray(); }));
            Assert(text.Contains("LAST EXPLANATION REMAINS VISIBLE"),"Printed output omitted the end of a long collapsed remark.");
            Assert(text.Contains("Keep the route clear") && text.Contains("Complete explanation"),"Printed output lost rich content or remark titles.");
        }
    }
}
