using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

public sealed partial class MainWindow
{
    private readonly Popup _pagePicker = new() { Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true };
    private readonly DispatcherTimer _folderRefresh = new() { Interval = TimeSpan.FromMilliseconds(175) };
    private FileSystemWatcher? _folderWatcher;
    private Button _browseButton = null!;
    private int _missingImagePosition;
    private bool _changingPage;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string first, string second);

    private static readonly IComparer<string> FolderOrder = Comparer<string>.Create((a,b) =>
    {
        int order = StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));
        return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a,b);
    });

    private static bool IsImageFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

    private void SetupFolder()
    {
        var folder = _document.Kind == DocumentKind.Image ? Path.GetDirectoryName(_document.SourceImagePath) : null;
        if (folder != null && string.Equals(_folderWatcher?.Path, folder, StringComparison.OrdinalIgnoreCase))
        {
            if (_folderRefresh.IsEnabled) RefreshFolder(); else LocateCurrentImage();
            return;
        }
        StopWatchingFolder();
        _folderImages = []; _folderIndex = -1; _missingImagePosition = 0;
        if (folder == null) return;
        try
        {
            _folderWatcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName };
            _folderWatcher.Created += FolderContentsChanged;
            _folderWatcher.Deleted += FolderContentsChanged;
            _folderWatcher.Renamed += FolderContentsChanged;
            _folderWatcher.Error += (sender,_) => ScheduleFolderRefresh(sender);
            _folderWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { StopWatchingFolder(); }
        RefreshFolder();
    }

    private void FolderContentsChanged(object sender, FileSystemEventArgs e)
    {
        if (IsImageFile(e.FullPath) || e is RenamedEventArgs renamed && IsImageFile(renamed.OldFullPath))
            ScheduleFolderRefresh(sender);
    }

    private void ScheduleFolderRefresh(object? sender)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (sender != _folderWatcher || _closing) return;
            _folderRefresh.Stop(); _folderRefresh.Start();
        }, DispatcherPriority.Background);
    }

    private void RefreshFolder()
    {
        _folderRefresh.Stop();
        var source = _document.SourceImagePath;
        if (_document.Kind != DocumentKind.Image || source == null) return;
        try { _folderImages = Directory.EnumerateFiles(Path.GetDirectoryName(source)!).Where(IsImageFile).Order(FolderOrder).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _folderImages = File.Exists(source) ? [source] : []; }
        LocateCurrentImage();
        UpdateState();
    }

    private void LocateCurrentImage()
    {
        int position = Array.BinarySearch(_folderImages, _document.SourceImagePath!, FolderOrder);
        _folderIndex = position >= 0 ? position : -1;
        _missingImagePosition = position >= 0 ? position : ~position;
    }

    private int FolderNeighbor(int direction) => _folderIndex >= 0 ? _folderIndex + direction :
        direction < 0 ? _missingImagePosition - 1 : _missingImagePosition;

    private void StopWatchingFolder()
    {
        _folderWatcher?.Dispose(); _folderWatcher = null;
        _folderRefresh.Stop();
    }

    private void GoToPage(int index) => SwitchToPage(index, reveal: true);

    private void SwitchToPage(int index, bool reveal)
    {
        if (_loading || _changingPage || index == _pageIndex || index < 0 || index >= _document.Pages.Count) return;
        _changingPage = true;
        try
        {
            _surface.CommitEdits();
            if (!CaptureHistory() || !PreparePdfPage(index)) return;
            _pageIndex = index; DisplayPage(true,reveal);
        }
        finally { _changingPage = false; }
    }

    private IEnumerable<string> BrowseLabels() => _document.Kind != DocumentKind.Image
        ? Enumerable.Range(1, _document.Pages.Count).Select(i => L.F("NotePage", i))
        : _folderImages.Select(p => Path.GetFileName(p));

    private void BrowsePositions()
    {
        if (_pagePicker.IsOpen) { _pagePicker.IsOpen = false; return; }
        bool notebook = _document.Kind != DocumentKind.Image;
        if (!notebook) RefreshFolder();
        var files = _folderImages; // Keep the popup's rows paired with their original paths during filesystem changes.
        var list = new ListBox { ItemsSource = BrowseLabels().ToArray(), SelectedIndex = notebook ? _pageIndex : _folderIndex,
            MaxHeight = Math.Clamp(ActualHeight * .45, 130, 380), BorderThickness = new Thickness(0) };
        AutomationProperties.SetName(list, L.T(notebook ? "BrowsePages" : "BrowseFolder"));
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        void OpenSelection()
        {
            int index = list.SelectedIndex;
            if (index < 0) return;
            _pagePicker.IsOpen = false;
            if (notebook) GoToPage(index);
            else if (!SamePath(files[index], _document.SourceImagePath!)) OpenFile(files[index]);
        }
        list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem)
            { e.Handled = true; OpenSelection(); }
        };
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; OpenSelection(); }
            else if (e.Key == Key.Escape) { e.Handled = true; _pagePicker.IsOpen = false; _browseButton.Focus(); }
        };
        var panel = new StackPanel { Width = Math.Min(310, ActualWidth - 40) };
        var heading = Studio.Label(L.T(notebook ? "BrowsePages" : "BrowseFolder"), 13, strong:true);
        heading.Margin = new Thickness(10,6,10,8); panel.Children.Add(heading); panel.Children.Add(list);
        var popup = new Border { Child = panel, Padding = new Thickness(5), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        popup.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush"); popup.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        _pagePicker.PlacementTarget = _browseButton; _pagePicker.Child = popup; _pagePicker.IsOpen = true;
        if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) is ListBoxItem row) row.Focus();
        else list.Focus();
    }

    private bool BeginPageChange()
    {
        if (_document.Kind != DocumentKind.Notebook) return false;
        _surface.CommitEdits();
        return CaptureHistory();
    }

    private void FinishPageChange(int pageBefore, bool reordered = false)
    {
        if (!RecordEdit(pageBefore,reordered)) return;
        ShowPage(); _recoveryTimer.Stop(); _recoveryTimer.Start();
    }

    private void DuplicatePage()
    {
        if (_document.Pages.Count >= 1000 || !BeginPageChange()) return;
        var page = DocumentStore.Clone(new DocumentData { Pages = [_document.Pages[_pageIndex]] }).Pages[0];
        foreach (var ink in page.Ink) ink.Id = Guid.NewGuid().ToString();
        foreach (var item in page.Items) item.Id = Guid.NewGuid().ToString();
        foreach (var image in page.Images) image.Id = Guid.NewGuid().ToString();
        ShiftPageViews(_pageIndex + 1, 1);
        _document.Pages.Insert(++_pageIndex, page); FinishPageChange(_pageIndex - 1);
    }

    private void MovePage(int direction)
    {
        int target = _pageIndex + direction;
        if (target < 0 || target >= _document.Pages.Count || !BeginPageChange()) return;
        var pages = _document.Pages;
        (pages[target], pages[_pageIndex]) = (pages[_pageIndex], pages[target]);
        int source = _pageIndex;
        SwapPageViews(source,target);
        _pageIndex = target; FinishPageChange(source,reordered:true);
    }

    private void SwapPageViews(int first, int second)
    {
        if (_displayedKey != null && !_surface.IsContinuous) _views[_displayedKey] = _surface.CaptureView();
        var a = $"{_sessionId}|{first}"; var b = $"{_sessionId}|{second}";
        bool hadA = _views.Remove(a, out var viewA);
        if (_views.Remove(b, out var viewB)) _views[a] = viewB;
        if (hadA) _views[b] = viewA;
        _displayedKey = null;
    }

    private void PageCommands(ItemsControl menu)
    {
        Command(menu,"BrowsePages", BrowsePositions);
        Command(menu,"AddPage", AddPage).IsEnabled = _document.Pages.Count < 1000;
        Command(menu,"DuplicatePage", DuplicatePage).IsEnabled = _document.Pages.Count < 1000;
        Command(menu,"MoveEarlier", () => MovePage(-1)).IsEnabled = _pageIndex > 0;
        Command(menu,"MoveLater", () => MovePage(1)).IsEnabled = _pageIndex + 1 < _document.Pages.Count;
        menu.Items.Add(new Separator());
        Command(menu,"Paper",EditPaper);
        Command(menu,"RemovePage",RemovePage).IsEnabled = _document.Pages.Count > 1;
    }
}
