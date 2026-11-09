using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Inklane.Core;

public static partial class DocumentStore
{
    /// <summary>Write standard annotations onto a copy, retaining source page objects and resources.</summary>
    public static void ExportReviewPdf(DocumentData model, string sourcePath, string destination)
    {
        Validate(model);
        if (model.Kind != DocumentKind.Pdf) { SavePdf(model,destination); return; }
        if (string.Equals(Path.GetFullPath(sourcePath),Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))
            throw new IOException("The original PDF cannot be replaced by an export.");
        using var input = File.OpenRead(sourcePath);
        if (input.Length > MaxPdfBytes || !string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)),model.SourcePdfSha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original PDF has changed since this review was opened.");
        input.Position = 0;
        using var pdf = PdfReader.Open(input,PdfDocumentOpenMode.Modify,_ => throw EncryptedPdf());
        if (pdf.SecuritySettings.IsEncrypted || pdf.Internals.GetAllObjects().Any(o => o is PdfSharp.Pdf.Security.PdfStandardSecurityHandler)) throw EncryptedPdf();
        if (pdf.PageCount != model.Pages.Count) throw new InvalidDataException("PDF page count does not match the review.");
        // A reviewed notebook is an external source here. Retaining its old embedded
        // editor marker would falsely promise editable reopening of the added review.
        foreach (var key in pdf.Internals.Catalog.Elements.Keys.Where(k => k.StartsWith("/Inklane",StringComparison.Ordinal)).ToArray())
            pdf.Internals.Catalog.Elements.Remove(key);
        if (pdf.Info.Creator == "Inklane") pdf.Info.Creator = "Inklane review export";
        for (int index=0;index<model.Pages.Count;index++)
        {
            var original = pdf.Pages[index]; var pageModel = model.Pages[index];
            var media = original.MediaBox; var crop = original.CropBox;
            if (crop.Width <= 0 || crop.Height <= 0) crop = media;
            double left=Math.Max(media.X1,crop.X1), bottom=Math.Max(media.Y1,crop.Y1),
                right=Math.Min(media.X2,crop.X2), top=Math.Min(media.Y2,crop.Y2);
            int rotation = ((original.Rotate % 360)+360)%360;
            if (rotation % 90 != 0 || original.Elements.GetReal("/UserUnit") is not (0 or 1))
                throw new NotSupportedException("This PDF page uses unsupported coordinate scaling. Use Print to PDF for this document.");
            double width=right-left,height=top-bottom;
            if (Math.Abs(Units.DipToPoint(pageModel.Width)-(rotation % 180 == 0 ? width : height))>.1 ||
                Math.Abs(Units.DipToPoint(pageModel.Height)-(rotation % 180 == 0 ? height : width))>.1)
                throw new InvalidDataException("PDF page geometry does not match the review.");
            XPoint Map(double x,double y) => rotation switch
            {
                90 => new(right-y,bottom+x), 180 => new(right-x,top-y),
                270 => new(left+y,top-x), _ => new(left+x,bottom+y)
            };
            var overlay=pdf.AddPage();
            overlay.Width=XUnit.FromPoint(Units.DipToPoint(pageModel.Width));
            overlay.Height=XUnit.FromPoint(Units.DipToPoint(pageModel.Height));
            foreach (var picture in pageModel.Images)
            {
                double x=Units.DipToPoint(picture.X),y=Units.DipToPoint(picture.Y),w=Units.DipToPoint(picture.Width),h=Units.DipToPoint(picture.Height);
                var stamp=new StandardAnnotation(pdf,"/Stamp") { Rectangle=new PdfRectangle(new XRect(x,overlay.Height.Point-y-h,w,h)) };
                stamp.Elements.SetString("/NM",picture.Id);stamp.Elements.SetInteger("/F",4);
                SetAppearance(pdf,stamp,w,h,g => { using var bytes=new MemoryStream(picture.PngData,0,picture.PngData.Length,false,true);using var image=XImage.FromStream(bytes);g.DrawImage(image,0,0,w,h); });
                overlay.Annotations.Add(stamp);
            }
            foreach (var stroke in pageModel.Ink) AddInk(pdf,overlay,stroke);
            foreach (var item in pageModel.Items) AddText(pdf,overlay,item);
            while (overlay.Annotations.Count>0)
            {
                var annotation=overlay.Annotations[0];var rect=annotation.Rectangle;
                var corners=new[] {Map(rect.X1,rect.Y1),Map(rect.X2,rect.Y1),Map(rect.X1,rect.Y2),Map(rect.X2,rect.Y2)};
                annotation.Rectangle=new PdfRectangle(new XRect(corners.Min(p=>p.X),corners.Min(p=>p.Y),corners.Max(p=>p.X)-corners.Min(p=>p.X),corners.Max(p=>p.Y)-corners.Min(p=>p.Y)));
                if (annotation.Elements.GetArray("/InkList") is { } paths)
                    foreach (PdfArray path in paths.Elements)
                        for(int p=0;p<path.Elements.Count;p+=2)
                        { var point=Map(path.Elements.GetReal(p),path.Elements.GetReal(p+1));path.Elements[p]=new PdfReal(point.X);path.Elements[p+1]=new PdfReal(point.Y); }
                if (rotation!=0 && annotation.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N") is { } appearance)
                {
                    double[] values=rotation switch {90=>[0,1,-1,0,rect.Height,0],180=>[-1,0,0,-1,rect.Width,rect.Height],_=>[0,-1,1,0,0,rect.Width]};
                    appearance.Elements["/Matrix"]=new PdfArray(pdf,values.Select(v=>(PdfItem)new PdfReal(v)).ToArray());
                }
                overlay.Annotations.Remove(annotation);original.Annotations.Add(annotation);
            }
            pdf.Pages.Remove(overlay);
        }
        AtomicWrite(destination,temp=> { pdf.Save(temp);CheckSize(temp,MaxPdfBytes); });
    }
}
