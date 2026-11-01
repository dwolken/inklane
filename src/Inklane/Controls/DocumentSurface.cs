using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Inklane.Core;

namespace Inklane.Controls;

public enum EditorTool { Pen, Select, Eraser, Text, Note, Pan }

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
    private bool _fit = true;
    private bool _spacePan;
    private bool _panning;
    private Point _panStart;
    private Point _scrollStart;
    private string? _selectedItem;
    private string _inkColor = "#202020";
    private double _inkWidth = 2;
    private double _zoom = 1;

    public event EventHandler? Changed;
    public event EventHandler? ViewChanged;
    public Func<string, string> Translate { get; set; } = key => key;
    public bool HasTextFocus => Keyboard.FocusedElement is TextBox box && IsAncestorOf(box);

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
        set { _fit = false; SetZoom(value, null); }
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
        SizeChanged += (_, _) =>
        {
            if (_fit) Dispatcher.BeginInvoke(FitToPage, DispatcherPriority.Loaded);
        };
        ApplyInkAttributes();
        ApplyTool();
    }

    public void LoadPage(PageData page, ImageSource? image = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        _loading = true;
        try
        {
            _page = page;
            _image = image;
            _selectedItem = null;
            _pageHost.Width = page.Width;
            _pageHost.Height = page.Height;
            _paper.Page = page;
            _paper.Image = image;
            _paper.InvalidateVisual();
            _ink.Strokes = new StrokeCollection(page.Ink.Select(ToStroke));
            _objects.Children.Clear();
            _items.Clear();
            foreach (var item in page.Items) AddItemVisual(item);
            _ink.Select(new StrokeCollection());
            AutomationProperties.SetName(_ink, Translate("PageCanvas"));
            ApplyTool();
        }
        finally { _loading = false; }
        if (_fit) Dispatcher.BeginInvoke(FitToPage, DispatcherPriority.Loaded);
    }

    public void CommitEdits()
    {
        if (_loading || _page is null) return;
        foreach (var visual in _items.Values)
        {
            visual.Item.Text = visual.Editor.Text;
        }
    }

    public void FitToPage()
    {
        if (_page is null || _scroll.ActualWidth < 100 || _scroll.ActualHeight < 100) return;
        _fit = true;
        // Use the outer viewport to avoid scrollbar feedback during fit.
        var scale = Math.Min((_scroll.ActualWidth - 68) / _page.Width,
            (_scroll.ActualHeight - 68) / _page.Height);
        SetZoom(scale, null);
        _scroll.ScrollToHorizontalOffset(0);
        _scroll.ScrollToVerticalOffset(0);
    }

    public void FitForEditing()
    {
        if (_page is null || _scroll.ActualWidth < 100 || _scroll.ActualHeight < 100) return;
        _fit = false;
        var fitWidth = (_scroll.ActualWidth - 68) / _page.Width;
        var fullFit = Math.Min(fitWidth, (_scroll.ActualHeight - 68) / _page.Height);
        SetZoom(Math.Max(fullFit, Math.Min(.8, fitWidth)), null);
        _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(0);
        _scroll.ScrollToVerticalOffset(0);
    }

    public void ZoomBy(double factor)
    {
        _fit = false;
        SetZoom(_zoom * factor, new Point(_scroll.ViewportWidth / 2, _scroll.ViewportHeight / 2));
    }

    private void SetZoom(double value, Point? focus)
    {
        if (!double.IsFinite(value)) return;
        var next = Math.Clamp(value, .1, 8);
        var ratio = next / _zoom;
        var oldX = _scroll.HorizontalOffset;
        var oldY = _scroll.VerticalOffset;
        _zoom = next;
        _scale.ScaleX = next;
        _scale.ScaleY = next;
        foreach (var visual in _items.Values.Where(v => v.Item.IsNote)) PositionItem(visual);
        if (focus is { } anchor)
        {
            _scroll.UpdateLayout();
            _scroll.ScrollToHorizontalOffset((oldX + anchor.X) * ratio - anchor.X);
            _scroll.ScrollToVerticalOffset((oldY + anchor.Y) * ratio - anchor.Y);
        }
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _fit = false;
            SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), e.GetPosition(_scroll));
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
        if (HasTextFocus) return;
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
            MinHeight = item.IsNote ? 100 : Math.Max(24, item.FontSize * 1.4),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(item.IsNote ? 12 : 0),
            Background = Brushes.Transparent,
            Foreground = item.IsNote ? new SolidColorBrush(Color.FromRgb(53, 43, 24)) : new SolidColorBrush(ParseColor(item.Color)),
            SelectionBrush = new SolidColorBrush(Color.FromRgb(118, 172, 222)),
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
        var root = new Border
        {
            Width = item.Width + (item.IsNote ? 0 : 8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = item.IsNote ? new SolidColorBrush(Color.FromRgb(255, 248, 220)) : Brushes.Transparent,
            CornerRadius = new CornerRadius(item.IsNote ? 7 : 2),
            Padding = new Thickness(item.IsNote ? 0 : 3),
            Focusable = false
        };
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Height = item.IsNote ? 30 : 18 };
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
            var collapse = new Button
            {
                Content = "−", Width = 30, Height = 28, MinHeight = 28, MinWidth = 0, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(87, 67, 28)),
                FontSize = 18, ToolTip = Translate("CollapseNote")
            };
            AutomationProperties.SetName(collapse, Translate("CollapseNote"));
            Grid.SetColumn(collapse, 1);
            header.Children.Add(collapse);
            collapse.Click += (_, _) => SetNoteExpanded(item, false);
            editor.Height = Math.Clamp(item.Height, 100, 300);
            root.BorderBrush = new SolidColorBrush(Color.FromRgb(218, 185, 104));
        }
        Grid.SetRow(editor, 1);
        panel.Children.Add(header);
        panel.Children.Add(editor);
        root.Child = panel;
        _objects.Children.Add(root);
        var visual = new ItemVisual(item, root, editor, header);
        _items.Add(item.Id, visual);
        if (item.IsNote)
        {
            var marker = CreateMarker(item);
            visual.Marker = marker;
            _objects.Children.Add(marker);
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
            _selectedItem = item.Id;
            UpdateItemStates();
        };
        grip.DragStarted += (_, _) =>
        {
            CommitEdits();
            dragOrigin = new Point(item.X, item.Y);
            _selectedItem = item.Id;
            _ink.Select(new StrokeCollection());
            UpdateItemStates();
        };
        grip.DragDelta += (_, e) => MoveItem(visual,
            e.HorizontalChange / (item.IsNote ? _zoom : 1),
            e.VerticalChange / (item.IsNote ? _zoom : 1));
        grip.DragCompleted += (_, e) =>
        {
            if (e.Canceled)
            {
                item.X = dragOrigin.X;
                item.Y = dragOrigin.Y;
                PositionItem(visual);
            }
            else NotifyChanged();
        };
        grip.KeyDown += (_, e) => MoveWithKeyboard(visual, e);
    }

    private static ControlTemplate GripTemplate(bool note)
    {
        var template = new ControlTemplate(typeof(Thumb));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "GripBorder";
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BackgroundProperty, note
            ? new SolidColorBrush(Color.FromRgb(248, 230, 179))
            : new SolidColorBrush(Color.FromArgb(230, 231, 237, 245)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "⋮⋮");
        text.SetValue(TextBlock.FontSizeProperty, 12d);
        text.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(96, 92, 78)));
        text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(text);
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
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
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
            Content = "●", FontSize = 13, Width = 28, Height = 28, MinHeight = 28, MinWidth = 0,
            Padding = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(255, 222, 131)),
            Foreground = new SolidColorBrush(Color.FromRgb(127, 86, 12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(192, 145, 48)), BorderThickness = new Thickness(1),
            ToolTip = Translate("ExpandNote"), Cursor = Cursors.Hand
        };
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
        item.IsExpanded = expanded;
        PositionItem(_items[item.Id]);
        UpdateItemStates();
        NotifyChanged();
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
        var cardWidth = item.Width / _zoom;
        var cardHeight = (item.Height + 32) / _zoom;
        var gap = 8 / _zoom;
        var cardX = item.X + 36 / _zoom;
        var cardY = Math.Clamp(item.Y, gap, Math.Max(gap, _page.Height - cardHeight - gap));
        if (cardX + cardWidth > _page.Width - gap)
        {
            cardX = item.X - cardWidth - gap;
            if (cardX < gap)
            {
                // A fitted portrait page may be narrower than a card plus its
                // marker. Put the card below/above so neither covers the other.
                cardX = Math.Clamp(item.X, gap, Math.Max(gap, _page.Width - cardWidth - gap));
                cardY = item.Y + 36 / _zoom;
                if (cardY + cardHeight > _page.Height - gap && item.Y - cardHeight - gap >= gap)
                    cardY = item.Y - cardHeight - gap;
            }
        }
        Canvas.SetLeft(visual.Root, cardX);
        Canvas.SetTop(visual.Root, cardY);
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
                visual.Root.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(58, 111, 190)) : Brushes.Transparent;
                visual.Editor.IsReadOnly = _tool == EditorTool.Eraser;
            }
            else
            {
                visual.Root.IsHitTestVisible = _tool != EditorTool.Pan && !_spacePan;
                visual.Marker!.IsHitTestVisible = _tool != EditorTool.Pan && !_spacePan;
                visual.Editor.IsReadOnly = _tool == EditorTool.Eraser;
                visual.Marker.ToolTip = Translate(item.IsExpanded ? "CollapseNote" : "ExpandNote");
            }
        }
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
            foreach (var item in _page.Items)
            {
                if (item.IsNote)
                {
                    var marker = new Rect(item.X, item.Y, 26, 26);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 222, 131)),
                        new Pen(new SolidColorBrush(Color.FromRgb(192, 145, 48)), 1), marker, 4, 4);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(127, 86, 12)), null,
                        new Point(item.X + 13, item.Y + 13), 3, 3);
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

    private sealed class ItemVisual(TextItem item, Border root, TextBox editor, Grid header)
    {
        public TextItem Item { get; } = item;
        public Border Root { get; } = root;
        public TextBox Editor { get; } = editor;
        public Grid Header { get; } = header;
        public Button? Marker { get; set; }
    }

    private sealed class PaperLayer : FrameworkElement
    {
        public PageData? Page { get; set; }
        public ImageSource? Image { get; set; }
        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Page is not null) Draw(drawingContext, Page, Image);
        }

        public static void Draw(DrawingContext dc, PageData page, ImageSource? image)
        {
            var area = new Rect(0, 0, page.Width, page.Height);
            dc.DrawRectangle(Brushes.White, null, area);
            if (image is not null) { dc.DrawImage(image, area); return; }
            if (page.Pattern == PaperPattern.Blank) return;
            var spacing = page.SpacingMm * 96 / 25.4;
            var margin = page.MarginMm * 96 / 25.4;
            if (!double.IsFinite(spacing) || spacing < 1) return;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(219, 226, 232)), .6);
            pen.Freeze();
            for (var y = margin; y <= page.Height - margin + .001; y += spacing)
                dc.DrawLine(pen, new Point(margin, y), new Point(page.Width - margin, y));
            if (page.Pattern == PaperPattern.Squared)
                for (var x = margin; x <= page.Width - margin + .001; x += spacing)
                    dc.DrawLine(pen, new Point(x, margin), new Point(x, page.Height - margin));
        }
    }
}
