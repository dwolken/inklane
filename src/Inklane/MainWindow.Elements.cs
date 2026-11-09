using System.IO;
using System.Windows;
using System.Windows.Controls;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

public sealed partial class MainWindow
{
    private string ElementDirectory => Path.Combine(App.DataDirectory, "Elements");
    private sealed record LibraryEntry(string Path, string Name)
    {
        public override string ToString() => Name;
    }

    private string? NameElement(string value)
    {
        var window = new Window { Owner = this, Title = L.T("ElementName"), Width = 380, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(22) };
        var input = new TextBox { Text = value, MaxLength = 200, Margin = new Thickness(0, 8, 0, 14) };
        var category = new TextBox { MaxLength = 80, Margin = new Thickness(0, 8, 0, 14) };
        var split = value.IndexOf(" / ", StringComparison.Ordinal);
        if (split >= 0) { category.Text = value[..split]; input.Text = value[(split + 3)..]; }
        panel.Children.Add(Studio.Label(L.T("ElementName"))); panel.Children.Add(input);
        panel.Children.Add(Studio.Label(L.T("ElementCategory"))); panel.Children.Add(category);
        var save = new Button { Content = L.T("Save"), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        panel.Children.Add(save); window.Content = panel;
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text) && input.Text.Length + category.Text.Length < 197) window.DialogResult = true; };
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? (string.IsNullOrWhiteSpace(category.Text) ? "" : category.Text.Trim() + " / ") + input.Text.Trim() : null;
    }

    private void SaveElement()
    {
        if (!_surface.HasSelection) return;
        var name = NameElement(""); if (name == null) return;
        try
        {
            var document = _surface.ExportSelectedObjects(); document.Title = name;
            Directory.CreateDirectory(ElementDirectory);
            DocumentStore.SaveJson(document, Path.Combine(ElementDirectory, Guid.NewGuid().ToString("N") + ".inklane-element.json"));
        }
        catch (Exception ex) when (IsExpected(ex)) { Error(L.T("ElementFailed"), ex); }
    }

    private void ElementLibrary()
    {
        var window = new Window { Owner = this, Title = L.T("ElementLibrary"), Width = 460, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new DockPanel { Margin = new Thickness(20) }; window.Content = panel;
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        System.Windows.Automation.AutomationProperties.SetName(search, L.T("Search"));
        var list = new ListBox(); var entries = new List<LibraryEntry>();
        try
        {
            if (Directory.Exists(ElementDirectory)) foreach (var path in Directory.EnumerateFiles(ElementDirectory, "*.inklane-element.json").Take(1000))
            {
                try { entries.Add(new LibraryEntry(path, DocumentStore.LoadJson(path).Title)); }
                catch (Exception ex) when (IsExpected(ex)) { entries.Add(new LibraryEntry(path, Path.GetFileNameWithoutExtension(path))); }
            }
        }
        catch (Exception ex) when (IsExpected(ex)) { Error(L.T("ElementFailed"), ex); return; }
        void Refresh()
        {
            list.ItemsSource = entries.Where(e => e.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).OrderBy(e => e.Name).ToList();
        }
        search.TextChanged += (_, _) => Refresh(); Refresh();
        var actions = Studio.Line(); actions.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var insert = new Button { Content = L.T("Insert"), IsDefault = true, MinWidth = 90 };
        var rename = new Button { Content = L.T("Rename"), Margin = new Thickness(8, 0, 0, 0) };
        var remove = new Button { Content = L.T("Delete"), Margin = new Thickness(8, 0, 0, 0) };
        actions.Children.Add(insert); actions.Children.Add(rename); actions.Children.Add(remove);
        if (entries.Count == 0) { var hint = Studio.Label(L.T("ElementEmpty")); hint.TextWrapping = TextWrapping.Wrap; panel.Children.Add(hint); }
        else panel.Children.Add(list);
        void Insert()
        {
            if (list.SelectedItem is not LibraryEntry entry) return;
            try
            {
                var document = DocumentStore.LoadJson(entry.Path);
                if (document.Kind != DocumentKind.Notebook || document.Pages.Count != 1) throw new InvalidDataException();
                window.DialogResult = true;
                ChangeObjects(() => _surface.PasteObjects(new DocumentSurface.PasteContent(document.Pages[0], true), null));
            }
            catch (Exception ex) when (IsExpected(ex)) { Error(L.T("ElementFailed"), ex); }
        }
        insert.Click += (_, _) => Insert(); list.MouseDoubleClick += (_, _) => Insert();
        rename.Click += (_, _) =>
        {
            if (list.SelectedItem is not LibraryEntry entry) return;
            var name = NameElement(entry.Name); if (name == null) return;
            try { var doc = DocumentStore.LoadJson(entry.Path); doc.Title = name; DocumentStore.SaveJson(doc, entry.Path); entries.Remove(entry); entries.Add(entry with { Name = name }); Refresh(); }
            catch (Exception ex) when (IsExpected(ex)) { Error(L.T("ElementFailed"), ex); }
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is not LibraryEntry entry) return;
            try
            {
                // Move to a local recoverable library trash; inserted document objects are unaffected.
                var trash = Path.Combine(ElementDirectory, "Trash"); Directory.CreateDirectory(trash);
                File.Move(entry.Path, Path.Combine(trash, Guid.NewGuid().ToString("N") + ".inklane-element.json")); entries.Remove(entry); Refresh();
            }
            catch (Exception ex) when (IsExpected(ex)) { Error(L.T("ElementFailed"), ex); }
        };
        window.ShowDialog();
    }
}
