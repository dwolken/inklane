using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Inklane.Core;

namespace Inklane;

public sealed partial class MainWindow
{
    private readonly Button _titleButton = new() { HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(1,0,5,0), MinWidth = 0, MinHeight = 0 };
    private readonly TextBox _titleEditor = new() { Visibility = Visibility.Collapsed, MaxLength = DocumentStore.MaxTitleLength, MinWidth = 60, Padding = new Thickness(4,0,4,0) };

    private UIElement BuildTitleEditor()
    {
        var grid = new Grid();
        _titleButton.SetResourceReference(StyleProperty,"QuietButton");
        _titleButton.Content = _documentTitle;
        _titleButton.Click += (_,_) => BeginRenameNotebook();
        grid.Children.Add(_titleButton); grid.Children.Add(_titleEditor);
        AutomationProperties.SetName(_titleEditor, L.T("NotebookTitle"));
        _titleEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            e.Handled = true;
            FinishRenameNotebook(e.Key == Key.Enter); _titleButton.Focus();
        };
        _titleEditor.LostKeyboardFocus += (_,_) => FinishRenameNotebook(true);
        return grid;
    }

    private void BeginRenameNotebook()
    {
        if (_document.Kind != DocumentKind.Notebook) return;
        _surface.CommitEdits();
        if (!CaptureHistory()) return;
        _titleEditor.Text = _document.Title.Length > 0 ? _document.Title : Path.GetFileNameWithoutExtension(_filePath) ?? "";
        _titleButton.Visibility = Visibility.Collapsed; _titleEditor.Visibility = Visibility.Visible;
        _titleEditor.Focus(); _titleEditor.SelectAll();
    }

    private void FinishRenameNotebook(bool commit)
    {
        if (_titleEditor.Visibility != Visibility.Visible) return;
        string title = string.Concat(_titleEditor.Text.Select(c => char.IsControl(c) ? ' ' : c)).Trim();
        _titleButton.Visibility = Visibility.Visible; _titleEditor.Visibility = Visibility.Collapsed;
        if (!commit || title == _document.Title || _document.Kind != DocumentKind.Notebook) return;
        _document.Title = title;
        DocumentChanged(); CaptureHistory();
    }

    private string SuggestedPdfName()
    {
        string title = _document.Title.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        string name = string.Concat(title.Select(ch => invalid.Contains(ch) ? '_' : ch)).TrimEnd(' ','.');
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (name.Length == 0 || stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))
            name = L.T("Untitled");
        return name.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
    }

    private string ExportStem => _document.Kind == DocumentKind.Notebook && _document.Title.Length > 0
        ? Path.GetFileNameWithoutExtension(SuggestedPdfName())
        : Path.GetFileNameWithoutExtension(DisplayName);
}
