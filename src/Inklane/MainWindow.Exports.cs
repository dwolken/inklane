using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Inklane.Controls;
using Inklane.Core;
using Microsoft.Win32;

namespace Inklane;

public sealed partial class MainWindow
{
    private void AddDocumentExports(ItemsControl menu)
    {
        var pdf=new MenuItem { Header=WorkflowText.T("ExportPdf"),ToolTip=WorkflowText.T("PdfExportInfo") };
        pdf.Click+=(_,_)=>ExportAnnotatedPdf();menu.Items.Add(pdf);
        if(_activeOfficeReview!=null && SourcePath!=null && SamePath(SourcePath,_activeOfficeReview.PdfPath))
        {
            var office=new MenuItem {Header=WorkflowText.T("ExportOffice"),ToolTip=WorkflowText.T("OfficeExportInfo")};
            office.Click+=(_,_)=>ExportOfficeReview();menu.Items.Add(office);
        }
    }
    private void ExportAnnotatedPdf()
    {
        if(_saving)return;
        FinishRenameNotebook(true);_surface.CommitEdits();if(!CaptureHistory())return;
        var dialog=new SaveFileDialog {Title=WorkflowText.T("ExportPdf"),Filter=L.T("SourcePdfFilter"),DefaultExt=".pdf",AddExtension=true,FileName=ExportStem+"-review.pdf"};
        if(_interaction.ChooseSavePath(this,dialog) is not {} path || !DestinationAllowed(path,".pdf",true))return;
        try { DocumentStore.ExportReviewPdf(_document,SourcePath??"",path); }
        catch(Exception ex) when(IsExpected(ex)){Error(L.T("SaveFailed"),ex);}
    }
    private void ExportOfficeReview()
    {
        if(_saving || _activeOfficeReview==null || _pdf==null)return;
        FinishRenameNotebook(true);_surface.CommitEdits();if(!CaptureHistory())return;
        var source=_activeOfficeReview;var ext=Path.GetExtension(source.OfficePath);
        var dialog=new SaveFileDialog {Title=WorkflowText.T("ExportOffice"),Filter=$"Office|*{ext}",DefaultExt=ext,AddExtension=true,FileName=Path.GetFileNameWithoutExtension(source.OfficePath)+"-review"+ext};
        if(_interaction.ChooseSavePath(this,dialog) is not {} path || !DestinationAllowed(path,ext,true))return;
        if(SamePath(path,source.OfficePath)){Error(L.T("ProtectOriginal"));return;}
        try
        {
            AtomicWrite(path,stream=>OfficeReviewExporter.Write(stream,source,_document,index=>
            {
                var page=_document.Pages[index];var surface=new DocumentSurface {Translate=L.T};
                surface.LoadPage(page,_pdf.Render(this,index));
                int dpi=Math.Clamp((int)Math.Floor(96*Math.Sqrt(4_000_000/(page.Width*page.Height))),1,120);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(surface.RenderPage(dpi)));
                using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
            }));
            _interaction.ShowMessage(this,WorkflowText.T("OfficeExportInfo"),WorkflowText.T("ExportOffice"));
        }
        catch(Exception ex) when(IsExpected(ex) || ex is System.Xml.XmlException){Error(L.T("SaveFailed"),ex);}
    }
}
