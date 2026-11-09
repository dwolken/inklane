using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

public sealed partial class MainWindow
{
    private void ChooseColor(bool fill = false)
    {
        var text = _surface.SelectedTexts.FirstOrDefault(); var shape = _surface.SelectedShapes.FirstOrDefault();
        var color = fill ? shape?.FillColor ?? _surface.ShapeFill ?? "#FFFFFF" : text?.Color ?? shape?.Color ?? _surface.InkColor;
        var picker = new ColorPicker(this, color, text?.Opacity ?? shape?.Opacity ?? _surface.InkOpacity,
            Settings.Current.RecentColors, Settings.Current.SavedColors);
        if (picker.ShowDialog() != true || picker.Result is not { } choice) return;
        _surface.CommitEdits(); if (!CaptureHistory()) return;
        _surface.ApplySelectedColor(choice.Hex, choice.Opacity, fill); CaptureHistory();
        Settings.Current.RecentColors.RemoveAll(c => c.Equals(choice.Hex, StringComparison.OrdinalIgnoreCase));
        Settings.Current.RecentColors.Insert(0, choice.Hex); Settings.Current.RecentColors = Settings.Current.RecentColors.Take(12).ToList();
        if (choice.Save && !Settings.Current.SavedColors.Contains(choice.Hex)) Settings.Current.SavedColors.Add(choice.Hex);
        TrySaveSettings();
    }

    private ContextMenu ShapeMenu()
    {
        var menu = new ContextMenu();
        Command(menu, "InsertElement", ElementLibrary);
        menu.Items.Add(new Separator());
        foreach (var kind in Enum.GetValues<ShapeKind>())
            Command(menu, kind.ToString(), () => { _surface.ShapeKind = kind; SetTool(EditorTool.Shape); });
        return menu;
    }

    private void ObjectProperties()
    {
        var text = _surface.SelectedTexts.FirstOrDefault(); var shape = _surface.SelectedShapes.FirstOrDefault(); var image = _surface.SelectedPictures.FirstOrDefault();
        if (text == null && shape == null && image == null) { ChooseColor(); return; }
        var dialog = new Window { Owner = this, Title = L.T("ObjectProperties"), Width = 380, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        dialog.SetResourceReference(BackgroundProperty, "SurfaceBrush"); dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(22) }; dialog.MaxHeight = SystemParameters.WorkArea.Height - 48; dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        TextBox Field(string key, string value)
        {
            panel.Children.Add(Studio.Label(L.T(key)+(key == "TextSize" ? " (pt)" : "")));
            var box = new TextBox { Text = value, Margin = new Thickness(0, 4, 0, 12) }; panel.Children.Add(box);
            System.Windows.Automation.AutomationProperties.SetName(box, L.T(key)); return box;
        }
        var family = text == null ? null : new ComboBox { IsEditable = true, Text = text.FontFamily, Margin = new Thickness(0, 4, 0, 10), ItemsSource = new[] { "Segoe UI", "Arial", "Georgia", "Cambria", "Consolas", "Times New Roman" } };
        ComboBox? format = null;
        TextBox? size = null; ComboBox? align = null; CheckBox? bold = null, italic = null, underline = null;
        if (text != null)
        {
            format = new ComboBox { ItemsSource = new[] { L.T("PlainText"), "Markdown" }, SelectedIndex = text.ContentFormat == "Markdown" ? 1 : 0, Margin = new Thickness(0,0,0,12) };
            panel.Children.Add(format); System.Windows.Automation.AutomationProperties.SetName(format, L.T("TextFormat"));
            panel.Children.Add(Studio.Label(L.T("Font"))); panel.Children.Add(family!);
            System.Windows.Automation.AutomationProperties.SetName(family!, L.T("Font"));
            size = Field("TextSize", (text.FontSize * 72 / 96).ToString("0.##", CultureInfo.CurrentCulture));
            bold = new CheckBox { Content = L.T("Bold"), IsChecked = text.Bold, Margin = new Thickness(0, 0, 12, 0) };
            italic = new CheckBox { Content = L.T("Italic"), IsChecked = text.Italic, Margin = new Thickness(0, 0, 12, 0) };
            underline = new CheckBox { Content = L.T("Underline"), IsChecked = text.Underline };
            var emphasis = new WrapPanel(); foreach (var choice in new[] { bold,italic,underline }) { choice.Margin = new Thickness(0,0,12,8); emphasis.Children.Add(choice); }
            panel.Children.Add(emphasis);
            align = new ComboBox { ItemsSource = new[] { L.T("Left"), L.T("Center"), L.T("Right"), L.T("Justify") },
                SelectedIndex = Array.IndexOf(new[] { "Left", "Center", "Right", "Justify" }, text.Alignment), Margin = new Thickness(0, 12, 0, 12) };
            System.Windows.Automation.AutomationProperties.SetName(align, L.T("Alignment")); panel.Children.Add(align);
        }
        var label = shape == null ? null : Field("ShapeLabel", shape.Label);
        var stroke = shape == null ? null : Field("Width", shape.StrokeWidth.ToString(CultureInfo.CurrentCulture));
        var width = Field("ObjectWidth", (text?.Width ?? shape?.Width ?? image!.Width).ToString("0.##", CultureInfo.CurrentCulture));
        var height = Field("ObjectHeight", (text?.Height ?? shape?.Height ?? image!.Height).ToString("0.##", CultureInfo.CurrentCulture));
        var rotation = text?.IsNote == true || shape?.StartId != null || shape?.EndId != null ? null : Field("Rotation", (text?.Rotation ?? shape?.Rotation ?? image!.Rotation).ToString("0.##", CultureInfo.CurrentCulture));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush"); panel.Children.Add(error);
        var apply = new Button { Content = L.T("Apply"), IsDefault = true, MinWidth = 90 };
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var actions = Studio.Line(apply, cancel); actions.HorizontalAlignment = HorizontalAlignment.Right; actions.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(actions);
        bool Number(TextBox? box, double fallback, double min, double max, out double result)
        {
            result = fallback; return box == null || double.TryParse(box.Text, out result) && double.IsFinite(result) && result >= min && result <= max;
        }
        apply.Click += (_, _) =>
        {
            if (!Number(width, 240, 1, 100000, out var w) || !Number(height, 80, 1, 100000, out var h) ||
                !Number(size, 18, 1, 750, out var fs) || !Number(rotation, 0, -360, 360, out var angle) ||
                !Number(stroke, 2, .1, 1000, out var sw) || family?.Text is { Length: 0 or > 128 })
            { error.Text = L.T("PropertiesInvalid"); return; }
            _surface.CommitEdits(); if (!CaptureHistory()) return;
            if (text != null)
                _surface.ApplyTextFormatting(i => { i.Width = w; i.Height = h; i.FontFamily = family!.Text; i.FontSize = fs * 96 / 72;
                    i.Bold = bold!.IsChecked == true; i.Italic = italic!.IsChecked == true; i.Underline = underline!.IsChecked == true;
                    i.ContentFormat = format!.SelectedIndex == 1 ? "Markdown" : "Plain"; i.Alignment = new[] { "Left", "Center", "Right", "Justify" }[Math.Max(0, align!.SelectedIndex)]; i.Rotation = angle; });
            if (shape != null) _surface.ApplyShapeFormatting(i => { i.Width = w; i.Height = h; i.Rotation = angle; i.StrokeWidth = sw; i.Label = label!.Text; });
            if (image != null) _surface.ApplyPictureFormatting(i => { i.Width = w; i.Height = h; i.Rotation = angle; });
            CaptureHistory(); dialog.DialogResult = true;
        };
        dialog.ShowDialog();
    }

