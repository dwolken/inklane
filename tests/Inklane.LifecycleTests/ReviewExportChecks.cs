using System.IO;
using Inklane.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void StandardPdfReviewExport()
    {
        using var state=new WindowCase("standard-pdf-export");
        var original=state.Path("source.pdf");
        using(var pdf=new PdfDocument())
        {
            foreach(int rotation in new[]{0,90,180,270})
            {
                var page=pdf.AddPage();page.Width=XUnit.FromPoint(400);page.Height=XUnit.FromPoint(500);
                using(var g=XGraphics.FromPdfPage(page)){g.DrawRectangle(XBrushes.LightBlue,0,0,400,500);g.DrawString("Original",new XFont("Segoe UI",22),XBrushes.Black,100,180);}
                page.CropBox=new PdfRectangle(new XPoint(20,30),new XPoint(380,470));page.Rotate=rotation;
            }
            pdf.Save(original);
        }
        var hash=DocumentStore.GetFileHash(original);state.Window.OpenFile(original);
        var document=Document(state.Window);
        foreach(var page in document.Pages)
        {
            page.Ink.Add(new InkData {Color="#D03040",Width=8,Points=[new(){X=40,Y=60},new(){X=180,Y=90}]});
            page.Items.Add(new TextItem {X=70,Y=110,IsNote=true,Title="Long explanation",Text=string.Concat(Enumerable.Repeat("Änderung → preserve every paragraph.\n",100))});
            page.Items.Add(new TextItem {X=45,Y=160,Width=250,Height=70,Text="Aligned review text"});
        }
        Call(state.Window,"ShowPage",false);Call(state.Window,"DocumentChanged");
        var destination=state.Path("review.pdf");state.Interaction.SavePaths.Enqueue(destination);Call(state.Window,"ExportAnnotatedPdf");
        Assert(File.Exists(destination),"PDF export failed: "+string.Join("; ",state.Interaction.Messages.Select(m=>m.Text)));
        Equal(hash,DocumentStore.GetFileHash(original));
        using(var result=PdfReader.Open(destination,PdfDocumentOpenMode.Import))
        {
            Equal(4,result.PageCount);
            for(int i=0;i<4;i++)
            {
                var page=result.Pages[i];Equal(i*90,page.Rotate);Equal(3,page.Annotations.Count);
                Equal(document.Pages[i].Items[0].Text,page.Annotations[1].Contents);
                var point=page.Annotations[0].Elements.GetArray("/InkList")!.Elements.GetArray(0)!;
                double x=30,y=Units.DipToPoint(document.Pages[i].Height)-45;
                var expected=i switch {1=>new XPoint(380-y,30+x),2=>new XPoint(380-x,470-y),3=>new XPoint(20+y,470-x),_=>new XPoint(20+x,30+y)};
                Assert(Math.Abs(point.Elements.GetReal(0)-expected.X)<.01 && Math.Abs(point.Elements.GetReal(1)-expected.Y)<.01,"Cropped/rotated ink coordinates drifted.");
            }
        }
        state.Interaction.Choices.Enqueue(1);state.Window.OpenFile(destination);
        Equal(DocumentKind.Pdf,Document(state.Window).Kind);Equal(4,Document(state.Window).Pages.Count);
        // Source modification and destination protection must fail without replacing either file.
        var saved=DocumentStore.GetFileHash(destination);
        try{DocumentStore.ExportReviewPdf(document,original,original);throw new Exception("Source overwrite allowed.");}catch(IOException){}
        File.AppendAllText(original,"\n% revision");
        try{DocumentStore.ExportReviewPdf(document,original,destination);throw new Exception("Stale source accepted.");}catch(InvalidDataException){}
        Equal(saved,DocumentStore.GetFileHash(destination));
    }
}
