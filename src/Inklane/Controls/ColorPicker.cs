using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Inklane.Controls;

/// <summary>A shared RGB/HSV color choice; the selected document objects are changed only on Apply.</summary>
internal sealed class ColorPicker : Window
{
    internal sealed record Choice(string Hex, double Opacity, bool Save);
    internal Choice? Result { get; private set; }
    private readonly TextBox _hex = new() { Width = 125, MaxLength = 7 };
    private readonly Border _preview = new() { Height = 32, Width = 48, CornerRadius = new CornerRadius(4) };
    private readonly Slider _hue = new() { Minimum = 0, Maximum = 359, Width = 300, TickFrequency = 1, SmallChange = 1, LargeChange = 15 };
    private readonly Slider _alpha = new() { Minimum = 0, Maximum = 100, Width = 175, SmallChange = 1, LargeChange = 10 };
    private readonly Image _field = new() { Width = 300, Height = 160, Cursor = Cursors.Cross };
    private readonly TextBlock _alphaLabel = new() { Width = 50, VerticalAlignment = VerticalAlignment.Center };
    private double _saturation = .5, _value = .8;
    private bool _sync;

    internal ColorPicker(Window owner, string hex, double opacity, IEnumerable<string> recent, IEnumerable<string> saved)
    {
        Owner = owner; Title = L.T("Color"); Width = 354; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Background = (Brush)FindResource("SurfaceBrush"); Foreground = (Brush)FindResource("TextBrush");
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;
        Palette(panel, new[] { "#202020", "#FFFFFF", "#64748B", "#245DD7", "#0F9BAD", "#16734A", "#84B83D", "#F4CC31", "#F28C28", "#C3343F", "#C34A86", "#7947AB" });
        panel.Children.Add(_field);
        _field.Margin = new Thickness(0, 12, 0, 4);
        panel.Children.Add(_hue); AutomationProperties.SetName(_hue, L.T("Hue"));
        var row = Studio.Line(_preview, _hex); row.Margin = new Thickness(0, 12, 0, 12); _hex.Margin = new Thickness(12, 0, 0, 0);
        panel.Children.Add(row); AutomationProperties.SetName(_hex, "HEX");
        _alpha.Value = opacity * 100;
        panel.Children.Add(Studio.Line(Studio.Label(L.T("Opacity")), _alpha, _alphaLabel));
        AutomationProperties.SetName(_alpha, L.T("Opacity"));
        if (recent.Any()) { panel.Children.Add(Studio.Label(L.T("RecentColors"))); Palette(panel, recent.Take(12)); }
        if (saved.Any()) { panel.Children.Add(Studio.Label(L.T("SavedColors"))); Palette(panel, saved.Take(24)); }
        var save = new CheckBox { Content = L.T("SaveColor"), Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(save);
        var error = new TextBlock { Foreground = (Brush)FindResource("ErrorBrush"), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var apply = new Button { Content = L.T("Apply"), IsDefault = true, MinWidth = 90 };
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true, Margin = new Thickness(8, 0, 0, 0), MinWidth = 90 };
        var actions = Studio.Line(apply, cancel); actions.HorizontalAlignment = HorizontalAlignment.Right; actions.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(actions);
        apply.Click += (_, _) =>
        {
            if (!Valid(_hex.Text)) { error.Text = L.T("HexInvalid"); _hex.Focus(); return; }
            Result = new Choice(_hex.Text.ToUpperInvariant(), _alpha.Value / 100, save.IsChecked == true); DialogResult = true;
        };
        _hue.ValueChanged += (_, _) => { if (!_sync) UpdateColor(); DrawField(); };
        _alpha.ValueChanged += (_, _) => { _alphaLabel.Text = $"{_alpha.Value:0}%"; };
        _hex.TextChanged += (_, _) => { if (!_sync && Valid(_hex.Text)) { SetColor(_hex.Text); error.Text = ""; } };
        _field.MouseLeftButtonDown += (_, e) => { _field.CaptureMouse(); Pick(e.GetPosition(_field)); };
        _field.MouseMove += (_, e) => { if (_field.IsMouseCaptured) Pick(e.GetPosition(_field)); };
        _field.MouseLeftButtonUp += (_, _) => _field.ReleaseMouseCapture();
        SetColor(hex); _alphaLabel.Text = $"{opacity * 100:0}%";
    }

    private void Palette(Panel host, IEnumerable<string> colors)
    {
        var row = new WrapPanel { Margin = new Thickness(-2, 6, -2, 6) };
        foreach (var color in colors.Where(Valid).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var swatch = new Button { Width = 23, Height = 23, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                Margin = new Thickness(2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), ToolTip = color };
            AutomationProperties.SetName(swatch, color); swatch.Click += (_, _) => SetColor(color); row.Children.Add(swatch);
        }
        host.Children.Add(row);
    }

    internal static bool Valid(string color) => color is { Length: 7 } && color[0] == '#' && color.AsSpan(1).ToArray().All(Uri.IsHexDigit);

    private void SetColor(string hex)
    {
        if (!Valid(hex)) return;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? _hue.Value : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        _sync = true; _hue.Value = (hue + 360) % 360; _saturation = max == 0 ? 0 : delta / max; _value = max;
        _hex.Text = hex.ToUpperInvariant(); _preview.Background = new SolidColorBrush(color); _sync = false; DrawField();
    }

    private static Color Hsv(double hue, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = hue switch { < 60 => (c,x,0d), < 120 => (x,c,0d), < 180 => (0d,c,x), < 240 => (0d,x,c), < 300 => (x,0d,c), _ => (c,0d,x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private void Pick(Point point) { _saturation = Math.Clamp(point.X / 300, 0, 1); _value = 1 - Math.Clamp(point.Y / 160, 0, 1); UpdateColor(); }
    private void UpdateColor()
    {
        var color = Hsv(_hue.Value, _saturation, _value); _sync = true;
        _hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}"; _preview.Background = new SolidColorBrush(color); _sync = false;
    }
    private void DrawField()
    {
        const int width = 300, height = 160; var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var color = Hsv(_hue.Value, x / (double)(width - 1), 1 - y / (double)(height - 1)); var i = (y * width + x) * 4;
            pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255;
        }
        _field.Source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }
}
