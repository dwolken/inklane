using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Inklane.Core;

namespace Inklane.Controls;

public sealed partial class DocumentSurface
{
    private readonly Dictionary<string, ShapeVisual> _shapes = [];
    private static readonly Guid StrokeGroup = new("b58c1518-278d-4718-a19a-73e88457570e");
    private static readonly Guid StrokeLayer = new("e369433a-dae5-464d-b26f-9c136623d4a6");
    private static readonly Guid StrokeOpacity = new("b171a380-e7c6-428a-ac50-ef1c9ab3da75");
    private double _opacity = 1;
    public ShapeKind ShapeKind { get; set; } = ShapeKind.Rectangle;
    public string? ShapeFill { get; set; }
    public double InkOpacity { get => _opacity; set { _opacity = Math.Clamp(value, 0, 1); ApplyInkAttributes(); } }
    public event EventHandler? SelectionChanged;
    internal IReadOnlyList<TextItem> SelectedTexts => _page?.Items.Where(i => IncludesObject(i.Id)).ToList() ?? [];
    internal IReadOnlyList<ImageItem> SelectedPictures => _page?.Images.Where(i => IncludesObject(i.Id)).ToList() ?? [];
    internal IReadOnlyList<ShapeItem> SelectedShapes => _page?.Shapes.Where(i => IncludesObject(i.Id)).ToList() ?? [];

    private sealed record ShapeVisual(ShapeItem Item, ShapeDrawing Drawing, Grid Controls, Border Outline, Thumb Resize);

