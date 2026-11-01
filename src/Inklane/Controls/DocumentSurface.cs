using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;
using Inklane.Core;

namespace Inklane.Controls;

public enum EditorTool { Pen, Select, Eraser, Text, Note, Pan }
public enum PageViewMode { Editing, FitWidth, FitPage, Manual }

/// <summary>Transient view state. Offsets are viewport DIPs at the captured zoom.</summary>
public readonly record struct PageViewport(PageViewMode Mode, double Zoom, double HorizontalOffset, double VerticalOffset)
{
    public PageViewport AtTop() => this with { HorizontalOffset = 0, VerticalOffset = 0 };
}

/// <summary>
/// The editable page has one coordinate system: 96 units per inch. Zoom only
/// transforms the view; persisted points and note anchors never depend on DPI.
/// </summary>
public sealed class DocumentSurface : UserControl
{
    private static readonly Guid StrokeId = new("70bbced0-ed7a-43af-8c61-391b4b265df2");
    private readonly ScrollViewer _scroll;
    private readonly Grid _pageHost;
    private readonly PaperLayer _paper;
    private readonly InkCanvas _ink;
    private readonly Canvas _objects;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Dictionary<string, ItemVisual> _items = [];
    private PageData? _page;
    private ImageSource? _image;
    private EditorTool _tool;
    private bool _loading;
    private bool _applyingView;
    private PageViewport? _pendingView;
    private bool _spacePan;
    private bool _panning;
    private Point _panStart;
    private Point _scrollStart;
    private string? _selectedItem;
    private string? _lastSelectedNote;
    private string? _editingText;
    private string _inkColor = "#202020";
    private double _inkWidth = 2;
    private double _zoom = 1;

    public event EventHandler? Changed;
    public event EventHandler? ViewChanged;
    public event EventHandler? NoteSelectionChanged;
    public Func<string, string> Translate { get; set; } = key => key;
    public bool HasTextFocus => Keyboard.FocusedElement is TextBox box && IsAncestorOf(box);
    public PageViewMode ViewMode { get; private set; } = PageViewMode.Editing;
    public string? SelectedNoteId => _selectedItem != null && _items.TryGetValue(_selectedItem, out var visual) && visual.Item.IsNote
        ? _selectedItem : null;

    public EditorTool Tool
    {
        get => _tool;
        set
        {
            CommitEdits();
            _tool = value;
            if (HasTextFocus) Keyboard.ClearFocus();
            _ink.Select(new StrokeCollection());
            _selectedItem = null;
            _editingText = null;
            ApplyTool();
        }
    }

    public string InkColor
    {
        get => _inkColor;
        set { _inkColor = value; ApplyInkAttributes(); }
    }

    public double InkWidth
    {
        get => _inkWidth;
        set { _inkWidth = Math.Clamp(value, .5, 30); ApplyInkAttributes(); }
    }

    public double TextSize { get; set; } = 18;

    public double Zoom
    {
        get => _zoom;
        set => SetManualZoom(value, ViewportCenter());
    }

