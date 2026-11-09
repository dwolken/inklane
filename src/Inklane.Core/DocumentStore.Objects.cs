using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using System.Windows.Media;

namespace Inklane.Core;

public static partial class DocumentStore
{
    private static void AddPicture(PdfDocument pdf, PdfPage page, ImageItem picture)
    {
        var bounds = new RotateTransform(picture.Rotation,picture.X+picture.Width/2,picture.Y+picture.Height/2).TransformBounds(new System.Windows.Rect(picture.X,picture.Y,picture.Width,picture.Height));
        var left = Units.DipToPoint(bounds.X); var top = Units.DipToPoint(bounds.Y);
        var width = Units.DipToPoint(bounds.Width); var height = Units.DipToPoint(bounds.Height);
        var imageWidth = Units.DipToPoint(picture.Width); var imageHeight = Units.DipToPoint(picture.Height);
        var annotation = new StandardAnnotation(pdf, "/Stamp")
        {
            Rectangle = new PdfRectangle(new XRect(left, page.Height.Point - top - height, width, height)), Title = "Inklane"
        };
        annotation.Elements.SetString("/NM", picture.Id); annotation.Elements.SetInteger("/F", 4);
        annotation.Elements.SetReal("/CA", picture.Opacity);
        SetAppearance(pdf, annotation, width, height, graphics =>
        {
            using var bytes = new System.IO.MemoryStream(picture.PngData, 0, picture.PngData.Length, false, true);
            using var image = XImage.FromStream(bytes);
            graphics.TranslateTransform(Units.DipToPoint(picture.X)-left,Units.DipToPoint(picture.Y)-top);
            graphics.RotateAtTransform(picture.Rotation, new XPoint(imageWidth / 2, imageHeight / 2));
            graphics.DrawImage(image, 0, 0, imageWidth, imageHeight);
        });
        page.Annotations.Add(annotation);
    }

    private static void AddObjects(PdfDocument pdf, PdfPage page, PageData model, bool includeImages)
    {
        foreach (var item in PageObjects.InPaintOrder(model))
            switch (item)
            {
                case ImageItem image when includeImages: AddPicture(pdf, page, image); break;
                case InkData ink: AddInk(pdf, page, ink); break;
                case ShapeItem shape: AddShape(pdf, page, shape, model); break;
                case TextItem text: AddText(pdf, page, text); break;
            }
    }

    private static void DrawRichText(XGraphics graphics, Drawing drawing, Matrix parent, double opacity = 1)
    {
        if (drawing is DrawingGroup group)
        {
            var matrix = group.Transform?.Value ?? Matrix.Identity; matrix.Append(parent);
            foreach (var child in group.Children) DrawRichText(graphics, child, matrix, opacity * group.Opacity);
            return;
        }
        if (drawing is ImageDrawing { ImageSource: DrawingImage image } placed)
        {
            var bounds = image.Drawing.Bounds;
            if (bounds.IsEmpty || bounds.Width == 0 || bounds.Height == 0) return;
            var matrix = Matrix.Identity; matrix.Translate(-bounds.X, -bounds.Y);
            matrix.Scale(placed.Rect.Width / bounds.Width, placed.Rect.Height / bounds.Height);
            matrix.Translate(placed.Rect.X, placed.Rect.Y); matrix.Append(parent);
            DrawRichText(graphics, image.Drawing, matrix, opacity); return;
        }
        Geometry? geometry = null; Brush? fill = null; System.Windows.Media.Pen? stroke = null;
        if (drawing is GeometryDrawing shape) { geometry = shape.Geometry; fill = shape.Brush; stroke = shape.Pen; }
        else if (drawing is GlyphRunDrawing glyph) { geometry = glyph.GlyphRun.BuildGeometry(); fill = glyph.ForegroundBrush; }
        if (geometry == null || fill is SolidColorBrush { Color.A: 0 } && stroke == null) return;
        var flat = geometry.GetFlattenedPathGeometry();
        var transform = flat.Transform?.Value ?? Matrix.Identity; transform.Append(parent);
        var path = new XGraphicsPath { FillMode = flat.FillRule == FillRule.EvenOdd ? XFillMode.Alternate : XFillMode.Winding };
        foreach (var figure in flat.Figures)
        {
            XPoint Point(System.Windows.Point point) { point = transform.Transform(point); return new(Units.DipToPoint(point.X), Units.DipToPoint(point.Y)); }
            var points = new List<XPoint> { Point(figure.StartPoint) };
            foreach (var segment in figure.Segments)
                if (segment is PolyLineSegment poly) points.AddRange(poly.Points.Select(Point));
                else if (segment is LineSegment line) points.Add(Point(line.Point));
            path.StartFigure();
            if (figure.IsClosed && points.Count > 2) path.AddPolygon(points.ToArray());
            else if (points.Count > 1) path.AddLines(points.ToArray());
        }
        XSolidBrush? brush = fill is SolidColorBrush solid ? new XSolidBrush(XColor.FromArgb((int)Math.Round(solid.Color.A * solid.Opacity * opacity), solid.Color.R, solid.Color.G, solid.Color.B)) : null;
        XPen? pen = stroke?.Brush is SolidColorBrush edge ? new XPen(XColor.FromArgb((int)Math.Round(edge.Color.A * edge.Opacity * opacity), edge.Color.R, edge.Color.G, edge.Color.B), Units.DipToPoint(stroke.Thickness)) : null;
        if (brush != null && pen != null) graphics.DrawPath(pen, brush, path);
        else if (brush != null) graphics.DrawPath(brush, path);
        else if (pen != null) graphics.DrawPath(pen, path);
    }

