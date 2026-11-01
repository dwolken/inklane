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
    public string Language { get; set; } = "System";
    public static Settings Current { get; private set; } = new();
    public static void Load()
    {
        try { Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(App.DataDirectory, "settings.json"))) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Current = new(); }
    }
    public static void Save()
    {
        Directory.CreateDirectory(App.DataDirectory);
        var path = Path.Combine(App.DataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Current));
        File.Move(path + ".tmp", path, true);
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
        Brush("SurfaceBrush", dark ? "#252927" : "#FFFFFF");
        Brush("WorkspaceBrush", dark ? "#171B19" : "#E9EEEB");
        Brush("TextBrush", dark ? "#F4F6F4" : "#1E2C25");
        Brush("MutedBrush", dark ? "#B8C6BE" : "#55665D");
        Brush("BorderBrush", dark ? "#46524B" : "#CDD8D1");
        Brush("AccentBrush", dark ? "#73D6B2" : "#176B59");
        Brush("ErrorBrush", dark ? "#FF9B9B" : "#A4262C");
        if (SystemParameters.HighContrast)
        {
            r["SurfaceBrush"] = r["WorkspaceBrush"] = SystemColors.WindowBrush;
            r["TextBrush"] = r["MutedBrush"] = SystemColors.WindowTextBrush;
            r["BorderBrush"] = SystemColors.WindowTextBrush;
            r["AccentBrush"] = SystemColors.HighlightBrush;
            r["ErrorBrush"] = SystemColors.WindowTextBrush;
        }
        if (!_subscribed)
        {
            _subscribed = true;
            SystemEvents.UserPreferenceChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Apply);
        }
    }
}
