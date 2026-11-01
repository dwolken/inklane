using System.Windows;
using System.Windows.Media;
using Inklane.Core;

namespace Inklane.Controls;

internal sealed class PaperPreview : FrameworkElement
{
    private double _widthMm = 210, _heightMm = 297, _spacing = 5, _margin = 15;
    private PaperPattern _pattern;

    public void SetPaper(double width, double height, PaperPattern pattern, double spacing, double margin)
    {
        (_widthMm,_heightMm,_pattern,_spacing,_margin) = (width,height,pattern,spacing,margin);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min((ActualWidth - 20) / _widthMm,(ActualHeight - 14) / _heightMm);
        if (scale <= 0) return;
        var sheet = new Rect((ActualWidth - _widthMm * scale)/2,(ActualHeight - _heightMm * scale)/2,_widthMm * scale,_heightMm * scale);
        dc.DrawRectangle(Brushes.White,new Pen(new SolidColorBrush(Color.FromRgb(198,195,200)),1),sheet);
        if (_pattern == PaperPattern.Blank) return;
        double inset = _margin * scale, spacing = _spacing * scale;
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(211,209,217)),.65);
        for (double y = sheet.Top+inset; y <= sheet.Bottom-inset; y += spacing)
            dc.DrawLine(pen,new Point(sheet.Left+inset,y),new Point(sheet.Right-inset,y));
        if (_pattern == PaperPattern.Squared)
            for (double x = sheet.Left+inset; x <= sheet.Right-inset; x += spacing)
                dc.DrawLine(pen,new Point(x,sheet.Top+inset),new Point(x,sheet.Bottom-inset));
    }
}
