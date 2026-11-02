using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

// Request one rendered page at a time. Source pages may have different physical sizes.
internal sealed class PdfMarkupPaginator(Window owner, DocumentData document, PdfBackdrop backdrop,
    Size sheet, Rect printable, int firstPage, int count) : DocumentPaginator
{
    public override bool IsPageCountValid => true;
    public override int PageCount => count;
    public override Size PageSize { get; set; } = sheet;
    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= count) return DocumentPage.Missing;
        if (!owner.Dispatcher.CheckAccess()) return owner.Dispatcher.Invoke(() => GetPage(pageNumber));
        var index = firstPage + pageNumber;
        var page = document.Pages[index];
        var renderer = new DocumentSurface { Translate = L.T };
        renderer.LoadPage(page,backdrop.Render(owner,index));
        var dpi = Math.Clamp((int)Math.Floor(96 * Math.Sqrt(8_000_000 / (page.Width * page.Height))),1,150);
        var bitmap = renderer.RenderPage(dpi);
        var scale = Math.Min(1,Math.Min(printable.Width/page.Width,printable.Height/page.Height));
        double width = page.Width * scale, height = page.Height * scale;
        var bounds = new Rect(printable.X + (printable.Width-width)/2,printable.Y + (printable.Height-height)/2,width,height);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White,null,new Rect(PageSize));
            drawing.DrawImage(bitmap,bounds);
        }
        return new DocumentPage(visual,PageSize,new Rect(PageSize),bounds);
    }
}
