using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Inklane.Core;

namespace Inklane.Controls;

public sealed partial class DocumentSurface
{
    private void CreateNoteTitle(ItemVisual visual)
    {
        var item = visual.Item;
        var foreground = new SolidColorBrush(Color.FromRgb(98, 68, 25));
        var title = new TextBox
        {
            Text = item.Title, MaxLength = DocumentStore.MaxTitleLength,
            FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = foreground,
            Background = Brushes.Transparent, CaretBrush = foreground,
            SelectionBrush = new SolidColorBrush(Color.FromRgb(169, 162, 226)),
            Padding = new Thickness(0), BorderThickness = new Thickness(0), MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center, Template = DocumentTextTemplate(),
            ToolTip = Translate("RenameNote")
        };
        var hint = new TextBlock
        {
            FontSize = title.FontSize, FontWeight = title.FontWeight, Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var field = new Grid();
        field.Children.Add(title);
        field.Children.Add(hint);
        var underline = new Border { Margin = new Thickness(12, 3, 26, 3),
            BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brushes.Transparent, Child = field };
        visual.Header.Children.Add(underline);
        visual.TitleEditor = title;
        visual.TitleHint = hint;
        AutomationProperties.SetAutomationId(title, "NoteTitle:" + item.Id);
        AutomationProperties.SetName(title, Translate("NoteTitle"));
        void UpdateUnderline() => underline.BorderBrush = title.IsKeyboardFocused || underline.IsMouseOver
            ? foreground : Brushes.Transparent;
        underline.MouseEnter += (_, _) => UpdateUnderline();
        underline.MouseLeave += (_, _) => UpdateUnderline();
        title.GotKeyboardFocus += (_, _) =>
        {
            SelectObject(item.Id);
            UpdateUnderline();
            UpdateNoteTitle(visual);
        };
        title.LostKeyboardFocus += (_, _) =>
        {
            UpdateUnderline();
            UpdateNoteTitle(visual);
        };
        title.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            visual.Editor.Focus();
            e.Handled = true;
        };
        title.TextChanged += (_, _) =>
        {
            // Pasted titles may contain tabs or control characters from other applications.
            var value = title.Text;
            if (value.Any(char.IsControl))
            {
                var caret = title.CaretIndex;
                title.Text = string.Concat(value.Select(c => char.IsControl(c) ? ' ' : c));
                title.CaretIndex = caret;
                return;
            }
            item.Title = value;
            UpdateNoteTitle(visual);
            NotifyChanged();
        };
    }

    private void UpdateNoteTitle(ItemVisual visual)
    {
        var hasTitle = !string.IsNullOrWhiteSpace(visual.Item.Title);
        visual.TitleHint!.Visibility = !hasTitle && !visual.TitleEditor!.IsKeyboardFocused
            ? Visibility.Visible : Visibility.Collapsed;
        var label = hasTitle ? visual.Item.Title : visual.TitleHint.Text;
        AutomationProperties.SetName(visual.Editor, label);
        AutomationProperties.SetHelpText(visual.Grip, label);
        if (visual.Marker is not { } marker) return;
        AutomationProperties.SetName(marker, label);
        marker.ToolTip = label + " · " + Translate(visual.Item.IsExpanded ? "CollapseNote" : "ExpandNote");
    }

    public bool RenameNote(string id)
    {
        if (!RevealNote(id)) return false;
        var title = _items[id].TitleEditor!;
        title.Focus();
        title.SelectAll();
        return true;
    }
}
