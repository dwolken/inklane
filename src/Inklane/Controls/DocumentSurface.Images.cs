using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Inklane.Core;

namespace Inklane.Controls;

public sealed partial class DocumentSurface
{
    private readonly Canvas _imageLayer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Dictionary<string, PictureVisual> _pictures = [];
    private sealed record PictureVisual(ImageItem Item, BitmapSource Source, Image Image, Grid Controls, Border Outline, Thumb Resize);

    private void LoadPictures()
    {
        _pictures.Clear();
        _imageLayer.Children.Clear();
        foreach (var item in _page!.Images) AddPicture(item);
    }

    private static ControlTemplate HandleTemplate(Brush fill)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, fill);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = border };
    }

    private void AddPicture(ImageItem item)
    {
        var source = EmbeddedPng.Decode(item.PngData);
        var image = new Image { Source = source, Stretch = Stretch.Fill };
        image.IsHitTestVisible = false;
        _objects.Children.Add(image);
        var controls = new Grid { Tag = item.Id };
        Panel.SetZIndex(controls, -1);
        var outline = new Border { IsHitTestVisible = false, BorderThickness = new Thickness(1.5), BorderBrush = Brushes.SlateBlue };
        var move = new Thumb { Cursor = Cursors.SizeAll, Template = HandleTemplate(Brushes.Transparent) };
        AutomationProperties.SetName(move, Translate("MovePicture"));
        AutomationProperties.SetAutomationId(move, "ImageMove:" + item.Id);
        var resize = new Thumb
        {
            Cursor = Cursors.SizeNWSE, Width = 12, Height = 12,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Template = HandleTemplate(Brushes.SlateBlue), ToolTip = Translate("ResizePicture")
        };
        AutomationProperties.SetName(resize, Translate("ResizePicture"));
        controls.Children.Add(move); controls.Children.Add(outline); controls.Children.Add(resize);
        _objects.Children.Add(controls);
        var visual = new PictureVisual(item, source, image, controls, outline, resize);
        _pictures.Add(item.Id, visual);
        PositionPicture(visual);

        var moved = new Vector();
        bool draggingInk = false;
        move.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var hit = _ink.Strokes.HitTest(e.GetPosition(_ink), Math.Clamp(6 / _zoom, 1, 100)).Where(s => !s.ContainsPropertyData(StrokeLayer) ? item.Layer <= 0 : Z((int)s.GetPropertyData(StrokeLayer),1) > Z(item.Layer,0)).LastOrDefault();
            draggingInk = hit != null;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (hit != null)
                {
                    var selected = _ink.GetSelectedStrokes();
                    if (selected.Contains(hit)) selected.Remove(hit); else selected.Add(hit);
                    SelectStrokes(selected);
                }
                else ToggleObject(item.Id);
                Focus(); e.Handled = true;
            }
            else if (hit != null && !_ink.GetSelectedStrokes().Contains(hit))
            {
                ClearObjectSelection();
                SelectStrokes(new System.Windows.Ink.StrokeCollection([hit]));
            }
        };
        move.DragStarted += (_, _) => { if (!draggingInk && !IncludesObject(item.Id)) SelectObject(item.Id); moved = default; };
        move.DragDelta += (_, e) => moved += MoveSelection(e.HorizontalChange, e.VerticalChange);
        move.DragCompleted += (_, e) =>
        {
            if (e.Canceled) MoveSelection(-moved.X, -moved.Y);
            else if (moved.LengthSquared > 0) SyncInkAndNotify();
        };
        var original = new Size();
        resize.DragStarted += (_, _) => original = new Size(item.Width, item.Height);
        resize.DragDelta += (_, e) =>
        {
            if (_page == null) return;
            var max = Math.Min(_page.Width - item.X, (_page.Height - item.Y) * original.Width / original.Height);
            item.Width = Math.Clamp(item.Width + e.HorizontalChange, Math.Min(12, Math.Max(1, max)), Math.Max(1, max));
            item.Height = item.Width * original.Height / original.Width;
            PositionPicture(visual);
        };
        resize.DragCompleted += (_, e) =>
        {
            if (e.Canceled) { item.Width = original.Width; item.Height = original.Height; PositionPicture(visual); }
            else if (item.Width != original.Width) NotifyChanged();
        };
        move.KeyDown += (_, e) => e.Handled = NudgeSelection(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private static void PositionPicture(PictureVisual visual)
    {
        foreach (var element in new FrameworkElement[] { visual.Image, visual.Controls })
        {
            Canvas.SetLeft(element, visual.Item.X); Canvas.SetTop(element, visual.Item.Y);
            element.Width = visual.Item.Width; element.Height = visual.Item.Height;
            element.RenderTransformOrigin = new Point(.5,.5); element.RenderTransform = new RotateTransform(visual.Item.Rotation);
            Panel.SetZIndex(element, Z(visual.Item.Layer, 0));
        }
        visual.Image.Opacity = visual.Item.Opacity;
    }

    private void UpdatePictureStates()
    {
        foreach (var visual in _pictures.Values)
        {
            var selected = IncludesObject(visual.Item.Id);
            visual.Controls.Visibility = !_spacePan && _tool == EditorTool.Select ? Visibility.Visible : Visibility.Collapsed;
            visual.Outline.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            visual.Resize.Visibility = selected && _otherObjects.Count == 0 && _ink.GetSelectedStrokes().Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RemovePicture(ImageItem item)
    {
        if (!_pictures.Remove(item.Id, out var visual)) return;
        _page!.Images.Remove(item);
        _objects.Children.Remove(visual.Image);
        _objects.Children.Remove(visual.Controls);
    }
}