    private sealed class ShapeDrawing(PageData page, ShapeItem shape) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc) => DrawShape(dc, page, shape);
    }

    private static void DrawShape(DrawingContext dc, PageData page, ShapeItem shape)
    {
        var geometry = ShapeGeometry.Create(page, shape);
        var pen = new Pen(new SolidColorBrush(ParseColor(shape.Color)), shape.StrokeWidth)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushOpacity(shape.Opacity);
        dc.DrawGeometry(shape.FillColor == null || shape.Kind is ShapeKind.Line or ShapeKind.Arrow ? null : new SolidColorBrush(ParseColor(shape.FillColor)), pen, geometry);
        if (shape.Label.Length != 0)
        {
            var label = new FormattedText(shape.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 16, new SolidColorBrush(ParseColor(shape.Color)), 1)
            { MaxTextWidth = Math.Max(1, shape.Width - 16), TextAlignment = TextAlignment.Center };
            dc.DrawText(label, new Point(shape.X + 8, shape.Y + 8));
        }
        dc.Pop();
    }

    private void AddShapeVisual(ShapeItem item)
    {
        var drawing = new ShapeDrawing(_page!, item) { Width = _page!.Width, Height = _page.Height, IsHitTestVisible = false };
        var controls = new Grid { Tag = item.Id };
        var outline = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.SlateBlue, IsHitTestVisible = false };
        var move = new Thumb { Cursor = Cursors.SizeAll, Template = HandleTemplate(Brushes.Transparent) };
        AutomationProperties.SetName(move, Translate("MoveItem"));
        AutomationProperties.SetAutomationId(move, "ShapeMove:" + item.Id);
        var resize = new Thumb { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Template = HandleTemplate(Brushes.SlateBlue) };
        AutomationProperties.SetName(resize, Translate("ResizeObject"));
        AutomationProperties.SetAutomationId(resize, "ShapeResize:" + item.Id);
        controls.Children.Add(move); controls.Children.Add(outline); controls.Children.Add(resize);
        _objects.Children.Add(drawing); _objects.Children.Add(controls);
        var visual = new ShapeVisual(item, drawing, controls, outline, resize); _shapes.Add(item.Id, visual);
        var moved = new Vector();
        (string? Start,string? End,double X,double Y,double Width,double Height,bool FlipX,bool FlipY,double Rotation) beforeMove = default;
        move.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ToggleObject(item.Id); e.Handled = true; }
        };
        move.DragStarted += (_, _) => { if (!IncludesObject(item.Id)) SelectObject(item.Id); moved = default;
            beforeMove = (item.StartId,item.EndId,item.X,item.Y,item.Width,item.Height,item.FlipX,item.FlipY,item.Rotation); };
        move.DragDelta += (_, e) => moved += MoveSelection(e.HorizontalChange, e.VerticalChange);
        move.DragCompleted += (_, e) =>
        {
            if (e.Canceled)
            {
                MoveSelection(-moved.X,-moved.Y);
                if (beforeMove.Start != null || beforeMove.End != null)
                {
                    (item.StartId,item.EndId,item.X,item.Y,item.Width,item.Height,item.FlipX,item.FlipY,item.Rotation) = beforeMove;
                    RefreshShapes();
                }
            }
            else if (moved.LengthSquared > 0 || item.StartId != beforeMove.Start || item.EndId != beforeMove.End) SyncInkAndNotify();
        };
        move.KeyDown += (_, e) => e.Handled = NudgeSelection(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        (string? Start,string? End,double X,double Y,double Width,double Height,bool FlipX,bool FlipY,double Rotation) beforeResize = default;
        resize.DragStarted += (_, _) => beforeResize = (item.StartId,item.EndId,item.X,item.Y,item.Width,item.Height,item.FlipX,item.FlipY,item.Rotation);
        resize.DragDelta += (_, e) =>
        {
            if (e.HorizontalChange == 0 && e.VerticalChange == 0) return;
            if (item.StartId != null || item.EndId != null) ShapeGeometry.Detach(_page!,item);
            item.Width = Math.Clamp(item.Width + e.HorizontalChange, 8, 100000); item.Height = Math.Clamp(item.Height + e.VerticalChange, 8, 100000); RefreshShapes();
        };
        resize.DragCompleted += (_, e) =>
        {
            if (e.Canceled) { (item.StartId,item.EndId,item.X,item.Y,item.Width,item.Height,item.FlipX,item.FlipY,item.Rotation) = beforeResize; RefreshShapes(); }
            else NotifyChanged();
        };
        RefreshShapes();
    }

    private void RefreshShapes()
    {
        if (_page == null) return;
        foreach (var visual in _shapes.Values)
        {
            var bounds = ShapeGeometry.Bounds(_page, visual.Item);
            Canvas.SetLeft(visual.Controls, bounds.Left); Canvas.SetTop(visual.Controls, bounds.Top);
            visual.Controls.Width = Math.Max(12, bounds.Width); visual.Controls.Height = Math.Max(12, bounds.Height);
            Panel.SetZIndex(visual.Drawing, Z(visual.Item.Layer, 2)); Panel.SetZIndex(visual.Controls, Z(visual.Item.Layer, 2));
            visual.Drawing.InvalidateVisual();
            var selected = IncludesObject(visual.Item.Id);
            visual.Controls.Visibility = _tool == EditorTool.Select && !_spacePan ? Visibility.Visible : Visibility.Collapsed;
            visual.Outline.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            visual.Resize.Visibility = selected && HasSingleObject ? Visibility.Visible : Visibility.Hidden;
        }
    }

    internal ShapeItem AddShape(Point point, ShapeKind kind)
    {
        if (_page == null) throw new InvalidOperationException();
        var item = new ShapeItem { Kind = kind, X = Math.Clamp(point.X, 0, Math.Max(0, _page.Width - 160)),
            Y = Math.Clamp(point.Y, 0, Math.Max(0, _page.Height - 100)), Color = InkColor, FillColor = ShapeFill,
            StrokeWidth = InkWidth, Opacity = InkOpacity };
        _page.Shapes.Add(item); AddShapeVisual(item); EnterSelectionTool(); SelectObject(item.Id); NotifyChanged();
        return item;
    }

    internal void ConnectSelectedShapes()
    {
        var nodes = SelectedShapes.Where(s => s.Kind is not (ShapeKind.Line or ShapeKind.Arrow)).ToArray();
        if (nodes.Length != 2 || _page == null) return;
        var a = ShapeGeometry.Center(nodes[0]); var b = ShapeGeometry.Center(nodes[1]);
        var connector = new ShapeItem { Kind = ShapeKind.Arrow, X = Math.Min(a.X, b.X), Y = Math.Min(a.Y, b.Y),
            Width = Math.Max(1, Math.Abs(a.X - b.X)), Height = Math.Max(1, Math.Abs(a.Y - b.Y)),
            StartId = nodes[0].Id, EndId = nodes[1].Id, Color = InkColor, Opacity = InkOpacity, StrokeWidth = InkWidth };
        _page.Shapes.Add(connector); AddShapeVisual(connector); SelectObject(connector.Id); NotifyChanged();
    }

    private void RemoveShape(ShapeItem item)
    {
        if (!_shapes.Remove(item.Id, out var visual)) return;
        // Detach using the last visible endpoint so deleting a node never moves its connector arbitrarily.
        foreach (var connector in _page!.Shapes.Where(s => s.StartId == item.Id || s.EndId == item.Id))
        {
            var (a, b) = ShapeGeometry.Endpoints(_page, connector);
            connector.X = Math.Min(a.X, b.X); connector.Y = Math.Min(a.Y, b.Y);
            connector.Width = Math.Max(1, Math.Abs(a.X - b.X)); connector.Height = Math.Max(1, Math.Abs(a.Y - b.Y));
            connector.FlipX = a.X > b.X; connector.FlipY = a.Y > b.Y; connector.Rotation = 0;
            if (connector.StartId == item.Id) connector.StartId = null;
            if (connector.EndId == item.Id) connector.EndId = null;
        }
        _page.Shapes.Remove(item); _objects.Children.Remove(visual.Drawing); _objects.Children.Remove(visual.Controls); RefreshShapes();
    }

    internal void ApplySelectedColor(string color, double opacity, bool fill = false)
    {
        InkColor = color; InkOpacity = opacity;
        if (fill) ShapeFill = color;
        foreach (var text in SelectedTexts) { text.Color = color; text.Opacity = opacity; ApplyTextStyle(_items[text.Id]); }
        foreach (var shape in SelectedShapes) { if (fill) shape.FillColor = color; else shape.Color = color; shape.Opacity = opacity; }
        foreach (var stroke in _ink.GetSelectedStrokes()) { if (stroke.ContainsPropertyData(StrokeColor)) stroke.RemovePropertyData(StrokeColor); stroke.AddPropertyData(StrokeColor, color); if (stroke.ContainsPropertyData(StrokeOpacity)) stroke.RemovePropertyData(StrokeOpacity); stroke.AddPropertyData(StrokeOpacity, opacity); UpdateInkLayer(stroke); }
        foreach (var picture in _pictures.Values.Where(v => IncludesObject(v.Item.Id))) { picture.Item.Opacity = opacity; PositionPicture(picture); }
        RefreshShapes(); if (HasSelection) SyncInkAndNotify();
    }

    internal void ApplyTextFormatting(Action<TextItem> change)
    {
        foreach (var item in SelectedTexts) { change(item); ApplyTextStyle(_items[item.Id]); }
        UpdateItemStates();
        if (SelectedTexts.Count > 0) NotifyChanged();
    }

    internal DocumentData ExportSelectedObjects() => DocumentStore.Clone(ObjectDocument(SelectionData()));

    internal void ApplyStrokeWidth(double width)
    {
        foreach (var stroke in _ink.GetSelectedStrokes()) stroke.DrawingAttributes.Width = stroke.DrawingAttributes.Height = width;
        foreach (var shape in SelectedShapes) shape.StrokeWidth = width;
        RefreshShapes(); if (_ink.GetSelectedStrokes().Count > 0 || SelectedShapes.Count > 0) SyncInkAndNotify();
    }

    internal void ApplyPictureFormatting(Action<ImageItem> change)
    {
        foreach (var item in SelectedPictures) { change(item); PositionPicture(_pictures[item.Id]); }
        if (SelectedPictures.Count > 0) NotifyChanged();
    }

    internal void ApplyShapeFormatting(Action<ShapeItem> change)
    {
        foreach (var item in SelectedShapes)
        {
            var dimensions = (item.Width,item.Height,item.Rotation); change(item);
            if ((item.StartId != null || item.EndId != null) && dimensions != (item.Width,item.Height,item.Rotation))
            {
                var edited = (item.Width,item.Height,item.Rotation); ShapeGeometry.Detach(_page!,item);
                (item.Width,item.Height,item.Rotation) = edited;
            }
        }
        RefreshShapes(); if (SelectedShapes.Count > 0) NotifyChanged();
    }

    private static Color ColorWithOpacity(string hex, double opacity)
    {
        var color = ParseColor(hex); color.A = (byte)Math.Round(opacity * 255); return color;
    }

    internal void EditSelectedContent()
    {
        if (_selectedItem != null && _items.TryGetValue(_selectedItem, out var visual)) BeginTextEdit(visual);
    }

    private static void UpdateRichPreview(ItemVisual visual)
    {
        var item = visual.Item;
        var key = $"{item.Text}|{item.Width}|{item.FontFamily}|{item.FontSize}|{item.Color}|{item.Opacity}|{item.Bold}|{item.Italic}|{item.Underline}|{item.Alignment}";
        if (visual.PreviewKey == key) return;
        visual.Preview!.Document = StructuredText.CreateDocument(item); visual.Preview.Opacity = item.Opacity; visual.PreviewKey = key;
    }

    private static void ApplyTextStyle(ItemVisual visual)
    {
        var item = visual.Item; var editor = visual.Editor;
        editor.FontFamily = new FontFamily(item.FontFamily); editor.FontSize = item.FontSize;
        editor.FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal;
        editor.FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal;
        editor.TextDecorations = item.Underline ? TextDecorations.Underline : null;
        editor.TextAlignment = Enum.Parse<TextAlignment>(item.Alignment);
        editor.Foreground = new SolidColorBrush(ColorWithOpacity(item.Color, item.Opacity));
        if (item.ContentFormat == "Markdown") UpdateRichPreview(visual);
        if (!item.IsNote)
        {
            visual.Root.Width = item.Width + 8; editor.MinHeight = 24; editor.Height = Math.Max(24, item.Height);
            visual.Root.RenderTransformOrigin = new Point(.5, .5); visual.Root.RenderTransform = new RotateTransform(item.Rotation);
            Panel.SetZIndex(visual.Root, Z(item.Layer, 3));
        }
    }

    private void AttachTextResize(ItemVisual visual, Grid panel)
    {
        var item = visual.Item;
        foreach (var (x, y, cursor) in new[] { (-1,-1,Cursors.SizeNWSE),(0,-1,Cursors.SizeNS),(1,-1,Cursors.SizeNESW),(-1,0,Cursors.SizeWE),(1,0,Cursors.SizeWE),(-1,1,Cursors.SizeNESW),(0,1,Cursors.SizeNS),(1,1,Cursors.SizeNWSE) })
        {
            var handle = new Thumb { Width = 9, Height = 9, Cursor = cursor, Template = HandleTemplate(Brushes.SlateBlue),
                HorizontalAlignment = x < 0 ? HorizontalAlignment.Left : x > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                VerticalAlignment = y < 0 ? VerticalAlignment.Top : y > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                Margin = new Thickness(-4), Tag = item.Id };
            Grid.SetRowSpan(handle, 2); Panel.SetZIndex(handle, 20); panel.Children.Add(handle); visual.ResizeHandles.Add(handle);
            AutomationProperties.SetName(handle, Translate("ResizeObject"));
            AutomationProperties.SetAutomationId(handle, $"TextResize:{item.Id}:{x}:{y}");
            Rect before = default;
            handle.DragStarted += (_, _) => { CommitEdits(); before = new Rect(item.X, item.Y, item.Width, item.Height); };
            handle.DragDelta += (_, e) =>
            {
                var dx = e.HorizontalChange; var dy = e.VerticalChange;
                var width = Math.Clamp(item.Width + x * dx, item.IsNote ? 160 : 40, 100000);
                var height = Math.Clamp(item.Height + y * dy, item.IsNote ? 64 : 24, 100000);
                if (!item.IsNote) { if (x < 0) item.X += item.Width - width; if (y < 0) item.Y += item.Height - height; }
                item.Width = width; item.Height = height; ApplyTextStyle(visual); PositionItem(visual);
            };
            handle.DragCompleted += (_, e) =>
            {
                if (e.Canceled) { item.X = before.X; item.Y = before.Y; item.Width = before.Width; item.Height = before.Height; ApplyTextStyle(visual); PositionItem(visual); }
                else if (before != new Rect(item.X, item.Y, item.Width, item.Height)) NotifyChanged();
            };
        }
    }

    private string GroupFor(string id) => _page?.Items.FirstOrDefault(i => i.Id == id)?.GroupId ??
        _page?.Images.FirstOrDefault(i => i.Id == id)?.GroupId ?? _page?.Shapes.FirstOrDefault(i => i.Id == id)?.GroupId ?? "";

    private void ExpandGroupSelection(string id)
    {
        var group = GroupFor(id); if (group.Length == 0 || _page == null) return;
        foreach (var other in _page.Items.Where(i => i.GroupId == group).Select(i => i.Id)
            .Concat(_page.Images.Where(i => i.GroupId == group).Select(i => i.Id)).Concat(_page.Shapes.Where(i => i.GroupId == group).Select(i => i.Id)))
            if (other != id) _otherObjects.Add(other);
        SelectStrokes(new StrokeCollection(_ink.Strokes.Where(s => s.ContainsPropertyData(StrokeGroup) && (string)s.GetPropertyData(StrokeGroup) == group)));
    }

    internal void GroupSelection(bool ungroup = false)
    {
        if (_page == null || !HasSelection) return;
        var group = ungroup ? "" : Guid.NewGuid().ToString("N");
        foreach (var i in SelectedTexts) i.GroupId = group;
        foreach (var i in SelectedShapes) i.GroupId = group;
        foreach (var i in _page.Images.Where(i => IncludesObject(i.Id))) i.GroupId = group;
        foreach (var i in _ink.GetSelectedStrokes()) { if (i.ContainsPropertyData(StrokeGroup)) i.RemovePropertyData(StrokeGroup); i.AddPropertyData(StrokeGroup, group); }
        SyncInkAndNotify();
    }

    internal void ChangeLayer(bool front)
    {
        if (_page == null || !HasSelection) return;
        var values = _page.Items.Select(i => i.Layer).Concat(_page.Shapes.Select(i => i.Layer)).Concat(_page.Images.Select(i => i.Layer)).Concat(_page.Ink.Select(i => i.Layer)).Append(0);
        var layer = Math.Clamp(front ? values.Max() + 2 : values.Min() - 2, -999998, 999998);
        foreach (var i in SelectedTexts) { i.Layer = layer; ApplyTextStyle(_items[i.Id]); }
        foreach (var i in SelectedShapes) i.Layer = layer;
        foreach (var i in _page.Images.Where(i => IncludesObject(i.Id))) { i.Layer = layer; PositionPicture(_pictures[i.Id]); }
        foreach (var i in _ink.GetSelectedStrokes()) { if (i.ContainsPropertyData(StrokeLayer)) i.RemovePropertyData(StrokeLayer); i.AddPropertyData(StrokeLayer, layer); UpdateInkLayer(i); }
        RefreshShapes(); SyncInkAndNotify();
    }
}
