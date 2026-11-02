using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Inklane.Core;

namespace Inklane.Controls;

public sealed partial class DocumentSurface
{
    private readonly HashSet<string> _otherObjects = [];
    private bool _selectingStrokes;
    internal Action? RequestSelectTool { get; set; }
    internal Func<Point?, ContextMenu>? CreateObjectMenu { get; set; }

    public bool HasSelection => _selectedItem != null || _otherObjects.Count != 0 || _ink.GetSelectedStrokes().Count != 0;

    internal bool HasSingleObject => _selectedItem != null && _otherObjects.Count == 0 && _ink.GetSelectedStrokes().Count == 0;
    internal bool HasSingleText => HasSingleObject && _items.TryGetValue(_selectedItem!,out var visual) && !visual.Item.IsNote;
    internal void EditSelectedText()
    {
        if (HasSingleText) BeginTextEdit(_items[_selectedItem!]);
    }

    private void InitializeSelection()
    {
        _ink.SelectionChanged += (_, _) =>
        {
            if (_selectingStrokes || _loading || _ink.GetSelectedStrokes().Count == 0) return;
            _otherObjects.Clear();
            _selectedItem = null;
            UpdateItemStates();
        };
        _ink.SelectionMoving += (_, e) =>
        {
            if (_selectedItem == null && _otherObjects.Count == 0) return;
            var delta = MoveSelection(e.NewRectangle.X - e.OldRectangle.X, e.NewRectangle.Y - e.OldRectangle.Y, moveInk: false);
            e.NewRectangle = new Rect(e.OldRectangle.Location + delta, e.OldRectangle.Size);
        };
        // Native ink resize handles affect strokes only. A mixed selection translates together.
        _ink.SelectionResizing += (_, e) => e.Cancel = _selectedItem != null || _otherObjects.Count > 0;

        ContextMenu = new ContextMenu();
        ContextMenuOpening += (_, e) =>
        {
            if (CreateObjectMenu == null || _page == null) return;
            var source = e.OriginalSource as DependencyObject;
            string? objectId = null;
            for (var current = source; current is Visual && current != this; current = VisualTreeHelper.GetParent(current))
            {
                if (current is TextBoxBase box && !box.IsReadOnly) return;
                if (current is FrameworkElement { Tag: string id } && (_items.ContainsKey(id) || _pictures.ContainsKey(id)))
                    objectId = id;
            }
            e.Handled = true;
            var position = e.CursorLeft < 0 ? (Point?)null : Mouse.GetPosition(_pageHost);
            // Object grips live in Select mode, but the context menu should also find
            // document objects under the pen without requiring a tool change first.
            var hit = position is { } pt
                ? _ink.Strokes.HitTest(pt, Math.Clamp(6 / _zoom, 1, 100)).LastOrDefault() : null;
            if (objectId == null && position is { } p)
            {
                objectId = _page.Items.LastOrDefault(i => !i.IsNote && new Rect(i.X,i.Y,i.Width,i.Height).Contains(p))?.Id;
                if (objectId == null && hit == null)
                    objectId = _page.Images.LastOrDefault(i => new Rect(i.X,i.Y,i.Width,i.Height).Contains(p))?.Id;
            }
            if (objectId != null && (_tool != EditorTool.Select || !IncludesObject(objectId)))
            {
                EnterSelectionTool();
                SelectObject(objectId);
            }
            else if (objectId == null && hit != null && !_ink.GetSelectedStrokes().Contains(hit))
            {
                EnterSelectionTool();
                ClearObjectSelection();
                SelectStrokes(new StrokeCollection([hit]));
            }
            else if (objectId == null && hit == null && position is { } empty &&
                _ink.HitTestSelection(empty) == InkCanvasSelectionHitResult.None) ClearObjectSelection();
            Focus();
            ShowObjectMenu(position, objectId);
        };
    }

    internal void ShowObjectMenu(Point? position = null, string? objectId = null)
    {
        if (CreateObjectMenu == null) return;
        objectId ??= _selectedItem;
        var menu = CreateObjectMenu(position);
        menu.PlacementTarget = objectId != null && _items.TryGetValue(objectId, out var visual)
            ? visual.Marker as UIElement ?? visual.Root
            : objectId != null && _pictures.TryGetValue(objectId, out var picture) ? picture.Controls : this;
        menu.Placement = position.HasValue ? PlacementMode.MousePoint : PlacementMode.Center;
        menu.IsOpen = true;
    }

    private bool IncludesObject(string id) => _selectedItem == id || _otherObjects.Contains(id);

    private void SelectStrokes(StrokeCollection strokes)
    {
        _selectingStrokes = true;
        try { _ink.Select(strokes); }
        finally { _selectingStrokes = false; }
    }

    private void SelectObject(string id)
    {
        _otherObjects.Clear();
        _selectedItem = id;
        if (_editingText != id) _editingText = null;
        if (_ink.GetSelectedStrokes().Count != 0) SelectStrokes([]);
        UpdateItemStates();
    }

    private void ToggleObject(string id)
    {
        if (_selectedItem == id)
        {
            _selectedItem = _otherObjects.FirstOrDefault();
            if (_selectedItem != null) _otherObjects.Remove(_selectedItem);
        }
        else if (!_otherObjects.Remove(id))
        {
            if (_selectedItem != null) _otherObjects.Add(_selectedItem);
            _selectedItem = id;
        }
        _editingText = null;
        UpdateItemStates();
    }

