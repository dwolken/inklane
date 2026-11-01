using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Inklane;

public partial class App : Application
{
    internal static string? DataDirectoryOverride { get; set; }
    public static string DataDirectory => DataDirectoryOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inklane");
    public static bool SmokeTest { get; private set; }
    public static bool FatalShutdown { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings.Load();
        for (var i = 0; i + 1 < e.Args.Length; i++)
        {
            if (e.Args[i] == "--language") Settings.Current.Language = e.Args[i + 1];
            if (e.Args[i] == "--appearance") Settings.Current.Appearance = e.Args[i + 1];
        }
        L.Initialize(Settings.Current.Language);
        Appearance.Apply();
        SmokeTest = e.Args.Contains("--smoke-test");
        DispatcherUnhandledException += (_, args) =>
        {
            if (FatalShutdown) { args.Handled = true; return; }
            FatalShutdown = true;
            (MainWindow as MainWindow)?.StopBackgroundWork();
            try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(Path.Combine(DataDirectory, "error.log"), $"{DateTimeOffset.Now:O}\n{args.Exception}\n"); } catch { }
            if (SmokeTest)
            {
                var index = Array.IndexOf(e.Args, "--smoke-test");
                if (index >= 0 && index + 1 < e.Args.Length)
                    try { File.WriteAllText(e.Args[index + 1], System.Text.Json.JsonSerializer.Serialize(new { success = false, error = args.Exception.ToString() })); } catch (IOException) { }
            }
            else MessageBox.Show(L.T("UnexpectedError"), "Inklane", MessageBoxButton.OK, MessageBoxImage.Error);
            // Preserve the most recent recovery snapshot; stop instead of continuing in an unknown state.
            Shutdown(1);
            args.Handled = true;
        };
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        window.Dispatcher.BeginInvoke(async () =>
        {
            if (SmokeTest)
            {
                var index = Array.IndexOf(e.Args, "--smoke-test");
                if (index + 1 >= e.Args.Length) { Shutdown(2); return; }
                try
                {
                    await Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render);
                    var result = window.RunStartupSmokeCheck();
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(e.Args[index + 1]))!);
                    File.WriteAllText(e.Args[index + 1], System.Text.Json.JsonSerializer.Serialize(result));
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(e.Args[index + 1], System.Text.Json.JsonSerializer.Serialize(new { success = false, error = ex.ToString() }));
                    Shutdown(1);
                }
            }
            else
            {
                window.OfferRecovery();
                var file = e.Args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));
                if (file != null) window.OpenFile(file);
            }
        }, DispatcherPriority.ApplicationIdle);
    }
}
