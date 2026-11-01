using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Inklane.Controls;
using Inklane.Core;
using Microsoft.Win32;

namespace Inklane;

public sealed class MainWindow : Window
{
    private DocumentData _document = NewDocument();
    private string? _filePath, _diskHash;
    private string _savedState = "";
    private ImageSource? _image;
    private int _pageIndex;
    private string[] _folderImages = [];
    private int _folderIndex;
    private bool _loading, _saving, _closing;
    private readonly IDocumentInteraction _interaction;
    private readonly DocumentSurface _surface = new() { Translate = L.T };
    private readonly List<string> _history = [];
    private int _historyIndex;
    private readonly DispatcherTimer _editTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly string _recoveryPath = Path.Combine(App.DataDirectory, "Recovery", $"{Environment.ProcessId}-{Guid.NewGuid():N}.json");
    private readonly TextBlock _documentTitle = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _kind = new() { FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3) };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _zoomLabel = new() { Width = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<EditorTool, ToggleButton> _tools = [];
    private readonly List<Control> _undoControls = [], _redoControls = [], _saveControls = [];
    private Button _previous = null!, _next = null!, _addPage = null!, _removePage = null!, _paper = null!;
    private FrameworkElement _inkProperties = null!, _textProperties = null!, _strokeWidth = null!;

    public MainWindow(IDocumentInteraction? interaction = null)
    {
        _interaction = interaction ?? new DefaultDocumentInteraction();
        Title = "Inklane";
        Width = Math.Min(1200, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(880, SystemParameters.WorkArea.Height - 48);
        MinWidth = 760; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        Content = BuildWorkspace();
        _surface.Changed += (_, _) => DocumentChanged();
        _surface.ViewChanged += (_, _) => _zoomLabel.Text = $"{_surface.Zoom:P0}";
        _editTimer.Tick += (_, _) => { _editTimer.Stop(); CaptureHistory(); };
        _recoveryTimer.Tick += (_, _) => { _recoveryTimer.Stop(); WriteRecovery(); };
        PreviewKeyDown += OnKey;
        Closing += OnClosing;
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) OpenFile(paths[0]); };
        ResetHistory();
        ShowPage();
        SetTool(EditorTool.Pen);
    }

    private static DocumentData NewDocument(PageData? page = null) => new() { Kind = DocumentKind.Notebook, Pages = [page ?? new PageData { Width = Units.MmToDip(210), Height = Units.MmToDip(297), Pattern = PaperPattern.Blank, SpacingMm = 5, MarginMm = 15 }] };
    private string DisplayName => _filePath != null ? Path.GetFileName(_filePath).Replace(".inklane.json", "", StringComparison.OrdinalIgnoreCase) : _document.Kind == DocumentKind.Image ? Path.GetFileName(_document.SourceImagePath) ?? L.T("Image") : L.T("Untitled");
    private bool IsDirty => DocumentStore.Serialize(_document) != _savedState;

    private UIElement BuildWorkspace()
    {
        var root = new DockPanel();
        var menu = new Menu { Padding = new Thickness(12, 2, 12, 2) };
        MenuItem Top(string key) { var m = new MenuItem { Header = L.T(key) }; menu.Items.Add(m); return m; }
        MenuItem Item(ItemsControl parent, string key, Action action, string? shortcut = null)
        {
            var m = new MenuItem { Header = L.T(key), InputGestureText = shortcut ?? "" };
            m.Click += (_, _) => action(); parent.Items.Add(m); return m;
        }
        var file = Top("File");
        Item(file, "NewNotebook", NewNotebook, "Ctrl+N"); Item(file, "Open", OpenDialog, "Ctrl+O");
        file.Items.Add(new Separator());
        _saveControls.Add(Item(file, "Save", () => Save(), "Ctrl+S")); Item(file, "SaveAs", () => Save(true), "Ctrl+Shift+S");
        Item(file, "ExportImage", ExportImage); Item(file, "ExportJson", ExportJson); Item(file, "Print", Print, "Ctrl+P"); file.Items.Add(new Separator()); Item(file, "Exit", Close);
        var edit = Top("Edit"); _undoControls.Add(Item(edit, "Undo", Undo, "Ctrl+Z")); _redoControls.Add(Item(edit, "Redo", Redo, "Ctrl+Y")); Item(edit, "Delete", _surface.DeleteSelection, "Delete");
        var view = Top("View"); Item(view, "Fit", _surface.FitToPage, "Ctrl+0"); Item(view, "ZoomIn", () => _surface.ZoomBy(1.2), "Ctrl++"); Item(view, "ZoomOut", () => _surface.ZoomBy(1 / 1.2), "Ctrl+-");
        var appearance = new MenuItem { Header = L.T("Appearance") }; view.Items.Add(appearance);
        foreach (var key in new[] { "System", "Light", "Dark" })
        {
            var m = Item(appearance, key, () => { Settings.Current.Appearance = key; Appearance.Apply(); TrySaveSettings(); foreach (MenuItem sibling in appearance.Items) sibling.IsChecked = Equals(sibling.Tag, key); });
            m.Tag = key; m.IsCheckable = true; m.IsChecked = Settings.Current.Appearance == key;
        }
        var language = new MenuItem { Header = L.T("Language") }; view.Items.Add(language);
        foreach (var (code, name) in new[] { ("System", L.T("System")), ("en", "English"), ("de", "Deutsch") })
        {
            var m = new MenuItem { Header = name, IsCheckable = true, IsChecked = Settings.Current.Language == code, Tag = code };
            m.Click += (_, _) => { Settings.Current.Language = code; TrySaveSettings(); foreach (MenuItem sibling in language.Items) sibling.IsChecked = Equals(sibling.Tag, code); MessageBox.Show(this, L.T("LanguageRestart"), "Inklane"); }; language.Items.Add(m);
        }
        var help = Top("Help"); Item(help, "Shortcuts", () => MessageBox.Show(this, L.T("ShortcutText"), L.T("Shortcuts"))); Item(help, "About", () => MessageBox.Show(this, L.T("AboutText"), L.T("About")));
        DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);

        var header = new Grid { Margin = new Thickness(20, 10, 20, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var doc = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) }; doc.Children.Add(_kind); doc.Children.Add(_documentTitle); _kind.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        header.Children.Add(doc);
        var fileActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        fileActions.Children.Add(Button("NewNotebook", NewNotebook)); fileActions.Children.Add(Button("Open", OpenDialog));
        var save = Button("Save", () => Save()); _saveControls.Add(save); fileActions.Children.Add(save); Grid.SetColumn(fileActions, 1); header.Children.Add(fileActions);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var toolbar = new WrapPanel { Margin = new Thickness(16, 8, 16, 8) };
        foreach (var tool in Enum.GetValues<EditorTool>())
        {
            var b = new ToggleButton { Content = L.T(tool.ToString()), Margin = new Thickness(2), Padding = new Thickness(12, 8, 12, 8), MinHeight = 36, ToolTip = L.T(tool + "Hint") };
            AutomationProperties.SetName(b, L.T(tool.ToString()));
            b.Click += (_, _) => SetTool(tool); _tools[tool] = b; toolbar.Children.Add(b);
        }
        toolbar.Children.Add(new Border { Width = 1, Height = 28, Margin = new Thickness(12, 0, 12, 0), Background = (Brush)FindResource("BorderBrush") });
        var ink = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var color = new ComboBox { Width = 105, Margin = new Thickness(4, 0, 8, 0) };
        foreach (var (label, hex) in new[] { ("Black", "#202020"), ("Blue", "#245DD7"), ("Red", "#C3343F"), ("Green", "#16734A"), ("Purple", "#7947AB") }) color.Items.Add(new ComboBoxItem { Content = L.T(label), Tag = hex });
        color.SelectedIndex = 0; color.SelectionChanged += (_, _) => _surface.InkColor = (string)((ComboBoxItem)color.SelectedItem).Tag;
        AutomationProperties.SetName(color, L.T("Color")); ink.Children.Add(color);
        var width = new ComboBox { Width = 82, MinWidth = 70, ItemsSource = new[] { "1 px", "2 px", "4 px", "8 px" }, SelectedIndex = 1 };
        width.SelectionChanged += (_, _) => _surface.InkWidth = double.Parse(((string)width.SelectedItem).Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        AutomationProperties.SetName(width, L.T("Width")); width.ToolTip = L.T("Width"); ink.Children.Add(width); _strokeWidth = width; _inkProperties = ink; toolbar.Children.Add(ink);
        var textProps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var size = new ComboBox { Width = 85, ItemsSource = new[] { "12 pt", "18 pt", "24 pt", "32 pt", "48 pt" }, SelectedIndex = 1, Margin = new Thickness(4, 0, 10, 0) };
        _surface.TextSize = 24;
        size.SelectionChanged += (_, _) => _surface.TextSize = double.Parse(((string)size.SelectedItem).Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture) * 96 / 72; AutomationProperties.SetName(size, L.T("TextSize")); size.ToolTip = L.T("TextSize"); textProps.Children.Add(size); _textProperties = textProps; toolbar.Children.Add(textProps);
        var undo = Button("Undo", Undo); var redo = Button("Redo", Redo); _undoControls.Add(undo); _redoControls.Add(redo); toolbar.Children.Add(undo); toolbar.Children.Add(redo);
        var toolBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 1), Child = toolbar }; toolBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); DockPanel.SetDock(toolBorder, Dock.Top); root.Children.Add(toolBorder);

        var navigation = new WrapPanel { Margin = new Thickness(18, 8, 18, 8) };
        _previous = Button("Previous", () => Navigate(-1)); _next = Button("Next", () => Navigate(1)); navigation.Children.Add(_previous); navigation.Children.Add(_pageLabel); navigation.Children.Add(_next);
        _addPage = Button("AddPage", AddPage); _removePage = Button("RemovePage", RemovePage); _paper = Button("Paper", EditPaper);
        _addPage.Margin = new Thickness(20, 2, 2, 2); navigation.Children.Add(_addPage); navigation.Children.Add(_removePage); navigation.Children.Add(_paper); DockPanel.SetDock(navigation, Dock.Top); root.Children.Add(navigation);

        var bottom = new Grid { Margin = new Thickness(18, 7, 18, 7) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.Margin = new Thickness(0, 0, 20, 0); bottom.Children.Add(_status); _hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Grid.SetColumn(_hint, 1); bottom.Children.Add(_hint);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal };
        var minus = Button("ZoomOut", () => _surface.ZoomBy(1 / 1.2)); minus.Content = "−"; var plus = Button("ZoomIn", () => _surface.ZoomBy(1.2)); plus.Content = "+";
        zoom.Children.Add(minus); zoom.Children.Add(_zoomLabel); zoom.Children.Add(plus); zoom.Children.Add(Button("Fit", _surface.FitToPage)); Grid.SetColumn(zoom, 2); bottom.Children.Add(zoom);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        _surface.SetResourceReference(BackgroundProperty, "WorkspaceBrush"); root.Children.Add(_surface);
        return root;
    }

    private static Button Button(string key, Action action)
    {
        var b = new Button { Content = L.T(key), Margin = new Thickness(2), ToolTip = L.T(key) };
        AutomationProperties.SetName(b, L.T(key)); b.Click += (_, _) => action(); return b;
    }
    private void SetTool(EditorTool tool)
    {
        _surface.CommitEdits(); _surface.Tool = tool;
        foreach (var pair in _tools) pair.Value.IsChecked = pair.Key == tool;
        _inkProperties.Visibility = tool is EditorTool.Pen or EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
        _strokeWidth.Visibility = tool == EditorTool.Pen ? Visibility.Visible : Visibility.Collapsed;
        _textProperties.Visibility = tool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
        _hint.Text = L.T(tool + "Hint");
    }
    private void ShowPage(bool fit = true)
    {
        _loading = true;
        _pageIndex = Math.Clamp(_pageIndex, 0, _document.Pages.Count - 1);
        _surface.LoadPage(_document.Pages[_pageIndex], _image);
        _loading = false;
        if (fit) Dispatcher.BeginInvoke(() => { if (_document.Kind == DocumentKind.Notebook) _surface.FitForEditing(); else _surface.FitToPage(); }, DispatcherPriority.Loaded);
        UpdateState();
    }
    private void UpdateState()
    {
        var dirty = IsDirty;
        Title = $"{(dirty ? "● " : "")}{DisplayName} — Inklane"; _documentTitle.Text = DisplayName; _documentTitle.ToolTip = _filePath ?? _document.SourceImagePath; _kind.Text = L.T(_document.Kind.ToString());
        _status.Text = L.T(_saving ? "Saving" : dirty ? "Unsaved" : _filePath == null ? "Ready" : "Saved");
        foreach (var c in _undoControls) c.IsEnabled = _historyIndex > 0;
        foreach (var c in _redoControls) c.IsEnabled = _historyIndex < _history.Count - 1;
        foreach (var c in _saveControls) c.IsEnabled = !_saving;
        var notebook = _document.Kind == DocumentKind.Notebook;
        _pageLabel.Text = notebook ? L.F("Page", _pageIndex + 1, _document.Pages.Count) : L.F("ImageNumber", _folderIndex + 1, Math.Max(1, _folderImages.Length));
        _previous.IsEnabled = notebook ? _pageIndex > 0 : _folderIndex > 0;
        _next.IsEnabled = notebook ? _pageIndex + 1 < _document.Pages.Count : _folderIndex + 1 < _folderImages.Length;
        _addPage.Visibility = _removePage.Visibility = _paper.Visibility = notebook ? Visibility.Visible : Visibility.Collapsed;
        _addPage.IsEnabled = _document.Pages.Count < 1000;
        _removePage.IsEnabled = _document.Pages.Count > 1;
    }
    private void DocumentChanged()
    {
        if (_loading) return;
        try
        {
            UpdateState();
            _editTimer.Stop(); _editTimer.Start(); _recoveryTimer.Stop(); _recoveryTimer.Start();
        }
        catch (InvalidDataException ex)
        {
            _document = DocumentStore.Deserialize(_history[_historyIndex]);
            ShowPage(false);
            Error(L.T("DocumentLimit"), ex);
        }
    }
    private void CaptureHistory()
    {
        _editTimer.Stop();
        var state = DocumentStore.Serialize(_document);
        if (_history.Count > 0 && _history[_historyIndex] == state) return;
        if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(state);
        while (_history.Count > 80 || (_history.Count > 2 && _history.Sum(s => (long)s.Length) > 20_000_000)) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1; UpdateState();
    }
    private void ResetHistory(bool recovered = false)
    {
        _editTimer.Stop(); _recoveryTimer.Stop(); _history.Clear(); _history.Add(DocumentStore.Serialize(_document)); _historyIndex = 0; _savedState = recovered ? "" : _history[0];
    }
    private void Undo()
    {
        _surface.CommitEdits(); CaptureHistory();
        if (_historyIndex == 0) return;
        _document = DocumentStore.Deserialize(_history[--_historyIndex]); ShowPage(false); _recoveryTimer.Start();
    }
    private void Redo()
    {
        _surface.CommitEdits(); CaptureHistory();
        if (_historyIndex + 1 >= _history.Count) return;
        _document = DocumentStore.Deserialize(_history[++_historyIndex]); ShowPage(false); _recoveryTimer.Start();
    }
    private bool CanLeave()
    {
        _surface.CommitEdits(); CaptureHistory();
        if (!IsDirty) return true;
        var choice = _interaction.Choose(this, "Inklane", L.F("SavePrompt", DisplayName), L.T("Save"), L.T("Discard"), L.T("Cancel"));
        return choice == 1 || choice == 0 && Save();
    }
    private void NewNotebook()
    {
        var paper = Dialogs.Paper(this); if (paper == null || !CanLeave()) return;
        ClearRecovery(); _document = NewDocument(paper); _filePath = _diskHash = null; _image = null; _pageIndex = 0; _folderImages = []; ResetHistory(); ShowPage(); SetTool(EditorTool.Pen);
    }
    private void OpenDialog()
    {
        var dialog = new OpenFileDialog { Filter = L.T("OpenFilter"), CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) OpenFile(dialog.FileName);
    }
    public void OpenFile(string path)
    {
        if (!CanLeave()) return;
        try
        {
            path = Path.GetFullPath(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            DocumentData candidate; ImageSource? image = null; string? savePath; string? hash;
            if (extension == ".pdf") { candidate = DocumentStore.LoadPdf(path); savePath = candidate.Kind == DocumentKind.Notebook ? path : null; hash = savePath != null ? DocumentStore.GetFileHash(path) : null; }
            else if (extension == ".json") { candidate = DocumentStore.LoadJson(path); savePath = candidate.Kind == DocumentKind.Image ? path : null; hash = savePath != null ? DocumentStore.GetFileHash(path) : null; }
            else if (extension is ".png" or ".jpg" or ".jpeg")
            {
                image = LoadImage(path);
                var sidecar = path + ".inklane.json";
                candidate = File.Exists(sidecar) ? DocumentStore.LoadJson(sidecar) : new DocumentData { Kind = DocumentKind.Image, SourceImagePath = path, SourceImageSha256 = DocumentStore.GetFileHash(path), Pages = [new PageData { Width = ((BitmapSource)image).PixelWidth, Height = ((BitmapSource)image).PixelHeight }] };
                if (candidate.Kind != DocumentKind.Image || !string.Equals(candidate.SourceImageSha256, DocumentStore.GetFileHash(path), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("ImageChanged"));
                candidate.SourceImagePath = path;
                savePath = File.Exists(sidecar) ? sidecar : null; hash = File.Exists(sidecar) ? DocumentStore.GetFileHash(sidecar) : null;
            }
            else throw new NotSupportedException();
            if (candidate.Kind == DocumentKind.Image && image == null)
            {
                var source = candidate.SourceImagePath;
                if (!File.Exists(source)) source = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(source ?? ""));
                if (!File.Exists(source) || !string.Equals(DocumentStore.GetFileHash(source), candidate.SourceImageSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("ImageChanged"));
                candidate.SourceImagePath = source; image = LoadImage(source);
            }
            ClearRecovery(); _document = candidate; _filePath = savePath; _diskHash = hash; _image = image; _pageIndex = 0;
            SetupFolder(); ResetHistory(); ShowPage();
        }
        catch (NotSupportedException) { Error(L.T("UnsupportedPdf")); }
        catch (Exception ex) when (IsExpected(ex)) { Error(L.T("OpenFailed"), ex); }
    }
    private void SetupFolder()
    {
        _folderImages = []; _folderIndex = 0;
        if (_document.Kind != DocumentKind.Image || _document.SourceImagePath == null) return;
        try { _folderImages = Directory.EnumerateFiles(Path.GetDirectoryName(_document.SourceImagePath)!).Where(p => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase).ToArray(); _folderIndex = Math.Max(0, Array.FindIndex(_folderImages, p => SamePath(p, _document.SourceImagePath))); }
        catch (IOException) { _folderImages = [_document.SourceImagePath]; }
        catch (UnauthorizedAccessException) { _folderImages = [_document.SourceImagePath]; }
    }
    private static BitmapSource LoadImage(string path)
    {
        var info = new FileInfo(path); if (info.Length > 256 * 1024 * 1024) throw new InvalidDataException("Image exceeds 256 MB.");
        using var input = File.OpenRead(path);
        var frame = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if ((long)frame.PixelWidth * frame.PixelHeight > 100_000_000) throw new InvalidDataException("Image exceeds 100 megapixels.");
        BitmapSource source = frame;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            try
            {
                var orientation = metadata.GetQuery("/app1/ifd/{ushort=274}");
                var matrix = orientation is ushort o ? o switch
                {
                    2 => new Matrix(-1, 0, 0, 1, 0, 0), 3 => new Matrix(-1, 0, 0, -1, 0, 0), 4 => new Matrix(1, 0, 0, -1, 0, 0),
                    5 => new Matrix(0, 1, 1, 0, 0, 0), 6 => new Matrix(0, 1, -1, 0, 0, 0), 7 => new Matrix(0, -1, -1, 0, 0, 0), 8 => new Matrix(0, -1, 1, 0, 0, 0), _ => Matrix.Identity
                } : Matrix.Identity;
                if (!matrix.IsIdentity) source = new TransformedBitmap(frame, new MatrixTransform(matrix));
            } catch (NotSupportedException) { }
        }
        source.Freeze(); return source;
    }
    private void Navigate(int direction)
    {
        if (_document.Kind == DocumentKind.Image)
        {
            var index = _folderIndex + direction; if (index >= 0 && index < _folderImages.Length) OpenFile(_folderImages[index]); return;
        }
        _surface.CommitEdits(); CaptureHistory(); var next = _pageIndex + direction;
        if (next >= 0 && next < _document.Pages.Count) { _pageIndex = next; ShowPage(); }
    }
    private void AddPage()
    {
        if (_document.Pages.Count >= 1000) return;
        _surface.CommitEdits(); CaptureHistory(); var page = _document.Pages[_pageIndex];
        _document.Pages.Insert(++_pageIndex, new PageData { Width = page.Width, Height = page.Height, Pattern = page.Pattern, SpacingMm = page.SpacingMm, MarginMm = page.MarginMm });
        CaptureHistory(); ShowPage(); DocumentChanged();
    }
    private void RemovePage()
    {
        if (_document.Pages.Count < 2) return;
        if (_interaction.Choose(this, L.T("RemovePage"), L.T("RemovePagePrompt"), L.T("RemovePage"), L.T("Cancel")) != 0) return;
        _surface.CommitEdits(); CaptureHistory(); _document.Pages.RemoveAt(_pageIndex); _pageIndex = Math.Min(_pageIndex, _document.Pages.Count - 1); CaptureHistory(); ShowPage(); DocumentChanged();
    }
    private void EditPaper()
    {
        _surface.CommitEdits(); var current = _document.Pages[_pageIndex]; var next = Dialogs.Paper(this, current); if (next == null) return;
        CaptureHistory(); current.Pattern = next.Pattern; current.SpacingMm = next.SpacingMm; current.MarginMm = next.MarginMm; CaptureHistory(); ShowPage(false); DocumentChanged();
    }

    private bool Save(bool saveAs = false)
    {
        _surface.CommitEdits(); CaptureHistory();
        try
        {
        var path = _filePath;
        if (!saveAs && path != null && File.Exists(path) && _diskHash != DocumentStore.GetFileHash(path))
        { _interaction.ShowMessage(this, L.T("ExternalChange"), "Inklane"); saveAs = true; }
        if (_document.Kind == DocumentKind.Image && path == null && !saveAs)
        {
            path = _document.SourceImagePath + ".inklane.json";
            if (File.Exists(path)) { _interaction.ShowMessage(this, L.T("ExternalChange"), "Inklane"); saveAs = true; }
        }
        if (saveAs || path == null)
        {
            var notebook = _document.Kind == DocumentKind.Notebook;
            var dialog = new SaveFileDialog { Filter = L.T(notebook ? "PdfFilter" : "JsonFilter"), DefaultExt = notebook ? ".pdf" : ".json", AddExtension = true, FileName = path == null ? notebook ? L.T("Untitled") + ".pdf" : Path.GetFileName(_document.SourceImagePath) + ".inklane.json" : Path.GetFileName(path), OverwritePrompt = true };
            path = _interaction.ChooseSavePath(this, dialog); if (path == null) return false;
            if (_filePath == null && _document.Kind == DocumentKind.Image && SamePath(path, _document.SourceImagePath + ".inklane.json") && File.Exists(path)) { Error(L.T("ExternalChange")); return false; }
            if (saveAs && _filePath != null && SamePath(path, _filePath) && File.Exists(path) && _diskHash != DocumentStore.GetFileHash(path)) { Error(L.T("ExternalChange")); return false; }
        }
            if (!DestinationAllowed(path, _document.Kind == DocumentKind.Notebook ? ".pdf" : ".json")) return false;
            if (_document.Kind == DocumentKind.Image && (_document.SourceImagePath == null || !File.Exists(_document.SourceImagePath) || !string.Equals(DocumentStore.GetFileHash(_document.SourceImagePath), _document.SourceImageSha256, StringComparison.OrdinalIgnoreCase)))
            { Error(L.T("ImageChanged")); return false; }
            _saving = true; UpdateState();
            if (_document.Kind == DocumentKind.Notebook) DocumentStore.SavePdf(_document, path); else DocumentStore.SaveJson(_document, path);
            _filePath = path; _diskHash = DocumentStore.GetFileHash(path); _savedState = DocumentStore.Serialize(_document); ClearRecovery(); return true;
        }
        catch (Exception ex) when (IsExpected(ex)) { Error(L.T("SaveFailed"), ex); return false; }
        finally { _saving = false; UpdateState(); }
    }
    private void ExportJson()
    {
        _surface.CommitEdits(); var dialog = new SaveFileDialog { Filter = L.T("JsonFilter"), DefaultExt = ".json", FileName = Path.GetFileNameWithoutExtension(DisplayName) + ".annotations.json" };
        var path = _interaction.ChooseSavePath(this, dialog); if (path == null || !DestinationAllowed(path, ".json", true)) return;
        try { DocumentStore.SaveJson(_document, path); }
        catch (Exception ex) when (IsExpected(ex)) { Error(L.T("SaveFailed"), ex); }
    }
    private void ExportImage()
    {
        _surface.CommitEdits(); var dialog = new SaveFileDialog { Filter = L.T("PngFilter"), DefaultExt = ".png", FileName = Path.GetFileNameWithoutExtension(DisplayName) + "-annotated.png" };
        var path = _interaction.ChooseSavePath(this, dialog); if (path == null || !DestinationAllowed(path, ".png", true)) return;
        try
        {
            var bitmap = _surface.RenderPage(_document.Kind == DocumentKind.Image ? 96 : 150); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            AtomicWrite(path, stream => encoder.Save(stream));
        }
        catch (Exception ex) when (IsExpected(ex)) { Error(L.T("SaveFailed"), ex); }
    }
    private bool ProtectsOriginal(string path)
    {
        if (_document.SourceImagePath != null && SamePath(path, _document.SourceImagePath)) { Error(L.T("ProtectOriginal")); return true; } return false;
    }
    private bool DestinationAllowed(string path, string extension, bool export = false)
    {
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)) { Error(L.F("WrongExtension", extension)); return false; }
        if (export && _filePath != null && SamePath(path, _filePath)) { Error(L.T("ProtectDocument")); return false; }
        return !ProtectsOriginal(path);
    }
    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private void Print()
    {
        _surface.CommitEdits();
        try
        {
            var first = _document.Pages[_pageIndex];
            var dialog = new PrintDialog { UserPageRangeEnabled = false };
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(Math.Min(first.Width, first.Height) < Units.MmToDip(160) ? PageMediaSizeName.ISOA5 : PageMediaSizeName.ISOA4);
            dialog.PrintTicket.PageOrientation = first.Width > first.Height ? PageOrientation.Landscape : PageOrientation.Portrait;
            if (dialog.ShowDialog() != true) return;
            var pages = new FixedDocument();
            foreach (var page in _document.Pages)
            {
                var renderer = new DocumentSurface { Translate = L.T }; renderer.LoadPage(page, _image);
                var bitmap = renderer.RenderPage(150);
                var fixedPage = new FixedPage { Width = page.Width, Height = page.Height, Background = Brushes.White };
                fixedPage.Children.Add(new Image { Source = bitmap, Width = page.Width, Height = page.Height });
                var content = new PageContent { Child = fixedPage }; pages.Pages.Add(content);
            }
            dialog.PrintDocument(pages.DocumentPaginator, DisplayName);
        }
        catch (Exception ex) when (IsExpected(ex) || ex is PrintSystemException) { Error(L.T("Error"), ex); }
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control); var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl)
        {
            Action? action = key switch
            {
                Key.N => NewNotebook, Key.O => OpenDialog, Key.S => () => Save(shift), Key.P => Print,
                Key.Z when !_surface.HasTextFocus => shift ? Redo : Undo, Key.Y when !_surface.HasTextFocus => Redo,
                Key.D0 or Key.NumPad0 => _surface.FitToPage, Key.OemPlus or Key.Add => () => _surface.ZoomBy(1.2), Key.OemMinus or Key.Subtract => () => _surface.ZoomBy(1 / 1.2), _ => null
            };
            if (action != null) { e.Handled = true; action(); } return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && key is Key.Left or Key.Right) { e.Handled = true; Navigate(key == Key.Left ? -1 : 1); return; }
        if (_surface.HasTextFocus || Keyboard.FocusedElement is TextBoxBase || Keyboard.Modifiers != ModifierKeys.None) return;
        EditorTool? tool = key switch { Key.P => EditorTool.Pen, Key.V => EditorTool.Select, Key.E => EditorTool.Eraser, Key.T => EditorTool.Text, Key.N => EditorTool.Note, Key.H => EditorTool.Pan, _ => null };
        if (tool.HasValue) { SetTool(tool.Value); e.Handled = true; }
        else if (key == Key.Delete) { _surface.DeleteSelection(); e.Handled = true; }
    }
    public void StopBackgroundWork() { _editTimer.Stop(); _recoveryTimer.Stop(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.SmokeTest || App.FatalShutdown || _closing) return;
        if (!CanLeave()) { e.Cancel = true; return; }
        _closing = true; ClearRecovery();
    }
    private bool WriteRecovery()
    {
        if (App.SmokeTest) return false;
        if (!IsDirty) { ClearRecovery(); return true; }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recoveryPath)!);
            var state = new Recovery { Document = _document, FilePath = _filePath, DiskHash = _diskHash, PageIndex = _pageIndex, SavedAt = DateTimeOffset.Now, ProcessId = Environment.ProcessId };
            AtomicWrite(_recoveryPath, s => JsonSerializer.Serialize(s, state));
            return true;
        }
        catch (Exception ex) when (IsExpected(ex)) { _status.Text = L.T("RecoveryFailed"); return false; }
    }
    public void OfferRecovery()
    {
        var directory = Path.GetDirectoryName(_recoveryPath)!; if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                if (new FileInfo(path).Length > 40_000_000) continue;
                var state = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(path)); if (state?.Document == null) continue;
                try { using var process = Process.GetProcessById(state.ProcessId); if (process.ProcessName == "Inklane") continue; } catch (ArgumentException) { }
                DocumentStore.Deserialize(DocumentStore.Serialize(state.Document));
                var choice = _interaction.Choose(this, L.T("Recovery"), L.F("RecoveryPrompt", state.SavedAt.LocalDateTime.ToString("g")), L.T("Restore"), L.T("Discard"), L.T("Cancel"));
                if (choice == 0)
                {
                    var image = state.Document.Kind == DocumentKind.Image ? LoadImage(state.Document.SourceImagePath!) : null;
                    if (image != null && state.Document.SourceImageSha256 != DocumentStore.GetFileHash(state.Document.SourceImagePath!)) throw new InvalidDataException(L.T("ImageChanged"));
                    _document = state.Document; _image = image; _filePath = state.FilePath; _diskHash = state.DiskHash; _pageIndex = state.PageIndex; SetupFolder(); ResetHistory(true); ShowPage(); if (WriteRecovery()) File.Delete(path); return;
                }
                if (choice == 1) { File.Delete(path); continue; }
                return;
            }
            catch (Exception ex) when (IsExpected(ex)) { Error(L.T("OpenFailed"), ex); return; }
        }
    }
    private void ClearRecovery()
    {
        _recoveryTimer.Stop();
        try { if (File.Exists(_recoveryPath)) File.Delete(_recoveryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private sealed class Recovery
    {
        public DocumentData? Document { get; set; }
        public string? FilePath { get; set; }
        public string? DiskHash { get; set; }
        public int PageIndex { get; set; }
        public DateTimeOffset SavedAt { get; set; }
        public int ProcessId { get; set; }
    }
    internal static void AtomicWrite(string path, Action<Stream> write)
    {
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".inklane-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(stream); stream.Flush(true); }
            if (File.Exists(path))
            {
                for (var attempt = 0; ; attempt++)
                {
                    try { File.Replace(temp, path, null); break; }
                    catch (IOException ex) when (attempt < 3 && (ex.HResult & 0xffff) is 32 or 33 or 1175) { Thread.Sleep(40 * (1 << attempt)); }
                }
            }
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static bool IsExpected(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or System.Security.SecurityException or JsonException or System.Runtime.InteropServices.COMException or FormatException or OutOfMemoryException;
    private void Error(string message, Exception? exception = null)
    {
        if (exception != null)
        {
            try { Directory.CreateDirectory(App.DataDirectory); File.AppendAllText(Path.Combine(App.DataDirectory, "error.log"), $"{DateTimeOffset.Now:O}\n{exception}\n"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        _interaction.ShowMessage(this, message, L.T("Error"), MessageBoxImage.Error);
    }
    private void TrySaveSettings() { try { Settings.Save(); } catch (Exception ex) when (IsExpected(ex)) { Error(L.T("SaveFailed"), ex); } }
    public object RunStartupSmokeCheck()
    {
        if (!IsLoaded || ActualWidth < 100 || _surface.ActualHeight < 100) throw new InvalidOperationException("Main workspace was not laid out.");
        var bitmap = _surface.RenderPage(); if (bitmap.PixelWidth < 100) throw new InvalidOperationException("Page renderer failed.");
        return new { success = true, version = "0.1.0-alpha.1", windowRendered = true, width = ActualWidth, height = ActualHeight, dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX, workArea = new[] { SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height }, pagePixels = new[] { bitmap.PixelWidth, bitmap.PixelHeight }, runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(), language = L.Language, theme = Settings.Current.Appearance };
    }
}
