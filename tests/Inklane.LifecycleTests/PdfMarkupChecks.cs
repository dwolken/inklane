using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using Inklane.Controls;
using Inklane.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void CreateSourcePdf(string file)
    {
        using var pdf = new PdfDocument();
        for (int i = 0; i < 2; i++)
        {
            var page = pdf.AddPage();
            page.Width = XUnit.FromPoint(300); page.Height = XUnit.FromPoint(400);
            using (var g = XGraphics.FromPdfPage(page))
            {
                g.DrawRectangle(i == 0 ? XBrushes.LightBlue : XBrushes.LightGreen,0,0,300,400);
                g.DrawRectangle(XBrushes.Magenta,20,60,36,40);
                g.DrawString(i == 0 ? "Elevations" : "Survey detail", new XFont("Segoe UI",18), XBrushes.Black,55,155);
            }
            if (i == 1)
            {
                page.CropBox = new PdfRectangle(new XPoint(20,30),new XPoint(280,340));
                page.Rotate = 90;
            }
        }
        pdf.Save(file);
    }

    private static byte[] Pixel(BitmapSource source, double x, double y)
    {
        var bmp = new FormatConvertedBitmap(source,PixelFormats.Bgr24,null,0);
        var bytes = new byte[3];
        bmp.CopyPixels(new Int32Rect((int)(x*bmp.PixelWidth),(int)(y*bmp.PixelHeight),1,1),bytes,3,0);
        return bytes;
    }

    private static void PdfMarkupJourney()
    {
        using var state = new WindowCase("pdf-journey");
        var file = state.Path("survey.pdf"); CreateSourcePdf(file);
        string original = DocumentStore.GetFileHash(file);
        var w = state.Window; w.OpenFile(file);
        Equal(DocumentKind.Pdf,Document(w).Kind); Equal(2,Document(w).Pages.Count);
        Equal(3,Document(w).SchemaVersion); Assert(!Dirty(w),"Reading the source created changes.");
        var surface = Field<DocumentSurface>(w,"_surface");
        Equal(PageViewMode.Editing,surface.ViewMode);
        Assert(Field<Button>(w,"_addPage").Visibility == Visibility.Collapsed,"Source PDF offered page insertion.");
        var first = Pixel(surface.RenderPage(),.6,.7);
        Assert(first[0] > first[2] + 25,"First source page was not rendered.");
        Call(w,"Navigate",1);
        var p = Document(w).Pages[1];
        Assert(Math.Abs(p.Width - 310*96d/72) < .02 && Math.Abs(p.Height - 260*96d/72) < .02,
            $"Rotated crop expected 413.33 × 346.67 DIPs, got {p.Width} × {p.Height}.");
        var second = surface.RenderPage();
        var green = Pixel(second,.6,.7);
        Assert(green[1] > green[0] + 50,"Second source page was not rendered.");
        var magenta = Pixel(second,.95,.07);
        Assert(magenta[0] > 220 && magenta[1] < 40 && magenta[2] > 220,"Rotated, cropped source landmark is misplaced.");
        var note = new TextItem { IsNote = true, IsExpanded = false, X = 334, Y = 19, Title = "Field joint", Text = "Review clearance.\nConfirm at site." };
        p.Items.Add(note); Call(w,"ShowPage",false); Call(w,"DocumentChanged");
        state.Interaction.SavePaths.Enqueue(state.Path("marked.png")); Call(w,"ExportImage");
        var pin = BitmapFrame.Create(new Uri(state.Path("marked.png")),BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
        var pinColor = Pixel(pin,(note.X+4) * pin.DpiX / 96 / pin.PixelWidth,(note.Y+9) * pin.DpiY / 96 / pin.PixelHeight);
        Assert(pinColor[2] > 230 && pinColor[0] < 195,"Export did not render the note at its page coordinates.");
        Assert(Call<bool>(w,"Save",false),"Could not save PDF sidecar.");
        var sidecar = file+".inklane.json"; var markup = DocumentStore.LoadJson(sidecar);
        Equal(original,markup.SourcePdfSha256); Equal("survey.pdf",markup.SourcePdfName);
        Assert(!File.ReadAllText(sidecar).Contains(state.Path(""),StringComparison.OrdinalIgnoreCase),"Sidecar contains an absolute folder path.");
        w.OpenFile(sidecar); Call(w,"GoToPage",1);
        var reopened = Document(w).Pages[1].Items.Single();
        Equal(note.Id,reopened.Id); Equal(note.Title,reopened.Title); Equal(note.Text,reopened.Text); Equal(note.X,reopened.X); Equal(note.Y,reopened.Y);
        Assert(Call<bool>(w,"JumpToNote",1,note.Id),"Reopened annotation could not be expanded.");
        Assert(!Dirty(w),"Reading a pinned note changed saved state.");
        var paginator = (DocumentPaginator)Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("Inklane.PdfMarkupPaginator")!,
            w,Document(w),Field<object>(w,"_pdf"),new Size(600,800),new Rect(10,10,580,780),0,2)!;
        var xps = state.Path("printed.xps");
        using (var spool = new XpsDocument(xps,FileAccess.Write))
            XpsDocument.CreateXpsDocumentWriter(spool).Write(paginator);
        using (var spool = new XpsDocument(xps,FileAccess.Read))
        {
            var printed = spool.GetFixedDocumentSequence().DocumentPaginator;
            printed.ComputePageCount(); Equal(2,printed.PageCount);
            for (int i = 0; i < 2; i++)
            {
                using var printedPage = printed.GetPage(i);
                var raster = new RenderTargetBitmap(600,800,96,96,PixelFormats.Pbgra32);
                raster.Render(printedPage.Visual);
                var color = Pixel(raster,.6,.55);
                Assert(i==0 ? color[0]>color[2]+25 : color[1]>color[0]+50,"Print pagination lost the source or reused a previous page.");
            }
        }
        Equal(original,DocumentStore.GetFileHash(file));
    }

    private static void PdfMarkupRecovery()
    {
        using var state = new WindowCase("pdf-recovery");
        var file = state.Path("base.pdf"); CreateSourcePdf(file);
        var window = state.Window; window.OpenFile(file); Call(window,"GoToPage",1);
        Document(window).Pages[1].Items.Add(new TextItem { IsNote=true, Title="Repair", Text="Unsaved field note", X=76,Y=83 });
        Call(window,"ShowPage",false); Call(window,"DocumentChanged");
        Assert(Call<bool>(window,"WriteRecovery"),"PDF recovery was not written.");
        var dir = Path.GetDirectoryName(Field<string>(window,"_recoveryPath"))!;
        using var other = new WindowCase("restored-pdf");
        SetField(other.Window,"_recoveryPath",Path.Combine(dir,"new-session.json"));
        other.Interaction.Choices.Enqueue(0); other.Window.OfferRecovery();
        Equal(DocumentKind.Pdf,Document(other.Window).Kind); Equal(1,Field<int>(other.Window,"_pageIndex"));
        Equal("Repair",Document(other.Window).Pages[1].Items.Single().Title);
        Assert(Dirty(other.Window),"Restored markup is unexpectedly saved.");
        var color = Pixel(Field<DocumentSurface>(other.Window,"_surface").RenderPage(),.6,.7);
        Assert(color[1] > color[0]+50,"Recovery displayed the wrong PDF source page.");
    }

    private static void PdfMarkupGuards()
    {
        using var state = new WindowCase("pdf-guards");
        var file = state.Path("letter.pdf"); CreateSourcePdf(file);
        var w = state.Window; w.OpenFile(file); AddText(w,"Attached text");
        Assert(Call<bool>(w,"Save",false),"Could not save setup markup.");
        var sidecar = file+".inklane.json"; var good = File.ReadAllBytes(sidecar);
        var wrong = DocumentStore.LoadJson(sidecar); wrong.Pages[1].Height += 10; DocumentStore.SaveJson(wrong,sidecar);
        int errors = state.Interaction.Messages.Count;
        w.OpenFile(file);
        Equal(errors+1,state.Interaction.Messages.Count);
        Equal(L.T("PdfGeometry"),state.Interaction.Messages.Last().Text);
        Assert(Math.Abs(Document(w).Pages[1].Height-wrong.Pages[1].Height)>5,"Incorrect page geometry was accepted.");
        File.WriteAllText(sidecar,"not annotation data");
        w.OpenFile(file);
        Equal(L.T("AnnotationDataInvalid"),state.Interaction.Messages.Last().Text);
        File.WriteAllBytes(sidecar,good);
        File.AppendAllText(file,"\n% external edit");
        w.OpenFile(file);
        Equal(L.T("PdfChanged"),state.Interaction.Messages.Last().Text);
        AddText(w,"new unsaved observation");
        Assert(!Call<bool>(w,"Save",false) && good.SequenceEqual(File.ReadAllBytes(sidecar)) && Dirty(w),"Changed PDF source allowed destructive save.");
        var native = state.Path("native.pdf"); DocumentStore.SavePdf(Notebook("Protected"),native);
        var damaged = state.Path("damaged.pdf");
        using (var pdf = PdfReader.Open(native,PdfDocumentOpenMode.Modify))
        { pdf.Internals.Catalog.Elements.Remove("/InklaneFormat"); pdf.Save(damaged); }
        state.Interaction.Choices.Enqueue(1);
        errors = state.Interaction.Messages.Count; w.OpenFile(damaged);
        Assert(state.Interaction.Messages.Count > errors && Document(w).SourcePdfName=="letter.pdf" && Dirty(w),"Damaged notebook was opened as a new source.");
        Equal(L.T("NotebookPdfUnsupported"),state.Interaction.Messages.Last().Text);
    }

    private static void OpenFailureMessages()
    {
        using var state = new WindowCase("open-diagnostics");
        var baseline = State(state.Window);
        var encrypted = state.Path("protected.pdf");
        using (var pdf = new PdfDocument())
        {
            pdf.AddPage();
            pdf.SecuritySettings.UserPassword = "test-only-password";
            pdf.Save(encrypted);
        }
        foreach (var (file,key) in new[]
        {
            ("protected.pdf","PdfEncrypted"), ("broken.pdf","PdfUnreadable"),
            ("broken.png","ImageUnreadable"), ("broken.docx","OfficeInvalid"),
            ("unsupported.txt","OpenUnsupportedFormat"), ("missing.pdf","OpenMissing")
        })
        {
            var path = state.Path(file);
            if (file.StartsWith("broken") || file == "unsupported.txt") File.WriteAllText(path,"invalid fixture");
            var count = state.Interaction.Messages.Count;
            state.Window.OpenFile(path);
            Equal(count+1,state.Interaction.Messages.Count);
            Equal(L.T(key),state.Interaction.Messages.Last().Text);
            Equal(baseline,State(state.Window));
        }
    }

    private static void DetachedPdfSidecar()
    {
        using var state = new WindowCase("pdf-save-as");
        var file = state.Path("survey.pdf"); CreateSourcePdf(file);
        var window = state.Window; window.OpenFile(file);
        AddText(window,"A portable observation");
        var sidecar = Path.Combine(Directory.CreateDirectory(state.Path("reviews")).FullName,"inspection.inklane.json");
        state.Interaction.SavePaths.Enqueue(sidecar);
        Assert(Call<bool>(window,"Save",true),"Could not save annotations separately.");
        Assert(!File.Exists(file+".inklane.json"),"Save As wrote an unexpected neighboring sidecar.");
        var notebook = state.Path("design.pdf"); DocumentStore.SavePdf(Notebook("Keep this view"),notebook);
        window.OpenFile(notebook);
        state.Interaction.ReviewPdfs.Enqueue(null); window.OpenFile(sidecar);
        Equal(DocumentKind.Notebook,Document(window).Kind);
        state.Interaction.ReviewPdfs.Enqueue(file); window.OpenFile(sidecar);
        Equal(DocumentKind.Pdf,Document(window).Kind);
        Equal("A portable observation",Document(window).Pages[0].Items.Single().Text);
        Equal(sidecar,Field<string>(window,"_filePath"));
    }
}