    private static XColor WithOpacity(string color, double opacity)
    {
        var rgb = ParseColor(color);
        return XColor.FromArgb((int)Math.Round(opacity * 255), rgb.R, rgb.G, rgb.B);
    }

    private static void AddShape(PdfDocument pdf, PdfPage page, ShapeItem shape, PageData model)
    {
        var geometry = ShapeGeometry.Create(model, shape).GetFlattenedPathGeometry();
        var bounds = ShapeGeometry.Bounds(model, shape);
        if (shape.Label.Length != 0) bounds.Union(new System.Windows.Rect(shape.X,shape.Y,shape.Width,shape.Height));
        bounds.Inflate(shape.StrokeWidth + 1, shape.StrokeWidth + 1);
        var left = Units.DipToPoint(bounds.Left); var top = Units.DipToPoint(bounds.Top);
        var width = Units.DipToPoint(Math.Max(1, bounds.Width)); var height = Units.DipToPoint(Math.Max(1, bounds.Height));
        var annotation = new StandardAnnotation(pdf, "/Stamp")
        {
            Rectangle = new PdfRectangle(new XRect(left, page.Height.Point - top - height, width, height)),
            Contents = shape.Label, Title = "Inklane"
        };
        annotation.Elements.SetString("/NM", shape.Id);
        annotation.Elements.SetInteger("/F", 4);
        SetAppearance(pdf, annotation, width, height, g =>
        {
            var path = new XGraphicsPath();
            foreach (var figure in geometry.Figures)
            {
                XPoint Point(System.Windows.Point p) => new(Units.DipToPoint(p.X) - left, Units.DipToPoint(p.Y) - top);
                var points = new List<XPoint> { Point(figure.StartPoint) };
                foreach (var segment in figure.Segments)
                    if (segment is PolyLineSegment poly) points.AddRange(poly.Points.Select(Point));
                    else if (segment is LineSegment line) points.Add(Point(line.Point));
                path.StartFigure();
                if (figure.IsClosed && points.Count > 2) path.AddPolygon(points.ToArray());
                else if (points.Count > 1) path.AddLines(points.ToArray());
            }
            var pen = new XPen(WithOpacity(shape.Color, shape.Opacity), Units.DipToPoint(shape.StrokeWidth));
            if (shape.FillColor != null && shape.Kind is not (ShapeKind.Line or ShapeKind.Arrow))
                g.DrawPath(pen, new XSolidBrush(WithOpacity(shape.FillColor, shape.Opacity)), path);
            else g.DrawPath(pen, path);
            if (shape.Label.Length != 0)
            {
                var formatter = new XTextFormatter(g) { Alignment = XParagraphAlignment.Center };
                formatter.DrawString(shape.Label, new XFont("Segoe UI", 12), new XSolidBrush(WithOpacity(shape.Color, shape.Opacity)),
                    new XRect(Units.DipToPoint(shape.X) - left + 5, Units.DipToPoint(shape.Y) - top + 5,
                        Math.Max(1, Units.DipToPoint(shape.Width) - 10), Math.Max(1, Units.DipToPoint(shape.Height) - 10)));
            }
        });
        page.Annotations.Add(annotation);
    }
}
