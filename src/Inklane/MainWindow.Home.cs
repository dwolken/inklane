using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Inklane.Controls;

namespace Inklane;

public sealed partial class MainWindow
{
    private readonly StackPanel _recentRows = new();
    private Button _resume = null!, _clearRecent = null!;

    private void BuildHome()
    {
        _home.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        _home.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _home.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel { Height = 54, Margin = new Thickness(15,0,15,0) };
        var brand = BrandLockup(31);
        _startupBrand = brand;
        DockPanel.SetDock(brand,Dock.Left); header.Children.Add(brand);
        var more = MenuButton("More","More",() =>
        {
            var menu = new ContextMenu();
            Command(menu,"PasteNewNotebook",() => StartNotebookFromClipboard(),"Ctrl+V");
            menu.Items.Add(new Separator()); AddPreferences(menu); return menu;
        });
        more.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(more);
        _home.Children.Add(Edges(header,new Thickness(0,0,0,1)));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        Grid.SetRow(scroll,1); _home.Children.Add(scroll);
        var page = new StackPanel { MaxWidth = 836, Margin = new Thickness(46,42,46,32) };
        scroll.Content = page;
        var titleRow = new DockPanel { Margin = new Thickness(0,0,0,22) };
        _resume = Studio.Action("Resume","Arrow",ShowEditor,true);
        DockPanel.SetDock(_resume,Dock.Right); titleRow.Children.Add(_resume);
        titleRow.Children.Add(Studio.Label(L.T("HomeTitle"),32,strong:true)); page.Children.Add(titleRow);
        var entries = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 2, Margin = new Thickness(0,0,0,24) };
        var file = EntryCard("OpenFile","FileEntryDetail","Open",OpenDialog);
        file.Margin = new Thickness(0,0,7,0);
        var book = EntryCard("NewNotebook","BookEntryDetail","Notebook",NewNotebook);
        book.Margin = new Thickness(7,0,0,0);
        entries.Children.Add(file); entries.Children.Add(book);
        page.Children.Add(entries);
        page.Children.Add(Studio.Rule());
        var recentHeader = new DockPanel { Margin = new Thickness(0,19,0,6) };
        _clearRecent = new Button { Content = L.T("ClearRecents"), Height = 30, FontSize = 12 };
        _clearRecent.SetResourceReference(StyleProperty,"QuietButton");
        _clearRecent.Click += (_,_) => { Settings.Current.RecentFiles.Clear(); Settings.Current.OfficeReviews.Clear(); TrySaveSettings(); RefreshRecent(); };
        DockPanel.SetDock(_clearRecent,Dock.Right); recentHeader.Children.Add(_clearRecent);
        recentHeader.Children.Add(Studio.Label(L.T("Recents"),16,strong:true)); page.Children.Add(recentHeader);
        page.Children.Add(_recentRows);
    }

    private static Button EntryCard(string key, string description, string icon, Action action)
    {
        var button = new Button { MinHeight = 145, VerticalAlignment = VerticalAlignment.Stretch };
        button.SetResourceReference(StyleProperty,"CardButton");
        AutomationProperties.SetName(button,L.T(key));
        var content = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0,0,0,12) };
        var arrow = Studio.Icon("Arrow",17); arrow.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Right);
        DockPanel.SetDock(arrow,Dock.Right); top.Children.Add(arrow);
        var tile = new Border { Width = 41, Height = 41, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left,
            Child = Studio.Icon(icon,24) };
        tile.SetResourceReference(Border.BackgroundProperty,"AccentSoftBrush");
        top.Children.Add(tile);
        content.Children.Add(top);
        content.Children.Add(Studio.Label(L.T(key),21,strong:true));
        var detail = Studio.Label(L.T(description),13,"MutedBrush");
        detail.TextWrapping = TextWrapping.Wrap; detail.Margin = new Thickness(0,5,0,0); content.Children.Add(detail);
        button.Content = content; button.Click += (_,_) => action();
        return button;
    }

    private void ShowHome()
    {
        _onHome = true; _editor.Visibility = Visibility.Collapsed; _home.Visibility = Visibility.Visible;
        _resume.Visibility = _hasDocument ? Visibility.Visible : Visibility.Collapsed;
        Title = "Inklane"; RefreshRecent();
    }

    internal void ShowEditor()
    {
        StopStartupBrandMotion();
        _onHome = false; _hasDocument = true; _home.Visibility = Visibility.Collapsed; _editor.Visibility = Visibility.Visible;
        UpdateState();
    }

    private void RefreshRecent()
    {
        _recentRows.Children.Clear();
        var recent = Settings.Current.RecentFiles;
        _clearRecent.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (recent.Count == 0)
        {
            var empty = Studio.Label(L.T("RecentsEmpty"),13,"MutedBrush"); empty.Margin = new Thickness(0,6,0,0);
            _recentRows.Children.Add(empty);
        }
        foreach (var file in recent.Where(p => !string.IsNullOrWhiteSpace(p)).Take(6))
        {
            var row = new DockPanel();
            var icon = Studio.Icon(IsOfficeFile(file) || Path.GetExtension(file).Equals(".pdf",StringComparison.OrdinalIgnoreCase) ? "Notebook" : "Image",20);
            icon.Margin = new Thickness(0,0,14,0); DockPanel.SetDock(icon,Dock.Left); row.Children.Add(icon);
            var lines = new StackPanel();
            lines.Children.Add(Studio.Label(Path.GetFileName(file),14,strong:true));
            lines.Children.Add(Studio.Label(Path.GetDirectoryName(file) ?? "",11,"MutedBrush"));
            row.Children.Add(lines);
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(9,5,9,5), ToolTip = file };
            button.SetResourceReference(StyleProperty,"QuietButton"); AutomationProperties.SetName(button,Path.GetFileName(file));
            button.Click += (_,_) => OpenFile(file); _recentRows.Children.Add(button);
        }
    }

    private void RememberRecent(string file)
    {
        var recent = Settings.Current.RecentFiles;
        recent.RemoveAll(p => string.Equals(p,file,StringComparison.OrdinalIgnoreCase));
        recent.Insert(0,file);
        if (recent.Count > 6) recent.RemoveRange(6,recent.Count - 6);
        // Recent-file history is optional. It must never turn a successful document save into a failed save.
        try { Settings.Save(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
