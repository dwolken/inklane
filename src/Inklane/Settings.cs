using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Inklane;

public sealed class Settings
{
    public string Appearance { get; set; } = "System";
    public List<string> RecentFiles { get; set; } = [];
    public List<OfficeReviewLink> OfficeReviews { get; set; } = [];
    public List<string> RecentColors { get; set; } = [];
    public List<string> SavedColors { get; set; } = [];
    public string Language { get; set; } = "System";
    public static Settings Current { get; private set; } = new();
    public static void Load()
    {
        try
        {
            Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(App.DataDirectory, "settings.json"))) ?? new();
            Current.Language ??= "System";
            Current.RecentColors = (Current.RecentColors ?? []).Where(Controls.ColorPicker.Valid).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
            Current.SavedColors = (Current.SavedColors ?? []).Where(Controls.ColorPicker.Valid).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
            Current.Appearance ??= "System";
            Current.RecentFiles = (Current.RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
            Current.OfficeReviews = (Current.OfficeReviews ?? []).Where(item => item != null
                && !string.IsNullOrWhiteSpace(item.OfficePath) && Path.IsPathFullyQualified(item.OfficePath)
                && !string.IsNullOrWhiteSpace(item.PdfPath) && Path.IsPathFullyQualified(item.PdfPath)
                && item.OfficeHash?.Length == 64 && item.PdfHash?.Length == 64).Take(12).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Current = new(); }
    }
    public static void Save()
    {
        Directory.CreateDirectory(App.DataDirectory);
        var path = Path.Combine(App.DataDirectory, "settings.json");
        MainWindow.AtomicWrite(path, stream => JsonSerializer.Serialize(stream, Current));
    }
}

public static class Appearance
{
    private static bool _subscribed;
    public static void Apply()
    {
        var mode = Settings.Current.Appearance;
        Application.Current.ThemeMode = mode == "Dark" ? ThemeMode.Dark : mode == "Light" ? ThemeMode.Light : ThemeMode.System;
        var dark = mode == "Dark" || (mode == "System" && (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0);
        var r = Application.Current.Resources;
        void Brush(string key, string value) => r[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        Brush("SurfaceBrush", dark ? "#25242D" : "#FCFBF9");
        Brush("WorkspaceBrush", dark ? "#19181F" : "#E8E6E2");
        Brush("CardBrush", dark ? "#302F3B" : "#FFFFFF");
        Brush("TextBrush", dark ? "#F3F0FA" : "#272534");
        Brush("MutedBrush", dark ? "#BAB6C7" : "#625E6B");
        Brush("BorderBrush", dark ? "#45424F" : "#DCD9DF");
        Brush("AccentBrush", dark ? "#BDB0FF" : "#5B4597");
        Brush("AccentSoftBrush", dark ? "#403653" : "#E9E3F7");
        Brush("HoverBrush", dark ? "#35323F" : "#EFECF0");
        Brush("PressedBrush", dark ? "#494355" : "#E1DBEA");
        Brush("PrimaryBrush", dark ? "#BDB0FF" : "#514083");
        Brush("OnPrimaryBrush", dark ? "#20162F" : "#FFFFFF");
        Brush("ErrorBrush", dark ? "#FF9B9B" : "#A4262C");
        if (SystemParameters.HighContrast)
        {
            r["SurfaceBrush"] = r["WorkspaceBrush"] = SystemColors.WindowBrush;
            r["TextBrush"] = r["MutedBrush"] = SystemColors.WindowTextBrush;
            r["BorderBrush"] = SystemColors.WindowTextBrush;
            r["AccentBrush"] = SystemColors.HighlightBrush;
            r["ErrorBrush"] = SystemColors.WindowTextBrush;
            r["CardBrush"] = r["HoverBrush"] = r["PressedBrush"] = r["AccentSoftBrush"] = SystemColors.WindowBrush;
            r["PrimaryBrush"] = SystemColors.HighlightBrush;
            r["OnPrimaryBrush"] = SystemColors.HighlightTextBrush;
        }
        if (!_subscribed)
        {
            _subscribed = true;
            SystemEvents.UserPreferenceChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Apply);
        }
    }
}
