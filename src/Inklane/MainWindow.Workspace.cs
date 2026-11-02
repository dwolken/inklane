using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

public sealed partial class MainWindow
{
    private readonly Grid _editor = new();
    private readonly Grid _home = new();
    private bool _onHome, _hasDocument;
    private Button _notesButton = null!;
    private readonly TextBlock _noteCount = Studio.Label("", 12, "MutedBrush");
    private readonly Border _noteBadge = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(5,0,5,1), Margin = new Thickness(2,0,0,0) };
    private readonly Dictionary<string, PageViewport> _views = new(StringComparer.OrdinalIgnoreCase);
    private string _sessionId = Guid.NewGuid().ToString("N");
    private string? _displayedKey, _displayedSession;

    private UIElement BuildWorkspace()
    {
        var host = new Grid { UseLayoutRounding = true, SnapsToDevicePixels = true };
        host.Children.Add(_editor);
        host.Children.Add(_home);
        _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _editor.RowDefinitions.Add(new RowDefinition());
        _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(10, 0, 12, 0), Height = 54 };
        var home = BrandButton(ShowHome);
        DockPanel.SetDock(home, Dock.Left); header.Children.Add(home);
        header.Children.Add(Studio.Rule(true));
        var actions = Studio.Line();
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var open = Studio.Action("OpenShort", "Open", OpenDialog, true);
        var save = Studio.Action("Save", "Save", () => Save(), true);
        _saveControls.Add(save);
        actions.Children.Add(open); actions.Children.Add(save); actions.Children.Add(Studio.Rule(true));
        _notesButton = Studio.Action("NoteIndex", "Notes", ToggleNotes);
        _noteBadge.Child = _noteCount;
        _noteBadge.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        _notesButton.Content = Studio.Line(Studio.Icon("Notes"), InlineCaption(L.T("Notes")), _noteBadge);
        actions.Children.Add(_notesButton);
        actions.Children.Add(MenuButton("More", "More", EditorMenu));
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5,0,8,0) };
        title.Children.Add(BuildTitleEditor()); title.Children.Add(_status); header.Children.Add(title);
        _editor.Children.Add(Edges(header, new Thickness(0,0,0,1)));

        var body = new DockPanel();
        Grid.SetRow(body, 1); _editor.Children.Add(body);
        var rail = BuildTools();
        DockPanel.SetDock(rail, Dock.Left); body.Children.Add(rail);
        var notes = BuildNotePanel();
        DockPanel.SetDock(notes, Dock.Right); body.Children.Add(notes);
        _surface.SetResourceReference(BackgroundProperty, "WorkspaceBrush");
        body.Children.Add(_surface);
        var footer = BuildFooter();
        Grid.SetRow(footer, 2); _editor.Children.Add(footer);
        BuildHome();
        return host;
    }

    private static Border Edges(UIElement child, Thickness border)
    {
        var result = new Border { Child = child, BorderThickness = border };
        result.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        result.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        return result;
    }

    private static TextBlock InlineCaption(string text) =>
        new() { Text = text, Margin = new Thickness(6,0,4,0), VerticalAlignment = VerticalAlignment.Center };

    private static Image BrandMark(int size)
    {
        var uri = new Uri("pack://application:,,,/Inklane;component/Assets/inklane.ico");
        var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - size * 2)).First();
        return new Image { Source = frame, Width = size, Height = size, Stretch = Stretch.Uniform };
    }

    private static Button BrandButton(Action action)
    {
        var mark = BrandMark(29);
        var word = Studio.Label("Inklane", 17, strong: true);
        word.Margin = new Thickness(7,0,1,0);
        var button = new Button { Content = Studio.Line(mark,word), ToolTip = L.T("Start"), Padding = new Thickness(4) };
        button.SetResourceReference(StyleProperty, "QuietButton");
        AutomationProperties.SetName(button, L.T("Start"));
        button.Click += (_, _) => action();
        return button;
    }

    private UIElement BuildTools()
    {
        var rail = new DockPanel { Width = 70, Margin = new Thickness(0,8,0,6) };
        var history = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var undo = Studio.Action("Undo", "Undo", Undo);
        var redo = Studio.Action("Redo", "Redo", Redo);
        _undoControls.Add(undo); _redoControls.Add(redo);
        history.Children.Add(Studio.Rule()); history.Children.Add(undo); history.Children.Add(redo);
        DockPanel.SetDock(history, Dock.Bottom); rail.Children.Add(history);
        var stack = new StackPanel();
        foreach (var tool in new[] { EditorTool.Select, EditorTool.Pen, EditorTool.Eraser, EditorTool.Text, EditorTool.Note, EditorTool.Pan })
        {
            var name = tool.ToString();
            var caption = L.T(tool == EditorTool.Select ? "SelectShort" : tool == EditorTool.Pan ? "PanShort" : name);
            var content = new StackPanel();
            content.Children.Add(Studio.Icon(name, 21));
            content.Children.Add(new TextBlock { Text = caption, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,0) });
            var button = new ToggleButton { Content = content, Height = 46, Margin = new Thickness(6,1,6,1), ToolTip = L.T(name + "Hint") };
            button.SetResourceReference(StyleProperty, "ToolButton");
            AutomationProperties.SetName(button, L.T(name));
            button.Click += (_, _) => SetTool(tool);
            _tools.Add(tool, button); stack.Children.Add(button);
        }
        rail.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        return Edges(rail, new Thickness(0,0,1,0));
    }

    private FrameworkElement BuildFooter()
    {
        var bar = new DockPanel { Height = 42, Margin = new Thickness(9,0,9,0) };
        var nav = Studio.Line();
        DockPanel.SetDock(nav, Dock.Left); bar.Children.Add(nav);
        _previous = Studio.Action("Previous", "Previous", () => Navigate(-1));
        _next = Studio.Action("Next", "Next", () => Navigate(1));
        _browseButton = Studio.Action("BrowsePages","Down",BrowsePositions);
        _browseButton.Content = Studio.Line(_pageLabel, Studio.Icon("Down",11));
        _browseButton.Padding = new Thickness(4,0,4,0);
        _browseButton.ContextMenu = new ContextMenu();
        _browseButton.ContextMenuOpening += (_, e) =>
        {
            var menu = _browseButton.ContextMenu; menu.Items.Clear();
            if (_document.Kind == DocumentKind.Notebook) PageCommands(menu);
            else Command(menu,_document.Kind == DocumentKind.Pdf ? "BrowsePages" : "BrowseFolder",BrowsePositions);
        };
        nav.Children.Add(_previous); nav.Children.Add(_browseButton); nav.Children.Add(_next);
        _addPage = Studio.Action("AddPage", "Plus", AddPage); nav.Children.Add(_addPage);
        _paper = Studio.Action("Paper", "Paper", EditPaper); nav.Children.Add(_paper);
        nav.Children.Add(Studio.Rule(true));
        var zoom = Studio.Line(Studio.Action("ZoomOut", "Minus", () => _surface.ZoomBy(1 / 1.2)));
        var fit = MenuButton("Zoom", "Zoom", ZoomMenu);
        fit.Content = Studio.Line(_zoomLabel, Studio.Icon("Down",12));
        _zoomLabel.MinWidth = 40; _zoomLabel.TextAlignment = TextAlignment.Center;
        zoom.Children.Add(fit); zoom.Children.Add(Studio.Action("ZoomIn", "Plus", () => _surface.ZoomBy(1.2)));
        DockPanel.SetDock(zoom, Dock.Right); bar.Children.Add(zoom);
        var properties = Studio.Line();
        var ink = Studio.Line();
        var color = new ComboBox { Width = 136, MinWidth = 0, MinHeight = 28, Height = 30, ToolTip = L.T("Color") };
        foreach (var (key, hex) in new[] { ("Black","#202020"), ("Blue","#245DD7"), ("Red","#C3343F"), ("Green","#16734A"), ("Purple","#7947AB") })
        {
            var chip = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(5.5),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BorderBrushProperty, "MutedBrush");
            var item = new ComboBoxItem { Content = Studio.Line(chip, InlineCaption(L.T(key))), Tag = hex };
            AutomationProperties.SetName(item, L.T(key)); color.Items.Add(item);
        }
        color.SelectedIndex = 0;
        color.SelectionChanged += (_, _) => _surface.InkColor = (string)((ComboBoxItem)color.SelectedItem).Tag;
        AutomationProperties.SetName(color, L.T("Color")); ink.Children.Add(color);
        var width = SizeChoice("Width", new[] {1,2,4,8}, "px", 1, v => _surface.InkWidth = v);
        ink.Children.Add(width); _strokeWidth = width;
        _inkProperties = ink; properties.Children.Add(ink);
        _textProperties = SizeChoice("TextSize", new[] {12,18,24,32,48}, "pt", 1, v => _surface.TextSize = v * 96d / 72);
        _surface.TextSize = 24; properties.Children.Add(_textProperties);
        bar.Children.Add(properties);
        return Edges(bar, new Thickness(0,1,0,0));
    }

    private static ComboBox SizeChoice(string key, int[] sizes, string unit, int selected, Action<int> change)
    {
        var box = new ComboBox { Width = 94, MinWidth = 0, Height = 30, MinHeight = 28, Margin = new Thickness(4,0,0,0),
            ToolTip = L.T(key), ItemsSource = sizes.Select(s => $"{s} {unit}").ToArray(), SelectedIndex = selected };
        AutomationProperties.SetName(box, L.T(key));
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) change(sizes[box.SelectedIndex]); };
        return box;
    }

    private static Button MenuButton(string key, string icon, Func<ContextMenu> create)
    {
        Button? button = null;
        button = Studio.Action(key, icon, () =>
        {
            var menu = create(); menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        });
        return button;
    }

    private static MenuItem Command(ItemsControl host, string key, Action action, string shortcut = "")
    {
        var item = new MenuItem { Header = L.T(key), InputGestureText = shortcut };
        item.Click += (_, _) => action(); host.Items.Add(item);
        return item;
    }

    private ContextMenu ZoomMenu()
    {
        var menu = new ContextMenu();
        Command(menu,"WritingView",_surface.FitForEditing);
        Command(menu,"FitWidth",_surface.FitToWidth,"Ctrl+1");
        Command(menu,"Fit",_surface.FitToPage,"Ctrl+0");
        Command(menu,"ActualSize",() => _surface.Zoom = 1);
        return menu;
    }

    private ContextMenu EditorMenu()
    {
        var menu = new ContextMenu();
        Command(menu, "OpenImage", () => PickFile("ImageFilter"));
        Command(menu, "NewNotebook", NewNotebook, "Ctrl+N");
        Command(menu, "SaveAs", () => Save(true), "Ctrl+Shift+S");
        menu.Items.Add(new Separator());
        AddClipboardMenu(menu);
        if (_document.Kind == DocumentKind.Notebook)
        {
            Command(menu,"RenameNotebook",BeginRenameNotebook);
            var pages = new MenuItem { Header = L.T("ManagePages") }; PageCommands(pages); menu.Items.Add(pages);
        }
        else Command(menu,_document.Kind == DocumentKind.Pdf ? "BrowsePages" : "BrowseFolder",BrowsePositions);
        menu.Items.Add(new Separator());
        Command(menu,"ExportFeedback",ExportFeedback).ToolTip = L.T("FeedbackHint");
        Command(menu,"ExportImage",ExportImage); Command(menu,"ExportJson",ExportJson);
        Command(menu,"Print",Print,"Ctrl+P");
        menu.Items.Add(new Separator());
        AddPreferences(menu);
        return menu;
    }

    private void AddPreferences(ContextMenu menu)
    {
        var appearance = new MenuItem { Header = L.T("Appearance") };
        foreach (var key in new[] { "System","Light","Dark" })
        {
            var item = Command(appearance, key, () => { Settings.Current.Appearance = key; Appearance.Apply(); TrySaveSettings(); });
            item.IsCheckable = true; item.IsChecked = Settings.Current.Appearance == key;
        }
        var language = new MenuItem { Header = L.T("Language") };
        foreach (var (code,name) in new[] { ("System",L.T("System")), ("en","English"), ("de","Deutsch") })
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = Settings.Current.Language == code };
            item.Click += (_,_) =>
            {
                Settings.Current.Language = code; TrySaveSettings();
                MessageBox.Show(this,L.T("LanguageRestart"),"Inklane");
            };
            language.Items.Add(item);
        }
        menu.Items.Add(appearance); menu.Items.Add(language);
        Command(menu, "Shortcuts", () => MessageBox.Show(this,L.T("ShortcutText"),L.T("Shortcuts")));
        Command(menu, "About", () => MessageBox.Show(this,$"Inklane {App.ProductVersion}\n\n{L.T("AboutText")}",L.T("About")));
    }

    private void RenameViewSession(string path)
    {
        if (_sessionId.Equals(path, StringComparison.OrdinalIgnoreCase)) return;
        string prefix = _sessionId + "|";
        foreach (var pair in _views.Where(v => v.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _views.Remove(pair.Key);
            _views[path + "|" + pair.Key[prefix.Length..]] = pair.Value;
        }
        _sessionId = _displayedSession = path;
        _displayedKey = $"{path}|{_pageIndex}";
    }

    private void ClearSessionViews()
    {
        string prefix = _sessionId + "|";
        foreach (var key in _views.Keys.Where(k => k.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray())
            _views.Remove(key);
        _displayedKey = null;
    }

    private void ShiftPageViews(int start, int direction)
    {
        string prefix = _sessionId + "|";
        var shifted = _views.Where(v => v.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            .Select(v => (v.Key, Index: int.Parse(v.Key[prefix.Length..]), v.Value))
            .Where(v => v.Index >= start).ToArray();
        foreach (var entry in shifted) _views.Remove(entry.Key);
        foreach (var entry in shifted)
            if (direction > 0 || entry.Index != start) _views[prefix + (entry.Index + direction)] = entry.Value;
        if (direction < 0) _displayedKey = null;
    }

    private void LoadCurrentPage(bool switchPage)
    {
        var key = $"{_sessionId}|{_pageIndex}";
        PageViewport? previous = null;
        if (_displayedKey != null)
        {
            previous = _surface.CaptureView();
            _views[_displayedKey] = previous.Value;
            if (_views.Count > 1000) _views.Remove(_views.Keys.First());
        }
        PageViewport? next = _views.TryGetValue(key, out var saved) ? saved : null;
        if (!switchPage) next = previous;
        else if (next == null && _displayedSession == _sessionId) next = previous?.AtTop();
        if (next == null && _document.Kind == DocumentKind.Pdf) next = new PageViewport(PageViewMode.Editing,1,0,0);
        _surface.LoadPage(_document.Pages[_pageIndex], _image, next);
        _displayedKey = key; _displayedSession = _sessionId;
        var image = _document.Kind == DocumentKind.Image;
        AutomationProperties.SetName(_previous,L.T(image ? "PreviousImage" : "PreviousPage"));
        AutomationProperties.SetName(_next,L.T(image ? "NextImage" : "NextPage"));
        _previous.ToolTip = AutomationProperties.GetName(_previous) + (image ? " (←)" : " (Alt+←)");
        _next.ToolTip = AutomationProperties.GetName(_next) + (image ? " (→)" : " (Alt+→)");
    }
}