    public DocumentSurface()
    {
        Focusable = true;
        _paper = new PaperLayer { IsHitTestVisible = false };
        _ink = new InkCanvas { Background = Brushes.Transparent, ClipToBounds = true };
        AutomationProperties.SetName(_ink, "Document canvas");
        _objects = new Canvas { ClipToBounds = false };
        _pageHost = new Grid
        {
            Margin = new Thickness(32),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LayoutTransform = _scale,
            SnapsToDevicePixels = false,
            ClipToBounds = false
        };
        _pageHost.Children.Add(_paper);
        _pageHost.Children.Add(_ink);
        _pageHost.Children.Add(_objects);
        _scroll = new ScrollViewer
        {
            Content = _pageHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            PanningMode = PanningMode.Both,
            Focusable = false
        };
        Content = _scroll;
        _scroll.ScrollChanged += (_, e) => { if (e.OriginalSource == _scroll) PositionNotes(); };
        _ink.StrokeCollected += (_, _) => SyncInkAndNotify();
        _ink.StrokeErased += (_, _) => SyncInkAndNotify();
        _ink.SelectionMoved += (_, _) => SyncInkAndNotify();
        _ink.SelectionResized += (_, _) => SyncInkAndNotify();
        _ink.SelectionChanged += (_, _) =>
        {
            if (_ink.GetSelectedStrokes().Count > 0)
            {
                _selectedItem = null;
                UpdateItemStates();
            }
        };
        _ink.PreviewMouseLeftButtonDown += CanvasPressed;
        PreviewMouseDown += BeginPan;
        PreviewMouseMove += ContinuePan;
        PreviewMouseUp += EndPan;
        LostMouseCapture += (_, _) => { _panning = false; ApplyCursor(); };
        PreviewMouseWheel += OnMouseWheel;
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Space && _spacePan) { _spacePan = false; ApplyTool(); }
        };
        LostKeyboardFocus += (_, _) =>
        {
            if (_spacePan) { _spacePan = false; ApplyTool(); }
        };
        _scroll.SizeChanged += (_, _) =>
        {
            if (_page == null || _applyingView) return;
            if (_pendingView is { } pending) RestoreView(pending);
            else if (ViewMode != PageViewMode.Manual) RestoreView(CaptureView());
        };
        ApplyInkAttributes();
        ApplyTool();
    }

    public void LoadPage(PageData page, ImageSource? image = null, PageViewport? view = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        _loading = true;
        try
        {
            _page = page;
            _image = image;
            _selectedItem = null;
            _editingText = null;
            _pageHost.Width = page.Width;
            _pageHost.Height = page.Height;
            _paper.Page = page;
            _paper.Image = image;
            _paper.InvalidateVisual();
            _ink.Strokes = new StrokeCollection(page.Ink.Select(ToStroke));
            _objects.Children.Clear();
            _items.Clear();
            foreach (var item in page.Items) AddItemVisual(item);
            NumberNotes();
            _ink.Select(new StrokeCollection());
            AutomationProperties.SetName(_ink, Translate("PageCanvas"));
            ApplyTool();
        }
        finally { _loading = false; }
        RestoreView(view ?? new PageViewport(image == null ? PageViewMode.Editing : PageViewMode.FitPage, 1, 0, 0));
    }

    public void CommitEdits()
    {
        if (_loading || _page is null) return;
        foreach (var visual in _items.Values)
        {
            visual.Item.Text = visual.Editor.Text;
        }
    }

    public void FitToPage() => RestoreView(new PageViewport(PageViewMode.FitPage, 1, 0, 0));
    public void FitToWidth() => RestoreView(new PageViewport(PageViewMode.FitWidth, 1, 0, 0));
    public void FitForEditing() => RestoreView(new PageViewport(PageViewMode.Editing, 1, 0, 0));
    public PageViewport CaptureView() => _pendingView ?? new PageViewport(ViewMode, _zoom, _scroll.HorizontalOffset, _scroll.VerticalOffset);

    public void RestoreView(PageViewport view)
    {
        if (_page == null || _applyingView) return;
        ViewMode = view.Mode;
        if (_scroll.ActualWidth <= 68 || _scroll.ActualHeight <= 68)
        {
            _pendingView = view;
            return;
        }

        _pendingView = null;
        _applyingView = true;
        try
        {
            var widthFit = (_scroll.ActualWidth - 68) / _page.Width;
            var zoom = view.Mode switch
            {
                PageViewMode.FitPage => Math.Min(widthFit, (_scroll.ActualHeight - 68) / _page.Height),
                PageViewMode.FitWidth => widthFit,
                PageViewMode.Editing => Math.Min(1.4, widthFit),
                _ => view.Zoom
            };
            ApplyScale(zoom);
            // A tall page introduces a vertical scrollbar. Use its measured
            // viewport width instead of assuming a theme-specific scrollbar size.
            if (view.Mode is PageViewMode.FitWidth or PageViewMode.Editing && _scroll.ViewportWidth > 68)
            {
                var available = (_scroll.ViewportWidth - 68) / _page.Width;
                ApplyScale(view.Mode == PageViewMode.Editing ? Math.Min(1.4, available) : available);
            }
            var ratio = view.Zoom > 0 ? _zoom / view.Zoom : 1;
            ScrollTo(view.HorizontalOffset * ratio, view.VerticalOffset * ratio);
        }
        finally { _applyingView = false; }
        PositionNotes();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomBy(double factor) => SetManualZoom(_zoom * factor, ViewportCenter());

    private Point ViewportCenter() => new(_scroll.ViewportWidth / 2, _scroll.ViewportHeight / 2);

    private void SetManualZoom(double value, Point focus)
    {
        if (_page == null || !double.IsFinite(value) || value <= 0) return;
        _pendingView = null;
        ViewMode = PageViewMode.Manual;
        var anchor = _scroll.TranslatePoint(focus, _pageHost);
        var fit = Math.Min((_scroll.ActualWidth - 68) / _page.Width, (_scroll.ActualHeight - 68) / _page.Height);
        var min = Math.Min(.05, Math.Max(.00001, fit / 2));
        ApplyScale(Math.Clamp(value, min, Math.Max(16, _zoom)));
        var moved = _pageHost.TranslatePoint(anchor, _scroll);
        ScrollTo(_scroll.HorizontalOffset + moved.X - focus.X, _scroll.VerticalOffset + moved.Y - focus.Y);
        PositionNotes();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) return;
        _zoom = scale;
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
        _scroll.UpdateLayout();
    }

    private void ScrollTo(double x, double y)
    {
        _scroll.ScrollToHorizontalOffset(x);
        _scroll.ScrollToVerticalOffset(y);
        _scroll.UpdateLayout();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetManualZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), e.GetPosition(_scroll));
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _editingText != null)
        {
            _editingText = null;
            UpdateItemStates();
            Focus();
            e.Handled = true;
            return;
        }
        if (HasTextFocus) return;
        if (e.Key == Key.F2 && _selectedItem != null && _items.TryGetValue(_selectedItem, out var selected) && !selected.Item.IsNote)
        {
            BeginTextEdit(selected);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && !_spacePan)
        {
            _spacePan = true;
            ApplyTool();
            e.Handled = true;
        }
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
        }
        if (e.Key == Key.Escape)
        {
            _ink.Select(new StrokeCollection());
            _selectedItem = null;
            UpdateItemStates();
        }
    }

    private void BeginPan(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle && !(_tool == EditorTool.Pan || _spacePan)) return;
        if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Left) return;
        _panStart = e.GetPosition(this);
        _scrollStart = new Point(_scroll.HorizontalOffset, _scroll.VerticalOffset);
        _panning = true;
        CaptureMouse();
        Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void ContinuePan(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var delta = e.GetPosition(this) - _panStart;
        _scroll.ScrollToHorizontalOffset(_scrollStart.X - delta.X);
        _scroll.ScrollToVerticalOffset(_scrollStart.Y - delta.Y);
        e.Handled = true;
    }

    private void EndPan(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        ReleaseMouseCapture();
        ApplyCursor();
        e.Handled = true;
    }

    private void ApplyTool()
    {
        _ink.EditingMode = _spacePan ? InkCanvasEditingMode.None : _tool switch
        {
            EditorTool.Pen => InkCanvasEditingMode.Ink,
            EditorTool.Select => InkCanvasEditingMode.Select,
            EditorTool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None
        };
        _ink.EditingModeInverted = InkCanvasEditingMode.EraseByStroke;
        UpdateItemStates();
        ApplyCursor();
    }

    private void ApplyCursor() => Cursor = _tool == EditorTool.Pan || _spacePan ? Cursors.Hand :
        _tool is EditorTool.Text ? Cursors.IBeam :
        _tool is EditorTool.Note ? Cursors.Cross : Cursors.Arrow;

    private void ApplyInkAttributes()
    {
        _ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = ParseColor(_inkColor), Width = _inkWidth, Height = _inkWidth,
            IgnorePressure = false, FitToCurve = false
        };
    }

    private void CanvasPressed(object sender, MouseButtonEventArgs e)
    {
        if (_page is null || _spacePan) return;
        if (_tool is not (EditorTool.Text or EditorTool.Note))
        {
            _selectedItem = null;
            UpdateItemStates();
            return;
        }
        var point = e.GetPosition(_pageHost);
        var isNote = _tool == EditorTool.Note;
        var width = isNote ? 280 : Math.Min(260, Math.Max(1, _page.Width));
        var item = new TextItem
        {
            Id = Guid.NewGuid().ToString("N"), Text = "", IsNote = isNote,
            IsExpanded = true, X = Math.Clamp(point.X, 0, Math.Max(0, _page.Width - (isNote ? 28 / _zoom : width))),
            Y = Math.Clamp(point.Y, 0, Math.Max(0, _page.Height - 32 / _zoom)), Width = width,
            Height = isNote ? 180 : 30, FontSize = Math.Clamp(TextSize, 8, 96), Color = _inkColor
        };
        _page.Items.Add(item);
        AddItemVisual(item);
        if (item.IsNote) NumberNotes();
        _selectedItem = item.Id;
        UpdateItemStates();
        _items[item.Id].Editor.Focus();
        NotifyChanged();
        e.Handled = true;
    }

    private void SyncInkAndNotify()
    {
        if (_loading || _page is null) return;
        _page.Ink = _ink.Strokes.Select(FromStroke).ToList();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (!_loading) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSelection()
    {
        if (_page is null || HasTextFocus) return;
        var selected = _ink.GetSelectedStrokes();
        if (selected.Count > 0)
        {
            _ink.Strokes.Remove(selected);
            SyncInkAndNotify();
        }
        if (_selectedItem is { } id && _items.TryGetValue(id, out var visual)) RemoveItem(visual.Item);
    }

    private void RemoveItem(TextItem item)
    {
        if (_page is null || !_items.Remove(item.Id, out var visual)) return;
        _page.Items.Remove(item);
        _objects.Children.Remove(visual.Root);
        if (visual.Marker is not null) _objects.Children.Remove(visual.Marker);
        _selectedItem = null;
        _editingText = null;
        if (item.IsNote) NumberNotes();
        UpdateItemStates();
        _ink.Focus();
        NotifyChanged();
    }

    private void AddItemVisual(TextItem item)
    {
        var dragOrigin = new Point(item.X, item.Y);
        var editor = new TextBox
        {
            Text = item.Text,
            AcceptsReturn = true,
            AcceptsTab = false,
            MaxLength = 1_000_000,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = item.IsNote ? 14 : item.FontSize,
            MinHeight = item.IsNote ? 32 : Math.Max(24, item.FontSize * 1.4),
            BorderThickness = new Thickness(0),
            Padding = item.IsNote ? new Thickness(12, 5, 12, 10) : new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = item.IsNote ? new SolidColorBrush(Color.FromRgb(53, 43, 24)) : new SolidColorBrush(ParseColor(item.Color)),
            SelectionBrush = new SolidColorBrush(Color.FromRgb(168, 165, 235)),
            VerticalScrollBarVisibility = item.IsNote ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsUndoEnabled = true,
            UndoLimit = 100,
            Template = DocumentTextTemplate()
        };
        if (item.IsNote)
        {
            // The card intentionally remains light paper in either app theme.
            // Scope only its scrollbar colors so the native Fluent template
            // remains visible against that paper, including in dark mode.
            editor.Resources["ScrollBarThumbFill"] = new SolidColorBrush(Color.FromRgb(126, 109, 65));
            editor.Resources["ScrollBarButtonArrowForeground"] = new SolidColorBrush(Color.FromRgb(87, 67, 28));
            editor.Resources["ScrollBarButtonBackground"] = new SolidColorBrush(Color.FromRgb(235, 216, 159));
            editor.Resources["ScrollBarTrackFill"] = Brushes.Transparent;
            editor.Resources["ScrollBarTrackFillPointerOver"] = new SolidColorBrush(Color.FromRgb(243, 228, 184));
            editor.Resources["ScrollBarTrackStroke"] = Brushes.Transparent;
        }
        AutomationProperties.SetName(editor, Translate(item.IsNote ? "Note" : "Text"));
        AutomationProperties.SetAutomationId(editor, (item.IsNote ? "NoteEditor:" : "TextEditor:") + item.Id);
        var root = new Border
        {
            Width = item.Width + (item.IsNote ? 0 : 8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = item.IsNote ? new SolidColorBrush(Color.FromRgb(255, 251, 239)) : Brushes.Transparent,
            CornerRadius = new CornerRadius(item.IsNote ? 8 : 3),
            Padding = new Thickness(item.IsNote ? 0 : 3),
            Focusable = false
        };
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Height = item.IsNote ? 34 : 18 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = new Thumb
        {
            Cursor = Cursors.SizeAll,
            Focusable = true,
            ToolTip = Translate("MoveItem"),
            Template = GripTemplate(item.IsNote),
            MinWidth = 24
        };
        AutomationProperties.SetName(grip, Translate("MoveItem"));
        header.Children.Add(grip);
        if (item.IsNote)
        {
            var caret = Studio.Icon("Down", 16);
            caret.RenderTransformOrigin = new Point(.5, .5);
            caret.RenderTransform = new RotateTransform(180);
            var collapse = new Button
            {
                Content = caret, Width = 28, Height = 26, MinHeight = 26, MinWidth = 0, Padding = new Thickness(0),
                Margin = new Thickness(1, 3, 4, 3), VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(87, 67, 28)),
                FontSize = 18, ToolTip = Translate("CollapseNote")
            };
            AutomationProperties.SetName(collapse, Translate("CollapseNote"));
            Grid.SetColumn(collapse, 1);
            header.Children.Add(collapse);
            collapse.Click += (_, _) => SetNoteExpanded(item, false);
            editor.Height = Math.Clamp(item.Height, 100, 300);
            root.BorderBrush = new SolidColorBrush(Color.FromRgb(209, 174, 103));
        }
        Grid.SetRow(editor, 1);
        panel.Children.Add(header);
        panel.Children.Add(editor);
        root.Child = panel;
        _objects.Children.Add(root);
        var visual = new ItemVisual(item, root, editor, header, grip);
        _items.Add(item.Id, visual);
        if (item.IsNote)
        {
            var marker = CreateMarker(item);
            visual.Marker = marker;
            _objects.Children.Add(marker);
        }
        else
        {
            var hitArea = new FrameworkElementFactory(typeof(Border));
            hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var bodyHandle = new Thumb { Cursor = Cursors.SizeAll, Focusable = true,
                Template = new ControlTemplate(typeof(Thumb)) { VisualTree = hitArea } };
            AutomationProperties.SetName(bodyHandle, Translate("MoveItem"));
            AutomationProperties.SetAutomationId(bodyHandle, "TextMove:" + item.Id);
            Grid.SetRow(bodyHandle, 1);
            panel.Children.Add(bodyHandle);
            visual.BodyHandle = bodyHandle;
            bodyHandle.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                BeginTextEdit(visual);
                editor.CaretIndex = Math.Max(0, editor.GetCharacterIndexFromPoint(e.GetPosition(editor), true));
                e.Handled = true;
            };
            AttachMoveHandle(bodyHandle);
        }
        PositionItem(visual);
        editor.TextChanged += (_, _) =>
        {
            if (_loading) return;
            item.Text = editor.Text;
            if (!item.IsNote)
            {
                editor.UpdateLayout();
                item.Height = Math.Max(24, editor.ActualHeight);
            }
            NotifyChanged();
        };
        editor.GotKeyboardFocus += (_, _) =>
        {
            _selectedItem = item.Id;
            _ink.Select(new StrokeCollection());
            UpdateItemStates();
        };
        root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_tool == EditorTool.Eraser)
            {
                RemoveItem(item);
                e.Handled = true;
                return;
            }
            if (_selectedItem != item.Id) _editingText = null;
            _selectedItem = item.Id;
            UpdateItemStates();
        };
        AttachMoveHandle(grip);

        void AttachMoveHandle(Thumb handle)
        {
            handle.DragStarted += (_, _) =>
            {
                CommitEdits();
                dragOrigin = new Point(item.X, item.Y);
                _selectedItem = item.Id;
                _ink.Select(new StrokeCollection());
                UpdateItemStates();
            };
            handle.DragDelta += (_, e) => MoveItem(visual,
                e.HorizontalChange / (item.IsNote ? _zoom : 1),
                e.VerticalChange / (item.IsNote ? _zoom : 1));
            handle.DragCompleted += (_, e) =>
            {
                if (e.Canceled)
                {
                    item.X = dragOrigin.X;
                    item.Y = dragOrigin.Y;
                    PositionItem(visual);
                }
                else if (item.X != dragOrigin.X || item.Y != dragOrigin.Y) NotifyChanged();
            };
            handle.KeyDown += (_, e) => MoveWithKeyboard(visual, e);
        }
    }

    private void BeginTextEdit(ItemVisual visual)
    {
        _selectedItem = _editingText = visual.Item.Id;
        UpdateItemStates();
        visual.Editor.Focus();
    }

    private static ControlTemplate GripTemplate(bool note)
    {
        var template = new ControlTemplate(typeof(Thumb));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "GripBorder";
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BackgroundProperty, note ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(236, 235, 250)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        if (note) text.SetValue(TextBlock.TextProperty, new TemplateBindingExtension(TagProperty));
        else text.SetValue(TextBlock.TextProperty, "⠿");
        text.SetValue(TextBlock.FontSizeProperty, note ? 12d : 14d);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(note ? Color.FromRgb(110, 74, 22) : Color.FromRgb(74, 67, 124)));
        text.SetValue(TextBlock.HorizontalAlignmentProperty, note ? HorizontalAlignment.Left : HorizontalAlignment.Center);
        if (note) text.SetValue(MarginProperty, new Thickness(12, 0, 0, 0));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        if (note)
        {
            var header = new FrameworkElementFactory(typeof(Grid));
            header.AppendChild(text);
            var dots = new GeometryGroup();
            for (var y = 1; y <= 9; y += 4)
                for (var x = 1; x <= 5; x += 4)
                    dots.Children.Add(new EllipseGeometry(new Point(x, y), 1, 1));
            dots.Freeze();
            var grip = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            grip.SetValue(System.Windows.Shapes.Path.DataProperty, dots);
            grip.SetValue(System.Windows.Shapes.Shape.FillProperty, new SolidColorBrush(Color.FromRgb(137, 109, 66)));
            grip.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
            grip.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            grip.SetValue(MarginProperty, new Thickness(0, 0, 5, 0));
            header.AppendChild(grip);
            border.AppendChild(header);
        }
        else border.AppendChild(text);
        template.VisualTree = border;
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(58, 111, 190)), "GripBorder"));
        template.Triggers.Add(focus);
        return template;
    }

    private static ControlTemplate DocumentTextTemplate()
    {
        // Document ink keeps its own colors, including in a dark application.
        // Retain the native TextBox editor and scrolling, without field chrome.
        var template = new ControlTemplate(typeof(TextBox));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(Control.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        host.SetValue(Control.ForegroundProperty, new TemplateBindingExtension(ForegroundProperty));
        host.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(host);
        template.VisualTree = border;
        return template;
    }

    private Button CreateMarker(TextItem item)
    {
        var dragStart = new Point();
        var anchorStart = new Point();
        var dragging = false;
        var marker = new Button
        {
            FontSize = 13, FontWeight = FontWeights.SemiBold, Width = 28, Height = 28, MinHeight = 28, MinWidth = 0,
            Padding = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(250, 218, 150)),
            Foreground = new SolidColorBrush(Color.FromRgb(79, 52, 16)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(167, 127, 54)), BorderThickness = new Thickness(1),
            ToolTip = Translate("ExpandNote"), Cursor = Cursors.Hand
        };
        marker.SetResourceReference(StyleProperty, "AnchorMarker");
        AutomationProperties.SetAutomationId(marker, "NoteMarker:" + item.Id);
        AutomationProperties.SetName(marker, Translate("Note"));
        marker.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(_pageHost);
            anchorStart = new Point(item.X, item.Y);
            dragging = false;
        };
        marker.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || !marker.IsMouseCaptured || _tool == EditorTool.Eraser) return;
            var delta = e.GetPosition(_pageHost) - dragStart;
            if (!dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 4 / _zoom) return;
            dragging = true;
            var visual = _items[item.Id];
            MoveItem(visual, anchorStart.X + delta.X - item.X, anchorStart.Y + delta.Y - item.Y);
            e.Handled = true;
        };
        marker.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            marker.ReleaseMouseCapture();
            _selectedItem = item.Id;
            UpdateItemStates();
            NotifyChanged();
            e.Handled = true;
        };
        marker.LostMouseCapture += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            NotifyChanged();
        };
        marker.Click += (_, _) =>
        {
            if (_tool == EditorTool.Eraser) { RemoveItem(item); return; }
            _selectedItem = item.Id;
            SetNoteExpanded(item, !item.IsExpanded);
            if (item.IsExpanded) _items[item.Id].Editor.Focus();
        };
        marker.KeyDown += (_, e) => MoveWithKeyboard(_items[item.Id], e);
        return marker;
    }

    private void MoveItem(ItemVisual visual, double dx, double dy)
    {
        if (_page is null) return;
        var item = visual.Item;
        item.X = Math.Clamp(item.X + dx, 0, Math.Max(0, _page.Width - (item.IsNote ? 28 / _zoom : item.Width)));
        item.Y = Math.Clamp(item.Y + dy, 0, Math.Max(0, _page.Height - (item.IsNote ? 28 / _zoom : item.Height)));
        PositionItem(visual);
    }

    private void MoveWithKeyboard(ItemVisual visual, KeyEventArgs e)
    {
        var amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var delta = e.Key switch
        {
            Key.Left => new Vector(-amount, 0), Key.Right => new Vector(amount, 0),
            Key.Up => new Vector(0, -amount), Key.Down => new Vector(0, amount), _ => default
        };
        if (delta.LengthSquared == 0) return;
        MoveItem(visual, delta.X, delta.Y);
        NotifyChanged();
        e.Handled = true;
    }

    private void SetNoteExpanded(TextItem item, bool expanded)
    {
        var changed = item.IsExpanded != expanded;
        item.IsExpanded = expanded;
        PositionItem(_items[item.Id]);
        UpdateItemStates();
        if (changed) ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool RevealNote(string id)
    {
        if (!_items.TryGetValue(id, out var visual) || !visual.Item.IsNote) return false;
        _selectedItem = id;
        _editingText = null;
        _ink.Select(new StrokeCollection());
        var anchor = _pageHost.TranslatePoint(new Point(visual.Item.X, visual.Item.Y), _scroll);
        var viewport = new Rect(0, 0, _scroll.ViewportWidth, _scroll.ViewportHeight);
        if (!viewport.Contains(new Rect(anchor, new Size(28, 28))))
        {
            ScrollTo(_scroll.HorizontalOffset + anchor.X - viewport.Width * .4,
                _scroll.VerticalOffset + anchor.Y - viewport.Height * .35);
        }
        SetNoteExpanded(visual.Item, true);
        return true;
    }

    private void NumberNotes()
    {
        if (_page == null) return;
        var number = 0;
        foreach (var item in _page.Items.Where(item => item.IsNote))
        {
            var visual = _items[item.Id];
            var value = (++number).ToString(CultureInfo.CurrentCulture);
            var format = Translate("NoteNumber");
            var label = format == "NoteNumber" ? $"{Translate("Note")} {value}" : string.Format(CultureInfo.CurrentCulture, format, value);
            visual.Grip.Tag = label;
            visual.Marker!.Content = value;
            visual.Marker.FontSize = number >= 1000 ? 9 : number >= 100 ? 11 : 13;
            AutomationProperties.SetName(visual.Marker, label);
            AutomationProperties.SetName(visual.Editor, label);
            AutomationProperties.SetHelpText(visual.Grip, label);
        }
    }

    private void PositionNotes()
    {
        foreach (var visual in _items.Values)
            if (visual.Item.IsNote) PositionItem(visual);
    }

    private void PositionItem(ItemVisual visual)
    {
        if (_page is null) return;
        var item = visual.Item;
        if (!item.IsNote)
        {
            // The text baseline is anchored independently of its editing grip.
            Canvas.SetLeft(visual.Root, item.X - 4);
            Canvas.SetTop(visual.Root, item.Y - 22);
            return;
        }
        Canvas.SetLeft(visual.Marker!, item.X);
        Canvas.SetTop(visual.Marker!, item.Y);
        // Notes are reading UI, not page typography. Their anchor scales with
        // the document while the marker and editor remain a readable UI size.
        visual.Root.RenderTransform = new ScaleTransform(1 / _zoom, 1 / _zoom);
        visual.Marker!.RenderTransform = new ScaleTransform(1 / _zoom, 1 / _zoom);
        // A readable card can be larger than a fitted page. Constrain it to
        // the visible workspace, not to the physical paper, so it cannot fall
        // behind the status bar. All placement remains in page coordinates.
        var origin = _pageHost.TranslatePoint(default, _scroll);
        var viewportWidth = _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : _page.Width * _zoom + 64;
        var viewportHeight = _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : _page.Height * _zoom + 64;
        var view = new Rect((8 - origin.X) / _zoom, (8 - origin.Y) / _zoom,
            Math.Max(1, viewportWidth - 16) / _zoom, Math.Max(1, viewportHeight - 16) / _zoom);
        visual.Root.Width = Math.Min(item.Width, Math.Max(40, viewportWidth - 16));
        var headerHeight = visual.Header.Height + 2;
        visual.Editor.Height = Math.Min(Math.Clamp(item.Height, 100, 300), Math.Max(32, viewportHeight - 16 - headerHeight));
        var cardWidth = visual.Root.Width / _zoom;
        var cardHeight = (visual.Editor.Height + headerHeight) / _zoom;
        var gap = 8 / _zoom;
        var markerSize = 28 / _zoom;
        double X(double value) => Math.Clamp(value, view.Left, Math.Max(view.Left, view.Right - cardWidth));
        double Y(double value) => Math.Clamp(value, view.Top, Math.Max(view.Top, view.Bottom - cardHeight));
        var candidates = new[]
        {
            new Point(item.X + markerSize + gap, Y(item.Y)),
            new Point(item.X - cardWidth - gap, Y(item.Y)),
            new Point(X(item.X), item.Y + markerSize + gap),
            new Point(X(item.X), item.Y - cardHeight - gap)
        };
        var position = candidates.FirstOrDefault(p => view.Contains(new Rect(p, new Size(cardWidth, cardHeight))),
            new Point(X(candidates[0].X), Y(candidates[0].Y)));
        Canvas.SetLeft(visual.Root, position.X);
        Canvas.SetTop(visual.Root, position.Y);
        visual.Root.Visibility = item.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        Panel.SetZIndex(visual.Root, item.IsExpanded ? 10 : 0);
        Panel.SetZIndex(visual.Marker!, 11);
    }

    private void UpdateItemStates()
    {
        foreach (var visual in _items.Values)
        {
            var item = visual.Item;
            var selected = _selectedItem == item.Id;
            if (!item.IsNote)
            {
                visual.Root.IsHitTestVisible = _tool is EditorTool.Select or EditorTool.Text or EditorTool.Eraser;
                visual.Header.Opacity = selected ? 1 : 0;
                visual.Header.IsHitTestVisible = selected;
                visual.Grip.Focusable = selected;
                visual.Root.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(108, 93, 179)) : Brushes.Transparent;
                var editing = _tool == EditorTool.Text || (selected && _editingText == item.Id);
                visual.BodyHandle!.Visibility = _tool == EditorTool.Select && !editing ? Visibility.Visible : Visibility.Collapsed;
                visual.Editor.IsReadOnly = !editing;
                visual.Editor.IsTabStop = editing;
            }
            else
            {
                visual.Root.IsHitTestVisible = _tool != EditorTool.Pan && !_spacePan;
                visual.Marker!.IsHitTestVisible = _tool != EditorTool.Pan && !_spacePan;
                visual.Editor.IsReadOnly = _tool == EditorTool.Eraser;
                visual.Marker.ToolTip = Translate(item.IsExpanded ? "CollapseNote" : "ExpandNote");
            }
        }
        var noteId = SelectedNoteId;
        if (_lastSelectedNote == noteId) return;
        _lastSelectedNote = noteId;
        NoteSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public RenderTargetBitmap RenderPage(int dpi = 96, bool includeNoteCards = false)
    {
        if (_page is null) throw new InvalidOperationException("No page is loaded.");
        CommitEdits();
        var width = checked((int)Math.Ceiling(_page.Width * dpi / 96d));
        var height = checked((int)Math.Ceiling(_page.Height * dpi / 96d));
        if (width <= 0 || height <= 0 || (long)width * height > 120_000_000)
            throw new InvalidOperationException("The requested image is too large.");
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            PaperLayer.Draw(dc, _page, _image);
            _ink.Strokes.Draw(dc);
            var noteNumber = 0;
            foreach (var item in _page.Items)
            {
                if (item.IsNote)
                {
                    var marker = new Rect(item.X, item.Y, 26, 26);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(250, 218, 150)),
                        new Pen(new SolidColorBrush(Color.FromRgb(167, 127, 54)), 1), marker, 7, 7);
                    var number = (++noteNumber).ToString(CultureInfo.CurrentCulture);
                    var label = new FormattedText(number, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                        number.Length > 2 ? 10 : 13, new SolidColorBrush(Color.FromRgb(79, 52, 16)), 1);
                    dc.DrawText(label, new Point(marker.X + (marker.Width - label.Width) / 2, marker.Y + (marker.Height - label.Height) / 2));
                    if (includeNoteCards && item.IsExpanded && _items.TryGetValue(item.Id, out var visual))
                    {
                        dc.PushTransform(new TranslateTransform(Canvas.GetLeft(visual.Root), Canvas.GetTop(visual.Root)));
                        dc.PushTransform(new ScaleTransform(1 / _zoom, 1 / _zoom));
                        dc.DrawRectangle(new VisualBrush(visual.Root), null, new Rect(visual.Root.RenderSize));
                        dc.Pop();
                        dc.Pop();
                    }
                }
                else
                {
                    var text = new FormattedText(item.Text, System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), item.FontSize,
                        new SolidColorBrush(ParseColor(item.Color)), 1)
                    {
                        MaxTextWidth = Math.Max(1, item.Width),
                        Trimming = TextTrimming.None
                    };
                    dc.DrawText(text, new Point(item.X, item.Y));
                }
            }
        }
        var result = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        result.Render(drawing);
        result.Freeze();
        return result;
    }

    private static Stroke ToStroke(InkData ink)
    {
        var points = new StylusPointCollection(ink.Points.Select(p => new StylusPoint(p.X, p.Y, Math.Clamp(p.Pressure, 0, 1))));
        if (points.Count == 0) points.Add(new StylusPoint(0, 0));
        var stroke = new Stroke(points, new DrawingAttributes
        {
            Color = ParseColor(ink.Color), Width = ink.Width, Height = ink.Width,
            IgnorePressure = false, FitToCurve = false
        });
        stroke.AddPropertyData(StrokeId, ink.Id);
        return stroke;
    }

    private static InkData FromStroke(Stroke stroke)
    {
        if (!stroke.ContainsPropertyData(StrokeId)) stroke.AddPropertyData(StrokeId, Guid.NewGuid().ToString("N"));
        return new InkData
        {
            Id = (string)stroke.GetPropertyData(StrokeId),
            Color = $"#{stroke.DrawingAttributes.Color.R:X2}{stroke.DrawingAttributes.Color.G:X2}{stroke.DrawingAttributes.Color.B:X2}",
            Width = stroke.DrawingAttributes.Width,
            Points = stroke.StylusPoints.Select(p => new InkPoint { X = p.X, Y = p.Y, Pressure = p.PressureFactor }).ToList()
        };
    }

    private static Color ParseColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch (FormatException) { return Colors.Black; }
    }

    private sealed class ItemVisual(TextItem item, Border root, TextBox editor, Grid header, Thumb grip)
    {
        public TextItem Item { get; } = item;
        public Border Root { get; } = root;
        public TextBox Editor { get; } = editor;
        public Grid Header { get; } = header;
        public Thumb Grip { get; } = grip;
        public Button? Marker { get; set; }
        public Thumb? BodyHandle { get; set; }
    }

    private sealed class PaperLayer : FrameworkElement
    {
        public PageData? Page { get; set; }
        public ImageSource? Image { get; set; }
        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Page is not null) Draw(drawingContext, Page, Image, preview: true);
        }

        public static void Draw(DrawingContext dc, PageData page, ImageSource? image, bool preview = false)
        {
            var area = new Rect(0, 0, page.Width, page.Height);
            if (preview)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, 27, 27, 44)), null, new Rect(2, 3, page.Width, page.Height));
            var paper = preview && image == null ? new SolidColorBrush(Color.FromRgb(255, 254, 251)) : Brushes.White;
            dc.DrawRectangle(paper, null, area);
            if (image is not null) { dc.DrawImage(image, area); return; }
            if (page.Pattern == PaperPattern.Blank) return;
            var spacing = page.SpacingMm * 96 / 25.4;
            var margin = page.MarginMm * 96 / 25.4;
            if (!double.IsFinite(spacing) || spacing < 1) return;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(218, 217, 214)), .6);
            pen.Freeze();
            for (var y = margin; y <= page.Height - margin + .001; y += spacing)
                dc.DrawLine(pen, new Point(margin, y), new Point(page.Width - margin, y));
            if (page.Pattern == PaperPattern.Squared)
                for (var x = margin; x <= page.Width - margin + .001; x += spacing)
                    dc.DrawLine(pen, new Point(x, margin), new Point(x, page.Height - margin));
        }
    }
}
