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

public sealed partial class MainWindow : Window
{
    private DocumentData _document = NewDocument();
    private string? _filePath, _diskHash;
    private string _savedState = "";
    private ImageSource? _image;
    private int _pageIndex;
    private string[] _folderImages = [];
    private int _folderIndex;
    private bool _loading, _saving, _closing, _pendingEdit;
    private readonly IDocumentInteraction _interaction;
    private readonly DocumentSurface _surface = new() { Translate = L.T };
    private readonly List<string> _history = [];
    private int _historyIndex;
    private readonly DispatcherTimer _editTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly string _recoveryPath = Path.Combine(App.DataDirectory, "Recovery", $"{Environment.ProcessId}-{Guid.NewGuid():N}.json");
    private readonly TextBlock _documentTitle = Studio.Label("", 15, strong: true);
    private readonly TextBlock _status = Studio.Label("", 11, "MutedBrush");
    private readonly TextBlock _pageLabel = Studio.Label("", 12);
    private readonly TextBlock _zoomLabel = Studio.Label("", 12);
    private readonly Dictionary<EditorTool, ToggleButton> _tools = [];
    private readonly List<Control> _undoControls = [], _redoControls = [], _saveControls = [];
    private Button _previous = null!, _next = null!, _addPage = null!, _paper = null!;
    private FrameworkElement _inkProperties = null!, _textProperties = null!, _strokeWidth = null!;

