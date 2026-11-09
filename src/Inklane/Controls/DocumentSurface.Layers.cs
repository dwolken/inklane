using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Inklane.Core;

namespace Inklane.Controls;

public sealed partial class DocumentSurface
{
    private static readonly Guid StrokeColor = new("e3addcc1-9ac2-4784-8e39-4ab0b66d0d9c");
    private readonly Dictionary<Stroke, InkDrawing> _inkVisuals = [];
    private StrokeCollection? _observedStrokes;
    private bool _layerDrag;
    private Point _layerDragPoint;
    private Vector _layerDragTotal;
    // Equal-layer legacy documents retain their original image, ink, shape, text order.
    private static int Z(int layer, int kind) => layer * 8 + kind;

    private sealed class InkDrawing(Stroke stroke) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var hex = stroke.ContainsPropertyData(StrokeColor) ? (string)stroke.GetPropertyData(StrokeColor) : "#202020";
            var opacity = stroke.ContainsPropertyData(StrokeOpacity) ? (double)stroke.GetPropertyData(StrokeOpacity) : 1;
            dc.DrawGeometry(new SolidColorBrush(ColorWithOpacity(hex, opacity)), null, stroke.GetGeometry());
        }
    }

    private static Stroke ToLiveStroke(InkData item)
    {
        var stroke = ToStroke(item); stroke.AddPropertyData(StrokeColor, item.Color);
        // InkCanvas still owns pressure input, native hit testing and lasso handles. Its
        // completed ink is rendered in the common object stack; the dynamic pen stays live.
        stroke.DrawingAttributes.Color = Colors.Transparent;
        return stroke;
    }

    private void LoadInkLayers()
    {
        if (_observedStrokes != null) _observedStrokes.StrokesChanged -= InkLayersChanged;
        foreach (var (stroke, _) in _inkVisuals) { stroke.Invalidated -= InkLayerInvalidated; }
        _inkVisuals.Clear();
        _observedStrokes = _ink.Strokes; _observedStrokes.StrokesChanged += InkLayersChanged;
        foreach (var stroke in _ink.Strokes) AddInkLayer(stroke);
    }

    private void InitializeLayerInput()
    {
        _objects.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_tool != EditorTool.Select || _spacePan || _page == null) return;
            // An active text field or resize grip keeps its own editing gesture.
            for (var node = e.OriginalSource as DependencyObject; node is Visual && node != _objects; node = VisualTreeHelper.GetParent(node))
            {
                if (node is TextBox { IsReadOnly: false }) return;
                if (node is System.Windows.Controls.Primitives.Thumb handle &&
                    System.Windows.Automation.AutomationProperties.GetAutomationId(handle).Contains("Resize:", StringComparison.Ordinal)) return;
            }
            var point = e.GetPosition(_pageHost);
            string? top = null; bool isInk = false;
            foreach (var candidate in PageObjects.InPaintOrder(_page))
            {
                bool hit = candidate switch
                {
                    ImageItem image => new Rect(image.X,image.Y,image.Width,image.Height).Contains(point),
                    TextItem text => new Rect(text.X,text.Y,text.IsNote ? 28/_zoom : text.Width,text.IsNote ? 28/_zoom : text.Height).Contains(point),
                    ShapeItem shape => ShapeGeometry.Bounds(_page,shape).Contains(point),
                    InkData ink => _ink.Strokes.Any(stroke => stroke.ContainsPropertyData(StrokeId) && (string)stroke.GetPropertyData(StrokeId) == ink.Id && stroke.HitTest(point,Math.Clamp(5/_zoom,1,100))),
                    _ => false
                };
                if (!hit) continue;
                isInk = candidate is InkData;
                top = candidate switch { ImageItem image => image.Id, TextItem text => text.Id, ShapeItem shape => shape.Id, InkData ink => ink.Id, _ => null };
            }
            if (!isInk || top == null) return;
            var selected = _ink.Strokes.First(stroke => stroke.ContainsPropertyData(StrokeId) && (string)stroke.GetPropertyData(StrokeId) == top);
            if (!_ink.GetSelectedStrokes().Contains(selected))
            {
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ClearObjectSelection();
                var strokes = new StrokeCollection(_ink.GetSelectedStrokes()) { selected };
                // Use InkCanvas's normal event to expand a saved group as well.
                _ink.Select(strokes);
            }
            _layerDrag = true; _layerDragPoint = point; _layerDragTotal = default; CaptureMouse(); Focus(); e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !_layerDrag) return;
            MoveSelection(-_layerDragTotal.X,-_layerDragTotal.Y); _layerDrag = false; ReleaseMouseCapture(); e.Handled = true;
        };
        PreviewMouseMove += (_, e) =>
        {
            if (!_layerDrag) return;
            var point = e.GetPosition(_pageHost); var delta = point - _layerDragPoint;
            _layerDragTotal += MoveSelection(delta.X,delta.Y); _layerDragPoint = point; e.Handled = true;
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_layerDrag) return; _layerDrag = false; ReleaseMouseCapture();
            if (_layerDragTotal.LengthSquared > 0) SyncInkAndNotify(); e.Handled = true;
        };
        LostMouseCapture += (_, _) =>
        {
            if (!_layerDrag) return; _layerDrag = false; if (_layerDragTotal.LengthSquared > 0) SyncInkAndNotify();
        };
    }

    private void AddInkLayer(Stroke stroke)
    {
        if (_inkVisuals.ContainsKey(stroke)) return;
        if (!stroke.ContainsPropertyData(StrokeColor))
        {
            var color = stroke.DrawingAttributes.Color;
            stroke.AddPropertyData(StrokeColor, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }
        if (!stroke.ContainsPropertyData(StrokeOpacity)) stroke.AddPropertyData(StrokeOpacity, InkOpacity);
        stroke.DrawingAttributes.Color = Colors.Transparent;
        var visual = new InkDrawing(stroke) { IsHitTestVisible = false, Width = _page!.Width, Height = _page.Height };
        _inkVisuals.Add(stroke, visual); _objects.Children.Add(visual);
        stroke.Invalidated += InkLayerInvalidated; UpdateInkLayer(stroke);
    }

    private void InkLayersChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        foreach (var stroke in e.Removed)
        {
            stroke.Invalidated -= InkLayerInvalidated;
            if (_inkVisuals.Remove(stroke, out var visual)) _objects.Children.Remove(visual);
        }
        foreach (var stroke in e.Added) AddInkLayer(stroke);
    }

    private void InkLayerInvalidated(object? sender, EventArgs e) { if (sender is Stroke stroke) UpdateInkLayer(stroke); }
    private void UpdateInkLayer(Stroke stroke)
    {
        if (!_inkVisuals.TryGetValue(stroke, out var visual)) return;
        var layer = stroke.ContainsPropertyData(StrokeLayer) ? (int)stroke.GetPropertyData(StrokeLayer) : 0;
        Panel.SetZIndex(visual, Z(layer, 1)); visual.InvalidateVisual();
    }
}