    private void AddVisualObjectCommands(ItemsControl menu)
    {
        var selected = _surface.HasSelection;
        if (_surface.SelectedTexts.Count != 0) Command(menu, "EditContent", _surface.EditSelectedContent);
        if (_surface.SelectedTexts.Count != 0 || _surface.SelectedShapes.Count != 0 || _surface.SelectedPictures.Count != 0) Command(menu, "ObjectProperties", ObjectProperties);
        Command(menu, "Color", () => ChooseColor()).IsEnabled = selected;
        if (_surface.SelectedShapes.Count != 0)
        {
            Command(menu, "FillColor", () => ChooseColor(true));
            Command(menu, "NoFill", () => ChangeObjects(() => _surface.ApplyShapeFormatting(i => i.FillColor = null)));
        }
        Command(menu, "SaveElement", SaveElement).IsEnabled = selected;
        Command(menu, "InsertElement", ElementLibrary);
        Command(menu, "GroupObjects", () => ChangeObjects(() => _surface.GroupSelection())).IsEnabled = selected;
        Command(menu, "UngroupObjects", () => ChangeObjects(() => _surface.GroupSelection(true))).IsEnabled = selected;
        Command(menu, "BringForward", () => ChangeObjects(() => _surface.ChangeLayer(true))).IsEnabled = selected;
        Command(menu, "SendBackward", () => ChangeObjects(() => _surface.ChangeLayer(false))).IsEnabled = selected;
        if (_surface.SelectedShapes.Count(s => s.Kind is not (ShapeKind.Line or ShapeKind.Arrow)) == 2)
            Command(menu, "ConnectShapes", () => ChangeObjects(_surface.ConnectSelectedShapes));
    }

    private void ChangeObjects(Action action) { _surface.CommitEdits(); if (!CaptureHistory()) return; action(); CaptureHistory(); }
}