    public MainWindow(IDocumentInteraction? interaction = null)
    {
        _interaction = interaction ?? new DefaultDocumentInteraction();
        Title = "Inklane";
        Width = Math.Min(1200, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(880, SystemParameters.WorkArea.Height - 48);
        MinWidth = Math.Min(710,SystemParameters.WorkArea.Width - 16);
        MinHeight = Math.Min(480,SystemParameters.WorkArea.Height - 16);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 14;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        Content = BuildWorkspace();
        _surface.Changed += (_, _) => DocumentChanged();
        _surface.NoteSelectionChanged += (_, _) => RefreshNotes();
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
        ShowHome();
    }

    private static DocumentData NewDocument(PageData? page = null) => new() { Kind = DocumentKind.Notebook, Pages = [page ?? new PageData { Width = Units.MmToDip(210), Height = Units.MmToDip(297), Pattern = PaperPattern.Blank, SpacingMm = 5, MarginMm = 15 }] };
    private string DisplayName => _filePath != null ? Path.GetFileName(_filePath).Replace(".inklane.json", "", StringComparison.OrdinalIgnoreCase) : _document.Kind == DocumentKind.Image ? Path.GetFileName(_document.SourceImagePath) ?? L.T("Image") : L.T("Untitled");
    private bool IsDirty => _pendingEdit || _history.Count > 0 && _history[_historyIndex] != _savedState;

    private void SetTool(EditorTool tool)
    {
        _surface.CommitEdits(); _surface.Tool = tool;
        foreach (var pair in _tools) pair.Value.IsChecked = pair.Key == tool;
        _inkProperties.Visibility = tool is EditorTool.Pen or EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
        _strokeWidth.Visibility = tool == EditorTool.Pen ? Visibility.Visible : Visibility.Collapsed;
        _textProperties.Visibility = tool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ShowPage(bool fit = true)
    {
        _loading = true;
        _pageIndex = Math.Clamp(_pageIndex, 0, _document.Pages.Count - 1);
        LoadCurrentPage(fit);
        _loading = false;
        RefreshNotes();
        UpdateState();
    }
    private void UpdateState()
    {
        var dirty = IsDirty;
        if (!_onHome) Title = $"{(dirty ? "● " : "")}{DisplayName} — Inklane";
        _documentTitle.Text = DisplayName;
        _documentTitle.ToolTip = _filePath ?? _document.SourceImagePath;
        var state = L.T(_saving ? "Saving" : dirty ? "Unsaved" : _filePath == null ? "Ready" : "Saved");
        _status.Text = $"{L.T(_document.Kind == DocumentKind.Notebook ? "NotebookType" : "ImageType")}  ·  {state}";
        foreach (var c in _undoControls) c.IsEnabled = _historyIndex > 0;
        foreach (var c in _redoControls) c.IsEnabled = _historyIndex < _history.Count - 1;
        foreach (var c in _saveControls) c.IsEnabled = !_saving;
        var notebook = _document.Kind == DocumentKind.Notebook;
        _pageLabel.Text = notebook ? L.F("Page", _pageIndex + 1, _document.Pages.Count) : L.F("ImageNumber", _folderIndex + 1, Math.Max(1, _folderImages.Length));
        _previous.IsEnabled = notebook ? _pageIndex > 0 : _folderIndex > 0;
        _next.IsEnabled = notebook ? _pageIndex + 1 < _document.Pages.Count : _folderIndex + 1 < _folderImages.Length;
        _addPage.Visibility = _paper.Visibility = notebook ? Visibility.Visible : Visibility.Collapsed;
        _addPage.IsEnabled = _document.Pages.Count < 1000;
        UpdateNoteCount();
    }
    private void DocumentChanged()
    {
        if (_loading) return;
        try
        {
            DocumentStore.CheckPageCapacity(_document.Pages[_pageIndex]);
            _pendingEdit = true;
            UpdateState();
            _editTimer.Stop(); _editTimer.Start(); _recoveryTimer.Stop(); _recoveryTimer.Start();
        }
        catch (InvalidDataException ex)
        {
            RollBackEdit(ex);
        }
    }
    private void RollBackEdit(InvalidDataException error)
    {
        RestoreSnapshot(_history[_historyIndex]);
        ShowPage(false);
        Error(L.T("DocumentLimit"), error);
    }
    private bool CaptureHistory()
    {
        _editTimer.Stop();
        string state;
        try { state = DocumentStore.SerializeForHistory(_document); }
        catch (InvalidDataException ex) { RollBackEdit(ex); return false; }
        _pendingEdit = false;
        if (_history.Count > 0 && _history[_historyIndex] == state) { UpdateState(); return true; }
        if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(state);
        while (_history.Count > 80 || (_history.Count > 2 && _history.Sum(s => (long)s.Length) > 20_000_000)) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1; UpdateState(); RefreshNotes();
        return true;
    }
    private void ResetHistory(bool recovered = false)
    {
        _pendingEdit = false;
        _editTimer.Stop(); _recoveryTimer.Stop(); _history.Clear(); _history.Add(DocumentStore.SerializeForHistory(_document)); _historyIndex = 0; _savedState = recovered ? "" : _history[0];
    }
    private void Undo()
    {
        _surface.CommitEdits(); CaptureHistory();
        if (_historyIndex == 0) return;
        RestoreSnapshot(_history[--_historyIndex]); ShowPage(false); _recoveryTimer.Start();
    }
    private void Redo()
    {
        _surface.CommitEdits(); CaptureHistory();
        if (_historyIndex + 1 >= _history.Count) return;
        RestoreSnapshot(_history[++_historyIndex]); ShowPage(false); _recoveryTimer.Start();
    }
    private void RestoreSnapshot(string snapshot)
    {
        _pendingEdit = false;
        var expanded = _document.Pages.SelectMany(p => p.Items).Where(i => i.IsNote && i.IsExpanded).Select(i => i.Id).ToHashSet();
        var restored = DocumentStore.Deserialize(snapshot);
        if (restored.Pages.Count != _document.Pages.Count) ClearSessionViews();
        foreach (var note in restored.Pages.SelectMany(p => p.Items).Where(i => i.IsNote))
            note.IsExpanded = expanded.Contains(note.Id);
        _document = restored;
    }
    private bool CanLeave()
    {
        _surface.CommitEdits(); if (!CaptureHistory()) return false;
        if (!IsDirty) return true;
        var choice = _interaction.Choose(this, "Inklane", L.F("SavePrompt", DisplayName), L.T("Save"), L.T("Discard"), L.T("Cancel"));
        return choice == 1 || choice == 0 && Save();
    }
    private void NewNotebook()
    {
        var paper = Dialogs.Paper(this); if (paper == null || !CanLeave()) return;
        ClearRecovery(); _sessionId = Guid.NewGuid().ToString("N"); _document = NewDocument(paper); _filePath = _diskHash = null; _image = null; _pageIndex = 0; _folderImages = []; _noteSearch.Clear(); ResetHistory(); ShowEditor(); ShowPage(); SetTool(EditorTool.Pen);
    }
    private void OpenDialog()
    {
        PickFile("OpenFilter");
    }
    private void PickFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = L.T(filter), CheckFileExists = true, Multiselect = false };
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
            _sessionId = candidate.SourceImagePath ?? path;
            _noteSearch.Clear();
            SetupFolder(); ResetHistory(); ShowEditor(); ShowPage(); RememberRecent(path);
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
        ShiftPageViews(_pageIndex+1,1);
        _document.Pages.Insert(++_pageIndex, new PageData { Width = page.Width, Height = page.Height, Pattern = page.Pattern, SpacingMm = page.SpacingMm, MarginMm = page.MarginMm });
        CaptureHistory(); ShowPage(); DocumentChanged();
    }
    private void RemovePage()
    {
        if (_document.Pages.Count < 2) return;
        if (_interaction.Choose(this, L.T("RemovePage"), L.T("RemovePagePrompt"), L.T("RemovePage"), L.T("Cancel")) != 0) return;
        _surface.CommitEdits(); CaptureHistory(); ShiftPageViews(_pageIndex,-1);
        _document.Pages.RemoveAt(_pageIndex); _pageIndex = Math.Min(_pageIndex, _document.Pages.Count - 1); CaptureHistory(); ShowPage(); DocumentChanged();
    }
    private void EditPaper()
    {
        _surface.CommitEdits(); var current = _document.Pages[_pageIndex]; var next = Dialogs.Paper(this, current); if (next == null) return;
        CaptureHistory(); current.Pattern = next.Pattern; current.SpacingMm = next.SpacingMm; current.MarginMm = next.MarginMm; CaptureHistory(); ShowPage(false); DocumentChanged();
    }

