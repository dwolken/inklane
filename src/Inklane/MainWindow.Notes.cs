using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

public sealed partial class MainWindow
{
    private Border _notesPanel = null!;
    private TextBox _noteSearch = null!;
    private readonly StackPanel _noteRows = new();

    private Border BuildNotePanel()
    {
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
            var matches = notes.Select((note,index) => (note,index)).Where(x =>
                x.note.Text.Contains(query,StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (matches.Count == 0) continue;
            if (_document.Kind == DocumentKind.Notebook)
            {
                var heading = Studio.Label(L.F("NotePage",page+1),11,"MutedBrush",true);
                heading.Margin = new Thickness(3,8,0,3); _noteRows.Children.Add(heading);
            }
            foreach (var (note,index) in matches)
            {
                var column = new DockPanel();
                var num = new Border { Width = 23, Height = 23, Margin = new Thickness(0,0,8,0), CornerRadius = new CornerRadius(6),
                    VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = (index+1).ToString(), Foreground = Brushes.Black,
                    FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 },
                    Background = new SolidColorBrush(Color.FromRgb(246,212,141)) };
                DockPanel.SetDock(num,Dock.Left); column.Children.Add(num);
                var excerpt = note.Text.Trim().Replace("\r","").Split('\n',2);
                var copy = new StackPanel();
                copy.Children.Add(Studio.Label(excerpt[0].Length > 0 ? excerpt[0] : L.T("EmptyNote"),13,strong:true));
                if (excerpt.Length > 1) copy.Children.Add(Studio.Label(excerpt[1].Replace('\n',' ').Trim(),12,"MutedBrush"));
                column.Children.Add(copy);
                var button = new Button { Content = column, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4,7,4,7) };
                button.SetResourceReference(StyleProperty,"QuietButton");
                if (page == _pageIndex && note.Id == _surface.SelectedNoteId)
                    button.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
                AutomationProperties.SetName(button,L.F("NoteNumber",index+1) + " · " + (excerpt[0].Length == 0 ? L.T("EmptyNote") : excerpt[0]));
                int targetPage = page;
                button.Click += (_,_) => JumpToNote(targetPage,note.Id);
                _noteRows.Children.Add(button);
            }
        }
        if (_noteRows.Children.Count == 0)
        {
            var empty = Studio.Label(L.T(query.Length > 0 ? "NoNoteMatches" : "NoNotes"),14,"MutedBrush");
            empty.Margin = new Thickness(0,14,0,0); _noteRows.Children.Add(empty);
        }
    }

    private void JumpToNote(int page, string id)
    {
        _surface.CommitEdits(); CaptureHistory();
        if (ActualWidth < 920 && _notesPanel.Visibility == Visibility.Visible) ToggleNotes();
        if (page != _pageIndex) { _pageIndex = page; ShowPage(); }
        _surface.UpdateLayout(); _surface.RevealNote(id);
    }
}