    internal void ClearObjectSelection()
    {
        _selectedItem = _editingText = null;
        _otherObjects.Clear();
        if (_ink.GetSelectedStrokes().Count != 0) SelectStrokes([]);
        UpdateItemStates();
    }

    private void EnterSelectionTool()
    {
        if (_tool == EditorTool.Select) return;
        if (RequestSelectTool != null) RequestSelectTool();
        else Tool = EditorTool.Select;
    }

    internal void SelectAllObjects()
    {
        if (_page == null) return;
        EnterSelectionTool();
        SelectObjects(_page.Items.Select(i => i.Id).Concat(_page.Images.Select(i => i.Id)), _ink.Strokes);
    }

    private void SelectObjects(IEnumerable<string> ids, StrokeCollection strokes)
    {
        _otherObjects.Clear();
        _selectedItem = null;
        foreach (var id in ids)
        {
            if (_selectedItem != null) _otherObjects.Add(_selectedItem);
            _selectedItem = id;
        }
        SelectStrokes(new StrokeCollection(strokes));
        UpdateItemStates();
        Focus();
    }

    private PageData SelectionData()
    {
        CommitEdits();
        return new PageData
        {
            Width = _page!.Width, Height = _page.Height, MarginMm = 0,
            Items = _page.Items.Where(i => IncludesObject(i.Id)).ToList(),
            Images = _page.Images.Where(i => IncludesObject(i.Id)).ToList(),
            Ink = _ink.GetSelectedStrokes().Select(FromStroke).ToList()
        };
    }

    private static Rect Bounds(PageData content)
    {
        var bounds = Rect.Empty;
        foreach (var item in content.Items)
            bounds.Union(new Rect(item.X, item.Y, item.IsNote ? 28 : item.Width, item.IsNote ? 28 : item.Height));
        foreach (var item in content.Images) bounds.Union(new Rect(item.X, item.Y, item.Width, item.Height));
        foreach (var stroke in content.Ink)
            foreach (var point in stroke.Points) bounds.Union(new Point(point.X, point.Y));
        return bounds;
    }

    private static double Constrain(double delta, double min, double max, double size)
    {
        // Oversized objects can still be positioned by either edge.
        return Math.Clamp(delta, Math.Min(-min, size - max), Math.Max(-min, size - max));
    }

    private Vector MoveSelection(double dx, double dy, bool moveInk = true)
    {
        if (_page == null) return default;
        var bounds = _ink.GetSelectedStrokes().GetBounds();
        foreach (var item in _page.Items.Where(i => IncludesObject(i.Id)))
            bounds.Union(new Rect(item.X, item.Y, item.IsNote ? 28 / _zoom : item.Width, item.IsNote ? 28 / _zoom : item.Height));
        foreach (var item in _page.Images.Where(i => IncludesObject(i.Id)))
            bounds.Union(new Rect(item.X, item.Y, item.Width, item.Height));
        if (bounds.IsEmpty) return default;
        dx = Constrain(dx, bounds.Left, bounds.Right, _page.Width);
        dy = Constrain(dy, bounds.Top, bounds.Bottom, _page.Height);
        foreach (var visual in _items.Values.Where(v => IncludesObject(v.Item.Id)))
        {
            visual.Item.X += dx; visual.Item.Y += dy;
            PositionItem(visual);
        }
        foreach (var visual in _pictures.Values.Where(v => IncludesObject(v.Item.Id)))
        {
            visual.Item.X += dx; visual.Item.Y += dy;
            PositionPicture(visual);
        }
        if (moveInk && _ink.GetSelectedStrokes().Count != 0)
            _ink.GetSelectedStrokes().Transform(new Matrix(1, 0, 0, 1, dx, dy), false);
        return new Vector(dx, dy);
    }

    internal bool NudgeSelection(Key key, bool large)
    {
        if (!HasSelection || HasTextFocus) return false;
        var amount = large ? 10 : 1;
        var (x,y) = key switch { Key.Left => (-amount,0), Key.Right => (amount,0), Key.Up => (0,-amount), Key.Down => (0,amount), _ => (0,0) };
        if (x == 0 && y == 0) return false;
        MoveSelection(x, y);
        SyncInkAndNotify();
        return true;
    }

    private void DeleteSelectedObjects()
    {
        if (_page == null || !HasSelection) return;
        var selected = _ink.GetSelectedStrokes();
        if (selected.Count > 0) _ink.Strokes.Remove(selected);
        foreach (var item in _page.Items.Where(i => IncludesObject(i.Id)).ToArray())
        {
            var visual = _items[item.Id];
            _objects.Children.Remove(visual.Root);
            if (visual.Marker != null) _objects.Children.Remove(visual.Marker);
            _items.Remove(item.Id);
            _page.Items.Remove(item);
        }
        foreach (var item in _page.Images.Where(i => IncludesObject(i.Id)).ToArray()) RemovePicture(item);
        ClearObjectSelection();
        NumberNotes();
        SyncInkAndNotify();
        Focus();
    }
}