    private bool Save(bool saveAs = false)
    {
        if (_saving) return false;
        _surface.CommitEdits(); if (!CaptureHistory()) return false;
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
            if (_document.Kind == DocumentKind.Notebook) SaveProgress.WriteNotebook(this, _document, _history[_historyIndex], path);
            else DocumentStore.SaveJson(_document, path);
            _filePath = path; _diskHash = DocumentStore.GetFileHash(path); _savedState = _history[_historyIndex]; ClearRecovery();
            if (_document.Kind == DocumentKind.Notebook) RenameViewSession(path);
            var source = _document.SourceImagePath;
            RememberRecent(source != null && SamePath(path, source + ".inklane.json") ? source : path);
            return true;
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
        if (_onHome)
        {
            if (ctrl && key is Key.O or Key.N)
            {
                e.Handled = true;
                if (key == Key.O) OpenDialog(); else NewNotebook();
            }
            else if (_hasDocument && key == Key.Escape) { e.Handled = true; ShowEditor(); }
            return;
        }
        if (ctrl)
        {
            Action? action = key switch
            {
                Key.N => NewNotebook, Key.O => OpenDialog, Key.S => () => Save(shift), Key.P => Print,
                Key.Z when !_surface.HasTextFocus => shift ? Redo : Undo, Key.Y when !_surface.HasTextFocus => Redo,
                Key.D1 or Key.NumPad1 => _surface.FitToWidth, Key.D0 or Key.NumPad0 => _surface.FitToPage, Key.OemPlus or Key.Add => () => _surface.ZoomBy(1.2), Key.OemMinus or Key.Subtract => () => _surface.ZoomBy(1 / 1.2), _ => null
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
        if (_saving && !App.FatalShutdown) { e.Cancel = true; return; }
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
                    _document = state.Document; _image = image; _filePath = state.FilePath; _diskHash = state.DiskHash; _pageIndex = state.PageIndex; SetupFolder(); ResetHistory(true); ShowEditor(); ShowPage(); if (WriteRecovery()) File.Delete(path); return;
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
        var startViewRendered = _onHome && _home.ActualWidth > 100;
        ShowEditor(); UpdateLayout();
        if (!startViewRendered || !IsLoaded || ActualWidth < 100 || _surface.ActualHeight < 100) throw new InvalidOperationException("Main workspace was not laid out.");
        var bitmap = _surface.RenderPage(); if (bitmap.PixelWidth < 100) throw new InvalidOperationException("Page renderer failed.");
        return new { success = true, version = App.ProductVersion, startViewRendered, windowRendered = true, width = ActualWidth, height = ActualHeight, dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX, workArea = new[] { SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height }, pagePixels = new[] { bitmap.PixelWidth, bitmap.PixelHeight }, runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(), language = L.Language, theme = Settings.Current.Appearance };
    }
}
