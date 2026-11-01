using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Inklane.Core;
using Inklane.Controls;

namespace Inklane;

internal static class Dialogs
{
    public static int Choose(Window owner, string title, string message, params string[] buttons)
    {
        var window = Create(owner, title, 470);
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0, 0, 0, 24) });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var result = -1;
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var b = new Button { Content = buttons[i], Margin = new Thickness(6, 4, 0, 0), IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
            b.Click += (_, _) => { result = index; window.Close(); };
            actions.Children.Add(b);
        }
        stack.Children.Add(actions);
        window.Content = stack;
        window.ShowDialog();
        return result;
    }

    public static PageData? Paper(Window owner, PageData? existing = null)
    {
        var window = Create(owner, L.T(existing == null ? "NewNotebook" : "Paper").TrimEnd('…'), 610);
        var stack = new StackPanel { Margin = new Thickness(18,16,18,8) };
        PaperChoices Choice(string label, params string[] values)
        {
            var choices = new PaperChoices(L.T(label),values) { Margin = new Thickness(0,5,0,12) };
            stack.Children.Add(Studio.Label(L.T(label),13,strong:true));
            stack.Children.Add(choices);
            return choices;
        }
        var size = Choice("Format", "A4", "A5");
        var orientation = Choice("Orientation", L.T("Portrait"), L.T("Landscape"));
        if (existing != null)
        {
            size.SelectedIndex = Math.Min(existing.Width, existing.Height) < Units.MmToDip(160) ? 1 : 0;
            orientation.SelectedIndex = existing.Width > existing.Height ? 1 : 0;
            size.IsEnabled = orientation.IsEnabled = false;
            foreach (UIElement field in stack.Children) field.Visibility = Visibility.Collapsed;
        }
        var pattern = Choice("Pattern", L.T("Blank"), L.T("Ruled"), L.T("Squared"));
        pattern.SelectedIndex = existing == null ? 0 : (int)existing.Pattern;
        var spacingPanel = new StackPanel();
        var spacing = new ComboBox { Margin = new Thickness(0, 3, 0, 9), ItemsSource = new[] { "5 mm", "10 mm", L.T("Custom") }, SelectedIndex = 0 };
        AddLabel(spacingPanel, "Spacing", spacing); spacingPanel.Children.Add(spacing);
        var customPanel = new StackPanel();
        var custom = new TextBox { Text = (existing?.SpacingMm ?? 5).ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 3, 0, 9) };
        AddLabel(customPanel, "CustomSpacing", custom); customPanel.Children.Add(custom); spacingPanel.Children.Add(customPanel);
        var margin = new TextBox { Text = (existing?.MarginMm ?? 15).ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 3, 0, 9) };
        AddLabel(spacingPanel, "Margin", margin); spacingPanel.Children.Add(margin);
        spacing.SelectedIndex = existing?.SpacingMm == 10 ? 1 : existing == null || existing.SpacingMm == 5 ? 0 : 2;
        void VisibilityChanged()
        {
            spacingPanel.Visibility = pattern.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
            customPanel.Visibility = spacing.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }
        pattern.ChoiceChanged += (_, _) => VisibilityChanged(); spacing.SelectionChanged += (_, _) => VisibilityChanged(); VisibilityChanged();
        stack.Children.Add(spacingPanel);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        error.Visibility = Visibility.Collapsed;
        stack.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var create = new Button { Content = L.T(existing == null ? "Create" : "Apply"), IsDefault = true };
        PageData? result = null;
        create.Click += (_, _) =>
        {
            double s = spacing.SelectedIndex == 1 ? 10 : 5, m = 15;
            if (pattern.SelectedIndex != 0 && ((spacing.SelectedIndex == 2 && !double.TryParse(custom.Text, out s)) || !double.TryParse(margin.Text, out m) || !double.IsFinite(s) || !double.IsFinite(m) || s < 1 || s > 100 || m < 0 || m > 60))
            { error.Text = L.T("InvalidPaper"); error.Visibility = Visibility.Visible; return; }
            var w = size.SelectedIndex == 0 ? 210 : 148;
            var h = size.SelectedIndex == 0 ? 297 : 210;
            result = new PageData { Width = existing?.Width ?? Units.MmToDip(orientation.SelectedIndex == 0 ? w : h), Height = existing?.Height ?? Units.MmToDip(orientation.SelectedIndex == 0 ? h : w), Pattern = (PaperPattern)pattern.SelectedIndex, SpacingMm = s, MarginMm = m };
            window.DialogResult = true;
        };
        cancel.SetResourceReference(FrameworkElement.StyleProperty,"QuietButton");
        create.SetResourceReference(FrameworkElement.StyleProperty,"PrimaryButton");
        actions.Children.Add(cancel); actions.Children.Add(create);
        actions.Margin = new Thickness(16,9,16,10);
        var preview = new PaperPreview { Height = 224 };
        var dimensions = Studio.Label("",13,strong:true);
        dimensions.HorizontalAlignment = HorizontalAlignment.Center;
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
        left.Children.Add(preview); left.Children.Add(dimensions);
        void UpdatePreview()
        {
            double w = size.SelectedIndex == 0 ? 210 : 148, h = size.SelectedIndex == 0 ? 297 : 210;
            if (orientation.SelectedIndex == 1) (w,h) = (h,w);
            double step = spacing.SelectedIndex == 1 ? 10 : 5;
            if (spacing.SelectedIndex == 2 && double.TryParse(custom.Text,out double val) && double.IsFinite(val))
                step = Math.Clamp(val,1,100);
            double inset = double.TryParse(margin.Text,out double m) && double.IsFinite(m) ? Math.Clamp(m,0,60) : 15;
            preview.SetPaper(w,h,(PaperPattern)pattern.SelectedIndex,step,inset);
            dimensions.Text = $"{w} × {h} mm";
        }
        foreach (var group in new[] {size,orientation,pattern}) group.ChoiceChanged += (_,_) => UpdatePreview();
        spacing.SelectionChanged += (_,_) => UpdatePreview();
        custom.TextChanged += (_,_) => UpdatePreview(); margin.TextChanged += (_,_) => UpdatePreview(); UpdatePreview();
        var well = new Border { Child = left, Width = 214 };
        well.SetResourceReference(Border.BackgroundProperty,"WorkspaceBrush");
        var body = new DockPanel();
        DockPanel.SetDock(well,Dock.Left); body.Children.Add(well);
        body.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var root = new DockPanel();
        var footer = new Border { Child = actions, BorderThickness = new Thickness(0,1,0,0) };
        footer.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(body); window.Content = root;
        window.ShowDialog();
        return result;
    }

    private static Window Create(Window owner, string title, double width)
    {
        var result = new Window { Owner = owner, Title = title, Width = Math.Min(width,SystemParameters.WorkArea.Width - 24), SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 80), ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 14 };
        result.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); result.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        return result;
    }
    private static void AddLabel(Panel panel, string key, Control target)
    {
        var label = new TextBlock { Text = L.T(key), FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetName(target, L.T(key));
        panel.Children.Add(label);
    }
}
