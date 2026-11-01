using System.Collections.Frozen;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane;

internal static class SaveProgress
{
    public static void WriteNotebook(Window owner, DocumentData document, string historySnapshot, string path)
    {
        // Short saves keep the normal inline status. Larger ink, annotation,
        // or page counts can spend seconds constructing PDF appearances.
        if (historySnapshot.Length < 1_000_000 && document.Pages.Count < 32 &&
            document.Pages.Sum(page => page.Ink.Count + page.Items.Count) < 160)
        {
            DocumentStore.SavePdf(document, path);
            return;
        }

        // CaptureHistory just validated this immutable snapshot. Note expansion
        // is the only field excluded from history, so capture it separately.
        var collapsed = document.Pages.SelectMany(page => page.Items)
            .Where(item => item.IsNote && !item.IsExpanded).Select(item => item.Id).ToFrozenSet(StringComparer.Ordinal);
        var dialog = CreateWindow(owner, path);
        ExceptionDispatchInfo? failure = null;
        var finished = false;
        var started = false;
        var worker = new Thread(() =>
        {
            try
            {
                var copy = DocumentStore.Deserialize(historySnapshot);
                foreach (var note in copy.Pages.SelectMany(page => page.Items).Where(item => item.IsNote))
                    note.IsExpanded = !collapsed.Contains(note.Id);
                DocumentStore.SavePdf(copy, path);
            }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            finally
            {
                dialog.Dispatcher.BeginInvoke(() => { finished = true; if (dialog.IsVisible) dialog.Close(); });
            }
        }) { Name = "Inklane PDF writer" };
        worker.SetApartmentState(ApartmentState.STA);
        dialog.Closing += (_, e) => { if (!finished) e.Cancel = true; };
        dialog.Loaded += (_, _) =>
        {
            if (started || finished) return;
            try { worker.Start(); started = true; }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
                finished = true;
                dialog.Dispatcher.BeginInvoke(dialog.Close);
            }
        };
        dialog.ShowDialog(); // Keeps the owner modal and the UI dispatcher responsive.
        if (started) worker.Join(); // Also finishes a write if Windows closes the owned window.
        failure?.Throw();
        if (!started) throw new IOException("The PDF writer could not start.");
    }

    private static Window CreateWindow(Window owner, string path)
    {
        var saving = L.T("Saving");
        var content = new StackPanel { Margin = new Thickness(22, 18, 22, 22) };
        content.Children.Add(Studio.Label(saving, 18, strong: true));
        var file = Studio.Label(Path.GetFileName(path), 13, "MutedBrush");
        file.Margin = new Thickness(0, 4, 0, 16);
        content.Children.Add(file);
        var indicator = new ProgressBar { Height = 5, IsIndeterminate = true };
        AutomationProperties.SetName(indicator, saving);
        content.Children.Add(indicator);
        var dialog = new Window
        {
            Title = "Inklane", Width = 340, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = owner.FontFamily, Content = content
        };
        if (owner.IsVisible) dialog.Owner = owner;
        dialog.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        dialog.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        AutomationProperties.SetAutomationId(dialog, "SaveProgress");
        return dialog;
    }
}
