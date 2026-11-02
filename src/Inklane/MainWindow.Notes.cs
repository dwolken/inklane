using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

public sealed partial class MainWindow
{
    private Border _notesPanel = null!;
    private TextBox _noteSearch = null!;
    private readonly StackPanel _noteRows = new();
    private ContextMenu _noteActions = null!;
    private sealed record NoteTarget(int Page, string Id);

    private Border BuildNotePanel()
    {
        var menu = new ContextMenu();
        void Act(Key key)
        {
            if (menu.PlacementTarget is Button { Tag: NoteTarget target }) RunNoteCommand(target,key);
        }
        Command(menu,"RenameNote",() => Act(Key.F2),"F2");
        Command(menu,"CopyObjects",() => Act(Key.C),"Ctrl+C");
        Command(menu,"CutObjects",() => Act(Key.X),"Ctrl+X");
        Command(menu,"DuplicateObjects",() => Act(Key.D),"Ctrl+D");
        Command(menu,"Delete",() => Act(Key.Delete),"Del");
        _noteActions = menu;
        var content = new DockPanel { Margin = new Thickness(14,14,14,10) };
        var top = new DockPanel { Margin = new Thickness(0,0,0,10) };
        var close = Studio.Action("CloseNotes","Close",ToggleNotes);
        DockPanel.SetDock(close,Dock.Right); top.Children.Add(close); top.Children.Add(Studio.Label(L.T("NoteIndex"),18,strong:true));
        DockPanel.SetDock(top,Dock.Top); content.Children.Add(top);
        _noteSearch = new TextBox { Margin = new Thickness(0,0,0,10), ToolTip = L.T("NoteSearch") };
        AutomationProperties.SetName(_noteSearch,L.T("NoteSearch"));
        _noteSearch.TextChanged += (_,_) => RefreshNotes();
        var searchRow = new StackPanel();
        var label = Studio.Label(L.T("NoteSearch"),11,"MutedBrush"); label.Margin = new Thickness(0,0,0,3);
        searchRow.Children.Add(label); searchRow.Children.Add(_noteSearch);
        DockPanel.SetDock(searchRow,Dock.Top); content.Children.Add(searchRow);
        var add = Studio.Action("PlaceNote","Plus",() =>
        {
            if (ActualWidth < 920) ToggleNotes();
            SetTool(EditorTool.Note); _surface.Focus();
        },true);
        add.HorizontalAlignment = HorizontalAlignment.Left; DockPanel.SetDock(add,Dock.Bottom); content.Children.Add(add);
        content.Children.Add(new ScrollViewer { Content = _noteRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        _notesPanel = Edges(content,new Thickness(1,0,0,0));
        _notesPanel.Width = 262; _notesPanel.Visibility = Visibility.Collapsed;
        return _notesPanel;
    }

    private void ToggleNotes()
    {
        bool show = _notesPanel.Visibility != Visibility.Visible;
        _notesPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _notesButton.SetResourceReference(BackgroundProperty,show ? "AccentSoftBrush" : "SurfaceBrush");
        if (show) RefreshNotes();
    }

    private void UpdateNoteCount()
    {
        int count = _document.Pages.Sum(page => page.Items.Count(item => item.IsNote));
        _noteCount.Text = count == 0 ? "" : count.ToString();
        _noteBadge.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshNotes()
    {
        if (_notesPanel == null || _notesPanel.Visibility != Visibility.Visible) return;
        _noteRows.Children.Clear();
        string query = _noteSearch.Text.Trim();
        for (int page = 0; page < _document.Pages.Count; page++)
        {
            var notes = _document.Pages[page].Items.Where(item => item.IsNote).ToList();
            var matches = notes.Select((note,index) => (note,index,title: string.IsNullOrWhiteSpace(note.Title)
                    ? L.F("NoteNumber",index+1) : note.Title)).Where(x =>
                x.title.Contains(query,StringComparison.CurrentCultureIgnoreCase) ||
                x.note.Text.Contains(query,StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (matches.Count == 0) continue;
            if (_document.Kind == DocumentKind.Notebook)
            {
                var heading = Studio.Label(L.F("NotePage",page+1),11,"MutedBrush",true);
                heading.Margin = new Thickness(3,8,0,3); _noteRows.Children.Add(heading);
            }
            foreach (var (note,index,title) in matches)
            {
                var column = new DockPanel();
                var num = new Border { Width = 23, Height = 23, Margin = new Thickness(0,0,8,0), CornerRadius = new CornerRadius(6),
                    VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = (index+1).ToString(), Foreground = Brushes.Black,
                    FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 },
                    Background = new SolidColorBrush(Color.FromRgb(246,212,141)) };
                DockPanel.SetDock(num,Dock.Left); column.Children.Add(num);
                var excerpt = note.Text.AsSpan().Trim();
                var preview = excerpt[..Math.Min(140,excerpt.Length)].ToString().ReplaceLineEndings(" ");
                var copy = new StackPanel();
                copy.Children.Add(Studio.Label(title,13,strong:true));
                if (preview.Length > 0) copy.Children.Add(Studio.Label(preview,12,"MutedBrush"));
                column.Children.Add(copy);
                var button = new Button { Content = column, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4,7,4,7) };
                button.SetResourceReference(StyleProperty,"QuietButton");
                if (page == _pageIndex && note.Id == _surface.SelectedNoteId)
                    button.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
                AutomationProperties.SetName(button,preview.Length == 0 ? title : title + " · " + preview);
                AutomationProperties.SetAutomationId(button,"NoteRow:" + note.Id);
                var target = new NoteTarget(page,note.Id);
                button.Tag = target;
                button.Click += (_,_) => JumpToNote(target.Page,target.Id);
                button.ContextMenu = _noteActions;
                _noteRows.Children.Add(button);
            }
        }
        if (_noteRows.Children.Count == 0)
        {
            var empty = Studio.Label(L.T(query.Length > 0 ? "NoNoteMatches" : "NoNotes"),14,"MutedBrush");
            empty.Margin = new Thickness(0,14,0,0); _noteRows.Children.Add(empty);
        }
    }

    private bool HandleNoteRowKey(Key key, ModifierKeys modifiers)
    {
        if (Keyboard.FocusedElement is not Button { Tag: NoteTarget target }) return false;
        if (!(modifiers == ModifierKeys.None && key is Key.F2 or Key.Delete ||
              modifiers == ModifierKeys.Control && key is Key.C or Key.X or Key.D or Key.A or Key.V)) return false;
        RunNoteCommand(target,key);
        return true;
    }

    private void RunNoteCommand(NoteTarget target, Key key)
    {
        if (!JumpToNote(target.Page,target.Id)) return;
        if (key == Key.F2) _surface.RenameNote(target.Id);
        else RunObjectCommand(key);
    }

    private bool JumpToNote(int page, string id)
    {
        _surface.CommitEdits();
        if (!CaptureHistory() || page >= _document.Pages.Count) return false;
        if (ActualWidth < 920 && _notesPanel.Visibility == Visibility.Visible) ToggleNotes();
        if (page != _pageIndex) { _pageIndex = page; ShowPage(); }
        _surface.UpdateLayout(); return _surface.RevealNote(id);
    }
}
