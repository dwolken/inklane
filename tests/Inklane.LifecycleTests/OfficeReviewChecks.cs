using System.IO;
using System.IO.Compression;
using Inklane.Core;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void OfficeReviewJourney()
    {
        foreach (var ext in new[] { ".docx", ".pptx" })
        {
            using var state = new WindowCase("office-" + ext[1..]);
            string office = state.Path("briefing" + ext), pdf = state.Path("export.pdf");
            // Real Office exports can run through this same journey without requiring Office in CI.
            var external = Environment.GetEnvironmentVariable("INKLANE_TEST_OFFICE_FIXTURES");
            if (external != null)
            {
                var original = Directory.GetFiles(external, "*" + ext).Single();
                File.Copy(original,office,true); File.Copy(Path.ChangeExtension(original,".pdf"),pdf,true);
            }
            else
            {
                using (var package = ZipFile.Open(office, ZipArchiveMode.Create))
                {
                    package.CreateEntry("[Content_Types].xml");
                    package.CreateEntry(ext == ".docx" ? "word/document.xml" : "ppt/presentation.xml");
                }
                CreateSourcePdf(pdf);
            }
            var w = state.Window;
            SetField(w,"_officeRenderer",null!); // Exercise the manual export workflow on any host.
            string officeHash = DocumentStore.GetFileHash(office), pdfHash = DocumentStore.GetFileHash(pdf);
            state.Interaction.Choices.Enqueue(0); state.Interaction.ReviewPdfs.Enqueue(null);
            w.OpenFile(office); Equal(DocumentKind.Notebook,Document(w).Kind);
            state.Interaction.Choices.Enqueue(0); state.Interaction.ReviewPdfs.Enqueue(pdf);
            w.OpenFile(office);
            Equal(DocumentKind.Pdf,Document(w).Kind); Equal(2,Document(w).Pages.Count);
            AddText(w,"Observe the threshold."); Assert(Call<bool>(w,"Save",false),"Failed to save review.");
            Equal(office,Settings.Current.RecentFiles[0]);
            var savedReview = State(w);
            Settings.Load(); w.OpenFile(office);
            Equal(savedReview,State(w));
            Equal(officeHash,DocumentStore.GetFileHash(office)); Equal(pdfHash,DocumentStore.GetFileHash(pdf));
            using (var edit = ZipFile.Open(office,ZipArchiveMode.Update)) edit.CreateEntry("revision.xml");
            var before = State(w);
            var count = state.Interaction.Messages.Count;
            state.Interaction.Choices.Enqueue(0); state.Interaction.ReviewPdfs.Enqueue(pdf);
            w.OpenFile(office);
            Equal(count + 1,state.Interaction.Messages.Count);
            Equal(before,State(w)); Equal(pdfHash,DocumentStore.GetFileHash(pdf));
            Assert(state.Interaction.Messages.Last().Text.Contains(L.T("OfficeOldCopy")), "Outdated copy was not explained.");
            Assert(state.Interaction.ReviewPdfs.Count == 0, "PDF picker was bypassed.");
        }
    }

    private static void PortableImagePair()
    {
        using var state = new WindowCase("portable-image");
        var pair = state.Path("relocated"); Directory.CreateDirectory(pair);
        var cwd = state.Path("unrelated"); Directory.CreateDirectory(cwd);
        var source = Path.Combine(pair,"plan.png"); var sidecar = source + ".inklane.json";
        var fixture = Environment.GetEnvironmentVariable("INKLANE_TEST_BROWSER_SIDECAR");
        if (fixture != null)
        {
            var name = Path.GetFileName(fixture)[..^".inklane.json".Length];
            source = Path.Combine(pair,name); sidecar = source + ".inklane.json";
            File.Copy(Path.Combine(Path.GetDirectoryName(fixture)!,name),source,true); File.Copy(fixture,sidecar,true);
        }
        else
        {
            WriteImage(source);
            var model = new DocumentData { Kind=DocumentKind.Image, SourceImagePath=Path.GetFileName(source),
                SourceImageSha256=DocumentStore.GetFileHash(source), Pages=[new PageData {Width=64,Height=64,
                Items=[new TextItem { X=15, Y=20, IsNote=true, Title="Shaded bench", Text="Check afternoon light." }] }] };
            DocumentStore.SaveJson(model,sidecar);
        }
        var expected = DocumentStore.LoadJson(sidecar);
        File.WriteAllText(Path.Combine(cwd,Path.GetFileName(source)),"wrong bytes");
        var originalDir = Environment.CurrentDirectory;
        try { Environment.CurrentDirectory = cwd; state.Window.OpenFile(sidecar); }
        finally { Environment.CurrentDirectory = originalDir; }
        var actualModel = Document(state.Window);
        Equal(source,actualModel.SourceImagePath);
        expected.SourceImagePath = source;
        Equal(DocumentStore.Serialize(expected),DocumentStore.Serialize(actualModel));
        var hash = DocumentStore.GetFileHash(source);
        AddText(state.Window,"Desktop follow-up"); Assert(Call<bool>(state.Window,"Save",false),"Native save failed.");
        state.Window.OpenFile(source); Equal(hash,DocumentStore.GetFileHash(source));
        Equal("Desktop follow-up",Document(state.Window).Pages[0].Items.Last().Text);
        // A separately saved legacy sidecar can have an unrelated same-named image
        // beside it. The matching local original must still be found.
        var legacy = Path.Combine(cwd,"detached.json");
        DocumentStore.SaveJson(Document(state.Window),legacy);
        state.Window.OpenFile(legacy);
        Equal(legacy,Field<string>(state.Window,"_filePath"));
        Equal(source,Document(state.Window).SourceImagePath);
    }

    private static void DirectOfficeReview()
    {
        var renderer = Environment.GetEnvironmentVariable("INKLANE_TEST_OFFICE_RENDERER")!;
        var samples = Environment.GetEnvironmentVariable("INKLANE_TEST_OFFICE_FIXTURES")
            ?? throw new InvalidOperationException("The real Office fixtures are required for converter checks.");
        foreach (var ext in new[] { ".docx", ".pptx" })
        {
            using var state = new WindowCase("render-office-" + ext[1..]);
            string source = state.Path("review" + ext), target = state.Path("Edition one.pdf");
            File.Copy(Directory.GetFiles(samples,"*" + ext).Single(),source);
            var hash = DocumentStore.GetFileHash(source);
            var w = state.Window;
            SetField(w,"_officeRenderer",renderer);
            state.Interaction.Choices.Enqueue(0); state.Interaction.SavePaths.Enqueue(target);
            w.OpenFile(source);
            Equal(DocumentKind.Pdf,Document(w).Kind); Equal(2,Document(w).Pages.Count);
            var firstPage = Document(w).Pages[0];
            Assert(ext == ".docx" ? firstPage.Height > firstPage.Width : firstPage.Width > firstPage.Height,
                "Page orientation changed during conversion.");
            AddText(w,"Check the east entrance."); Assert(Call<bool>(w,"Save",false),"Converted PDF failed to save markup.");
            var savedReview = State(w);
            w.OpenFile(source); Equal(savedReview,State(w));
            Equal(hash,DocumentStore.GetFileHash(source)); Equal(DocumentStore.GetFileHash(target),Document(w).SourcePdfSha256);
        }
    }
}
