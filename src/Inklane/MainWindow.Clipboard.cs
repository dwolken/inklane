using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

public sealed partial class MainWindow
{
    private void InitializeObjectCommands()
    {
        _surface.RequestSelectTool = () => SetTool(EditorTool.Select);
        _surface.ValidateInsertedContent = () => DocumentStore.Serialize(_document);
        _surface.CreateObjectMenu = point =>
        {
            var menu = new ContextMenu();
            PopulateObjectMenu(menu, point);
            return menu;
        };
    }

    private void AddClipboardMenu(ItemsControl host)
    {
        var edit = new MenuItem { Header = L.T("Edit") };
        PopulateObjectMenu(edit, null);
        host.Items.Add(edit);
    }

    private void PopulateObjectMenu(ItemsControl menu, Point? point)
    {
        var selected = _surface.HasSelection;
        if (_surface.HasSingleText) Command(menu, "EditTextObject", _surface.EditSelectedText, "F2");
        if (_surface.HasSingleObject && _surface.SelectedNoteId is { } id)
            Command(menu, "RenameNote", () => _surface.RenameNote(id), "F2");
        Command(menu, "CutObjects", () => RunObjectCommand(Key.X), "Ctrl+X").IsEnabled = selected;
        Command(menu, "CopyObjects", () => RunObjectCommand(Key.C), "Ctrl+C").IsEnabled = selected;
        Command(menu, "PasteObjects", () => RunObjectCommand(Key.V, point), "Ctrl+V");
        Command(menu, "DuplicateObjects", () => RunObjectCommand(Key.D), "Ctrl+D").IsEnabled = selected;
        Command(menu, "Delete", () => RunObjectCommand(Key.Delete), "Del").IsEnabled = selected;
        Command(menu, "SelectAllObjects", () => RunObjectCommand(Key.A), "Ctrl+A");
        menu.Items.Add(new Separator());
        Command(menu, _document.Kind == DocumentKind.Image ? "CopyAnnotatedPicture" : "CopyPagePicture",
            () => RunObjectCommand(Key.PrintScreen));
        if (_document.Kind == DocumentKind.Image)
            Command(menu, "CopyOriginalPicture", CopyOriginalPicture);
    }

    private void CopyOriginalPicture()
    {
        if (_image is not BitmapSource source) return;
        try { DocumentSurface.CopyBitmap(source); }
        catch (Exception ex) when (IsExpected(ex)) { Error(L.T("ClipboardFailed"), ex); }
    }

    private bool HandleObjectKey(Key key, ModifierKeys modifiers)
    {
        if (_onHome || _saving || Keyboard.FocusedElement is TextBoxBase or ComboBox) return false;
        if (_noteRows.IsKeyboardFocusWithin) return HandleNoteRowKey(key, modifiers);
        if (_surface.IsKeyboardFocusWithin && (key == Key.Apps && modifiers == ModifierKeys.None ||
            key == Key.F10 && modifiers == ModifierKeys.Shift))
        {
            _surface.ShowObjectMenu();
            return true;
        }
        if (modifiers == ModifierKeys.Control && key is Key.C or Key.X or Key.V or Key.D or Key.A)
        {
            RunObjectCommand(key);
            return true;
        }
        if (modifiers == ModifierKeys.None && key is Key.Delete or Key.Back && _surface.HasSelection)
        {
            RunObjectCommand(Key.Delete);
            return true;
        }
        if (modifiers == ModifierKeys.None && key == Key.F2 && _surface.SelectedNoteId is { } id)
        {
            _surface.RenameNote(id);
            return true;
        }
        if (modifiers is ModifierKeys.None or ModifierKeys.Shift && _surface.IsKeyboardFocusWithin && _surface.HasSelection)
        {
            if (key == Key.Escape) { _surface.ClearObjectSelection(); return true; }
            return _surface.NudgeSelection(key, modifiers == ModifierKeys.Shift);
        }
        return false;
    }

    private void RunObjectCommand(Key key, Point? point = null)
    {
        if (_saving) return;
        try
        {
            _surface.CommitEdits();
            if (_surface.HasTextFocus) _surface.Focus();
            if (key == Key.PrintScreen) { _surface.CopyPagePicture(); return; }
            if (key == Key.C)
            {
                if (_surface.HasSelection) _surface.CopyObjects();
                else if (_document.Kind == DocumentKind.Image) _surface.CopyPagePicture();
                return;
            }
            if (key == Key.A) { _surface.SelectAllObjects(); return; }
            if (key != Key.V && !_surface.HasSelection || !CaptureHistory()) return;
            switch (key)
            {
                case Key.X: _surface.CopyObjects(); _surface.DeleteSelection(); break;
                case Key.Delete: _surface.DeleteSelection(); break;
                case Key.D: _surface.DuplicateObjects(); break;
                case Key.V: _surface.PasteObjects(_surface.ReadClipboardObjects(_document.Pages[_pageIndex]), point); break;
            }
            CaptureHistory();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Error(L.T(ex is InvalidDataException ? "ClipboardInvalid" : ex is NotSupportedException ? "ClipboardUnsupported" : "ClipboardFailed"), ex);
        }
    }

    private bool StartNotebookFromClipboard()
    {
        try
        {
            var notebook = NewDocument();
            var content = _surface.ReadClipboardObjects(notebook.Pages[0]);
            var page = notebook.Pages[0];
            DocumentSurface.PositionPastedObjects(content.Page, page, new Point(32,32));
            page.Ink.AddRange(content.Page.Ink);
            page.Items.AddRange(content.Page.Items);
            page.Images.AddRange(content.Page.Images);
            DocumentStore.Serialize(notebook); // Reject oversize or invalid data before switching documents.
            if (!CanLeave()) return false;
            ClearRecovery(); _sessionId = Guid.NewGuid().ToString("N");
            _pdf?.Dispose(); _pdf = null;
            _document = NewDocument(); _filePath = _diskHash = null; _image = null; _pageIndex = 0;
            SetupFolder(); _noteSearch.Clear();
            ResetHistory();
            _document = notebook; ShowEditor(); ShowPage();
            _surface.SelectAllObjects(); DocumentChanged();
            return CaptureHistory();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Error(L.T(ex is InvalidDataException ? "ClipboardInvalid" : ex is NotSupportedException ? "ClipboardUnsupported" : "ClipboardFailed"), ex);
            return false;
        }
    }
}
