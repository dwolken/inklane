using System.Windows;
using System.Windows.Media;

namespace Inklane.Core;

public static class ShapeGeometry
{
    public static void Detach(PageData page, ShapeItem shape)
    {
        var (start,end) = Endpoints(page,shape);
        shape.X = Math.Min(start.X,end.X); shape.Y = Math.Min(start.Y,end.Y);
        shape.Width = Math.Max(1,Math.Abs(end.X-start.X)); shape.Height = Math.Max(1,Math.Abs(end.Y-start.Y));
        shape.FlipX = start.X > end.X; shape.FlipY = start.Y > end.Y;
        shape.StartId = shape.EndId = null; shape.Rotation = 0;
    }

    public static (Point Start, Point End) Endpoints(PageData page, ShapeItem shape)
    {
        var start = new Point(shape.X + (shape.FlipX ? shape.Width : 0), shape.Y + (shape.FlipY ? shape.Height : 0));
        var end = new Point(shape.X + (shape.FlipX ? 0 : shape.Width), shape.Y + (shape.FlipY ? 0 : shape.Height));
        var a = page.Shapes.FirstOrDefault(s => s.Id == shape.StartId);
        var b = page.Shapes.FirstOrDefault(s => s.Id == shape.EndId);
        if (a != null) start = Center(a);
        if (b != null) end = Center(b);
        var origin = start;
        if (a != null) start = Edge(a, end);
        if (b != null) end = Edge(b, origin);
        return (start, end);
    }

    public static Rect Bounds(PageData page, ShapeItem shape) => Create(page, shape).GetRenderBounds(new Pen(Brushes.Black, Math.Max(.1, shape.StrokeWidth)));

    public static Point Center(ShapeItem shape) => new(shape.X + shape.Width / 2, shape.Y + shape.Height / 2);

    private static Point Edge(ShapeItem shape, Point toward)
    {
        var center = Center(shape);
        var inverse = new RotateTransform(-shape.Rotation, center.X, center.Y);
        var d = inverse.Transform(toward) - center;
        if (d.LengthSquared < .001) return center;
        var x = Math.Abs(d.X) / (shape.Width / 2);
        var y = Math.Abs(d.Y) / (shape.Height / 2);
        var scale = shape.Kind == ShapeKind.Ellipse ? 1 / Math.Sqrt(x * x + y * y)
            : shape.Kind == ShapeKind.Diamond ? 1 / (x + y) : 1 / Math.Max(x, y);
        return new RotateTransform(shape.Rotation, center.X, center.Y).Transform(center + d * scale);
    }

    public static Geometry Create(PageData page, ShapeItem shape)
    {
        Geometry geometry;
        var rect = new Rect(shape.X, shape.Y, shape.Width, shape.Height);
        if (shape.Kind == ShapeKind.Rectangle) geometry = new RectangleGeometry(rect, 4, 4);
        else if (shape.Kind == ShapeKind.Ellipse) geometry = new EllipseGeometry(rect);
        else
        {
            var path = new StreamGeometry();
            using (var ctx = path.Open())
            {
                if (shape.Kind == ShapeKind.Diamond)
                {
                    ctx.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Top), true, true);
                    ctx.PolyLineTo([new Point(rect.Right, rect.Top + rect.Height / 2), new Point(rect.Left + rect.Width / 2, rect.Bottom), new Point(rect.Left, rect.Top + rect.Height / 2)], true, false);
                }
                else
                {
                    var (start, end) = Endpoints(page, shape);
                    ctx.BeginFigure(start, false, false); ctx.LineTo(end, true, false);
                    if (shape.Kind == ShapeKind.Arrow)
                    {
                        var direction = end - start;
                        if (direction.Length > .01)
                        {
                            direction.Normalize(); var side = new Vector(-direction.Y, direction.X);
                            var size = Math.Max(10, shape.StrokeWidth * 4);
                            ctx.BeginFigure(end - direction * size + side * size * .45, false, false);
                            ctx.LineTo(end, true, false);
                            ctx.LineTo(end - direction * size - side * size * .45, true, false);
                        }
                    }
                }
            }
            geometry = path;
        }
        if (shape.Rotation != 0 && shape.StartId == null && shape.EndId == null)
            geometry.Transform = new RotateTransform(shape.Rotation, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        geometry.Freeze();
        return geometry;
    }
}
