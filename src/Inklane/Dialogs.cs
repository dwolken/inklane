using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Inklane.Core;

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
        var window = Create(owner, L.T(existing == null ? "NewNotebook" : "Paper").TrimEnd('…'), 440);
        var stack = new StackPanel { Margin = new Thickness(24) };
        ComboBox Choice(string label, params string[] values)
        {
            var cb = new ComboBox { Margin = new Thickness(0, 5, 0, 16), HorizontalContentAlignment = HorizontalAlignment.Left };
            foreach (var v in values) cb.Items.Add(v);
            cb.SelectedIndex = 0;
            AddLabel(stack, label, cb); stack.Children.Add(cb);
            return cb;
        }
        var size = Choice("Format", "A4 · 210 × 297 mm", "A5 · 148 × 210 mm");
        var orientation = Choice("Orientation", L.T("Portrait"), L.T("Landscape"));
        if (existing != null)
        {
            size.SelectedIndex = Math.Min(existing.Width, existing.Height) < Units.MmToDip(160) ? 1 : 0;
            orientation.SelectedIndex = existing.Width > existing.Height ? 1 : 0;
            size.IsEnabled = orientation.IsEnabled = false;
        }
        var pattern = Choice("Pattern", L.T("Blank"), L.T("Ruled"), L.T("Squared"));
        pattern.SelectedIndex = existing == null ? 0 : (int)existing.Pattern;
        var spacingPanel = new StackPanel();
        var spacing = new ComboBox { Margin = new Thickness(0, 5, 0, 16), ItemsSource = new[] { "5 mm", "10 mm", L.T("Custom") }, SelectedIndex = 0 };
        AddLabel(spacingPanel, "Spacing", spacing); spacingPanel.Children.Add(spacing);
        var customPanel = new StackPanel();
        var custom = new TextBox { Text = (existing?.SpacingMm ?? 5).ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 5, 0, 16) };
        AddLabel(customPanel, "CustomSpacing", custom); customPanel.Children.Add(custom); spacingPanel.Children.Add(customPanel);
        var margin = new TextBox { Text = (existing?.MarginMm ?? 15).ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 5, 0, 16) };
        AddLabel(spacingPanel, "Margin", margin); spacingPanel.Children.Add(margin);
        spacing.SelectedIndex = existing?.SpacingMm == 10 ? 1 : existing == null || existing.SpacingMm == 5 ? 0 : 2;
        void VisibilityChanged()
        {
            spacingPanel.Visibility = pattern.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
            customPanel.Visibility = spacing.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }
        pattern.SelectionChanged += (_, _) => VisibilityChanged(); spacing.SelectionChanged += (_, _) => VisibilityChanged(); VisibilityChanged();
        stack.Children.Add(spacingPanel);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        stack.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var create = new Button { Content = L.T(existing == null ? "Create" : "Apply"), IsDefault = true };
        PageData? result = null;
        create.Click += (_, _) =>
        {
            double s = spacing.SelectedIndex == 1 ? 10 : 5, m = 15;
            if (pattern.SelectedIndex != 0 && ((spacing.SelectedIndex == 2 && !double.TryParse(custom.Text, out s)) || !double.TryParse(margin.Text, out m) || !double.IsFinite(s) || !double.IsFinite(m) || s < 1 || s > 100 || m < 0 || m > 60))
            { error.Text = L.T("InvalidPaper"); return; }
            var w = size.SelectedIndex == 0 ? 210 : 148;
            var h = size.SelectedIndex == 0 ? 297 : 210;
            result = new PageData { Width = existing?.Width ?? Units.MmToDip(orientation.SelectedIndex == 0 ? w : h), Height = existing?.Height ?? Units.MmToDip(orientation.SelectedIndex == 0 ? h : w), Pattern = (PaperPattern)pattern.SelectedIndex, SpacingMm = s, MarginMm = m };
            window.DialogResult = true;
        };
        actions.Children.Add(cancel); actions.Children.Add(create); stack.Children.Add(actions);
        window.Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.ShowDialog();
        return result;
    }

    private static Window Create(Window owner, string title, double width)
    {
        var result = new Window { Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 80), ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 14 };
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
