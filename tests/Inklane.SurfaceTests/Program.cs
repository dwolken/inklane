using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using Inklane;
using Inklane.Controls;
using Inklane.Core;

namespace Inklane.SurfaceTests;

/// <summary>
/// STA integration checks exercise real WPF controls and renderer, without
/// synthesizing physical input or claiming hardware/UI acceptance coverage.
/// </summary>
internal static class Program
{
    private static int _failures;
    private static int _checks;
    private static string _output = "";

    [STAThread]
    private static int Main(string[] args)
    {
        _output = Path.GetFullPath(args.FirstOrDefault() ?? ".scratch/surface-test-output");
        Directory.CreateDirectory(_output);
        // A plain Application avoids invoking product startup/settings/recovery
        // when a real modal dialog enters its nested dispatcher below.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
        LoadApplicationResources(app);
        L.Initialize("en");
        Settings.Current.Appearance = "Light";
        Appearance.Apply();
        Run("A4 raster dimensions and white page boundaries", PageRaster);
        Run("Original image pixels survive rendering without annotations", ImageRaster);
        Run("View, tool and clean export operations do not mutate document", ViewPurity);
        Run("Typed text edits persist Unicode and multiline content", EditableText);
        Run("Expanded note text and anchor survive collapse and reload", NoteRoundTrip);
        Run("Note marker and editor retain readable size at different zooms", NoteReadability);
        Run("Expanded lower-page notes stay inside the visible viewport during fit, resize and pan", NoteViewportPlacement);
        Run("Object drag uses page coordinates and canceled drag rolls back", ObjectMovement);
        Run("Selected ink deletion updates editable model", InkDeletion);
        Run("Collected native ink serializes an accepted RGB color", InkCollection);
        Run("Native pressure changes the rendered stroke outline", PressureRendering);
        Run("Fresh notebook leaves without a save prompt", FreshDocumentLifecycle);
        Run("Notebook page history supports undo and redo", PageHistory);
        Run("Text undo and redo preserve all content and saved state", TextHistory);
        Run("English, German and unsupported-language fallback resolve", Localization);
        Run("Shell controls retain Fluent templates in light and dark modes", FluentAppearance);
        Run("Paper dialog hides irrelevant controls and returns valid presets", PaperPresets);
        Run("Paper dialog rejects invalid custom values and preserves millimeters", PaperCustom);
        Run("Workspace controls fit small and large layouts in both languages", ResponsiveLayout);
        Run("Editing fit opens a readable upper page while full-page fit remains available", EditingFit);
        Run("All localized strings have complete values and matching placeholders", LocalizationCoverage);
        Console.WriteLine($"{_checks - _failures}/{_checks} checks passed. Artifacts: {_output}");
        app.Shutdown();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action action)
    {
        _checks++;
        try { action(); Console.WriteLine($"PASS {name}"); }
        catch (Exception exception) { _failures++; Console.WriteLine($"FAIL {name}\n{exception}"); }
    }

    private static PageData Sample() => new()
    {
        Pattern = PaperPattern.Squared, SpacingMm = 5, MarginMm = 15,
        Ink = [new InkData { Width = 4, Points = [new() { X = 100, Y = 100, Pressure = .2f }, new() { X = 200, Y = 180, Pressure = .9f }] }],
        Items =
        [
            new TextItem { X = 120, Y = 220, Text = "Editable title", Width = 260, Height = 80, FontSize = 22 },
            new TextItem { X = 480.125, Y = 280.375, Text = "Anchored note\n" + string.Join("\n", Enumerable.Range(1, 40).Select(i => $"Line {i}: Grüße aus Österreich.")), IsNote = true, IsExpanded = true, Height = 180, Width = 280 }
        ]
    };

    private static DocumentSurface Surface(PageData page)
    {
        var surface = new DocumentSurface { Width = 1100, Height = 800, Translate = L.T };
        surface.LoadPage(page);
        Layout(surface);
        surface.FitToPage();
        Layout(surface);
        return surface;
    }

    private static void PageRaster()
    {
        var surface = Surface(new PageData());
        var image = surface.RenderPage(96);
        Equal(794, image.PixelWidth); Equal(1123, image.PixelHeight);
        var pixels = Pixels(image);
        Pixel(pixels, image.PixelWidth, 0, 0, 255, 255, 255);
        Pixel(pixels, image.PixelWidth, 400, 600, 255, 255, 255);
        // The partial final pixel is antialiased against the physical page edge.
        Assert(pixels[((1121 * image.PixelWidth) + 792) * 4 + 3] == 255, "Interior page became transparent.");
    }

    private static void ImageRaster()
    {
        var data = new byte[120 * 80 * 4];
        for (var i = 0; i < data.Length; i += 4) { data[i] = 40; data[i + 1] = 90; data[i + 2] = 210; data[i + 3] = 255; }
        var source = BitmapSource.Create(120, 80, 96, 96, PixelFormats.Pbgra32, null, data, 120 * 4);
        var surface = Surface(new PageData { Width = 120, Height = 80, MarginMm = 0 });
        surface.LoadPage(new PageData { Width = 120, Height = 80, MarginMm = 0 }, source);
        var rendered = surface.RenderPage();
        Equal(120, rendered.PixelWidth); Equal(80, rendered.PixelHeight);
        Assert(data.SequenceEqual(Pixels(rendered)), "Rendering changed unannotated image pixels.");
    }

    private static void ViewPurity()
    {
        var page = Sample();
        var surface = Surface(page);
        var before = State(page);
        var changes = 0; surface.Changed += (_, _) => changes++;
        surface.ZoomBy(1.5); surface.Zoom = .25; surface.FitToPage();
        foreach (var tool in Enum.GetValues<EditorTool>()) surface.Tool = tool;
        surface.CommitEdits(); surface.RenderPage();
        Equal(before, State(page)); Equal(0, changes);
        var unmeasured = new DocumentSurface(); unmeasured.LoadPage(page); unmeasured.RenderPage(150);
        Equal(before, State(page));
    }

    private static void EditableText()
    {
        var page = Sample(); var surface = Surface(page); var changes = 0;
        surface.Changed += (_, _) => changes++;
        var text = Editor(surface, "Text");
        text.Text = "Editable — äöüß\nSecond line remains editable.";
        surface.CommitEdits();
        Equal(text.Text, page.Items[0].Text); Equal(1, changes);
        Assert(page.Items[0].Height >= 44, "Multiline editor did not grow.");
        var clone = DocumentStore.Deserialize(State(page)).Pages[0];
        surface.LoadPage(clone); Layout(surface);
        Equal(text.Text, Editor(surface, "Text").Text);
        Save(surface.RenderPage(), "annotated-a4.png");
    }

    private static void NoteRoundTrip()
    {
        var page = Sample(); var surface = Surface(page); var note = page.Items[1];
        var text = note.Text; var x = note.X; var y = note.Y; var changes = 0;
        surface.Changed += (_, _) => changes++;
        var collapse = Descendants(surface).OfType<Button>().Single(b => AutomationProperties.GetName(b) == L.T("CollapseNote"));
        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!note.IsExpanded, "Note did not collapse."); Equal(1, changes);
        var clone = DocumentStore.Deserialize(State(page)).Pages[0];
        surface.LoadPage(clone); Layout(surface);
        Equal(text, clone.Items[1].Text); Near(x, clone.Items[1].X); Near(y, clone.Items[1].Y);
        var marker = Descendants(surface).OfType<Button>().Single(b => AutomationProperties.GetName(b) == L.T("Note"));
        marker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(clone.Items[1].IsExpanded, "Note did not expand.");
        Equal(text, Editor(surface, "Note").Text);
    }

    private static void NoteReadability()
    {
        var page = Sample(); var surface = Surface(page);
        var marker = Descendants(surface).OfType<Button>().Single(b => AutomationProperties.GetName(b) == L.T("Note"));
        var editor = Editor(surface, "Note");
        var original = State(page);
        foreach (var zoom in new[] { .15, .4, 1d, 2d, 4d })
        {
            surface.Zoom = zoom; Layout(surface);
            var markerBounds = marker.TransformToAncestor(surface).TransformBounds(new Rect(marker.RenderSize));
            var editorBounds = editor.TransformToAncestor(surface).TransformBounds(new Rect(editor.RenderSize));
            Near(28, markerBounds.Width); Near(28, markerBounds.Height);
            Assert(editorBounds.Width >= 270 && editorBounds.Width <= 280, "Note editor became too narrow.");
            Near(180, editorBounds.Height);
            var pageVisual = VisualTreeHelper.GetParent(Descendants(surface).OfType<InkCanvas>().Single());
            var anchor = marker.TranslatePoint(default, (UIElement)pageVisual);
            Near(page.Items[1].X, anchor.X); Near(page.Items[1].Y, anchor.Y);
        }
        Equal(original, State(page));
        surface.FitToPage(); Layout(surface);
        var bitmap = new RenderTargetBitmap(1100, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        Save(bitmap, "expanded-note-workspace.png");
    }

    private static void ObjectMovement()
    {
        var page = Sample(); var surface = Surface(page); surface.Zoom = 1.7; Layout(surface);
        var grip = Descendants(surface).OfType<Thumb>().First(t => AutomationProperties.GetName(t) == L.T("MoveItem"));
        var item = page.Items[0]; var x = item.X; var y = item.Y; var changes = 0;
        surface.Changed += (_, _) => changes++;
        grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        grip.RaiseEvent(new DragDeltaEventArgs(20, 30) { RoutedEvent = Thumb.DragDeltaEvent });
        grip.RaiseEvent(new DragCompletedEventArgs(20, 30, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Near(x + 20, item.X); Near(y + 30, item.Y); Equal(1, changes);
        grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        grip.RaiseEvent(new DragDeltaEventArgs(100, 100) { RoutedEvent = Thumb.DragDeltaEvent });
        grip.RaiseEvent(new DragCompletedEventArgs(100, 100, true) { RoutedEvent = Thumb.DragCompletedEvent });
        Near(x + 20, item.X); Near(y + 30, item.Y); Equal(1, changes);
    }

    private static void NoteViewportPlacement()
    {
        var page = Sample(); page.Items[1].Y = 550;
        var surface = Surface(page); var original = State(page); var changes = 0;
        surface.Changed += (_, _) => changes++;
        var editor = Editor(surface, "Note");
        var card = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(editor));
        var marker = Descendants(surface).OfType<Button>().Single(b => AutomationProperties.GetName(b) == L.T("Note"));
        var scroll = (ScrollViewer)Field(surface, "_scroll")!;
        void CheckBounds()
        {
            surface.UpdateLayout();
            var bounds = card.TransformToAncestor(surface).TransformBounds(new Rect(card.RenderSize));
            Assert(bounds.Left >= 7.9 && bounds.Top >= 7.9 && bounds.Right <= scroll.ViewportWidth - 7.9 && bounds.Bottom <= scroll.ViewportHeight - 7.9,
                $"Expanded card is outside the viewport: card={bounds}, viewport={scroll.ViewportWidth}×{scroll.ViewportHeight}.");
            var markerBounds = marker.TransformToAncestor(surface).TransformBounds(new Rect(marker.RenderSize));
            Assert(!bounds.IntersectsWith(markerBounds), "Card covers its marker despite available horizontal room.");
        }
        foreach (var size in new[] { new Size(960, 440), new Size(760, 240), new Size(760, 180) })
        {
            surface.Width = size.Width; surface.Height = size.Height;
            surface.Measure(size); surface.Arrange(new Rect(size)); surface.UpdateLayout();
            surface.FitToPage(); CheckBounds();
        }
        surface.Height = 440; surface.Measure(new Size(760, 440)); surface.Arrange(new Rect(0, 0, 760, 440));
        surface.Zoom = 1.5; surface.UpdateLayout();
        scroll.ScrollToVerticalOffset(450); scroll.ScrollToHorizontalOffset(200); surface.UpdateLayout(); CheckBounds();
        Equal(original, State(page)); Equal(0, changes);
        surface.FitToPage(); surface.UpdateLayout(); CheckBounds();
        RenderContent(surface, Brushes.LightGray, "note-viewport-placement.png");
    }

    private static void InkDeletion()
    {
        var page = Sample(); var surface = Surface(page); surface.Tool = EditorTool.Select;
        var canvas = Descendants(surface).OfType<InkCanvas>().Single();
        canvas.Select(new StrokeCollection([canvas.Strokes[0]]));
        surface.DeleteSelection();
        Equal(0, canvas.Strokes.Count); Equal(0, page.Ink.Count);
    }

    private static void InkCollection()
    {
        var page = new PageData(); var surface = Surface(page);
        var canvas = Descendants(surface).OfType<InkCanvas>().Single();
        var points = new System.Windows.Input.StylusPointCollection
        {
            new System.Windows.Input.StylusPoint(90, 110, .25f), new System.Windows.Input.StylusPoint(140, 160, .8f)
        };
        var stroke = new Stroke(points, new DrawingAttributes { Color = Color.FromRgb(32, 32, 32), Width = 2, Height = 2 });
        canvas.Strokes.Add(stroke);
        canvas.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke));
        Equal(1, page.Ink.Count); Equal("#202020", page.Ink[0].Color);
        var reopened = DocumentStore.Deserialize(State(page));
        Equal("#202020", reopened.Pages[0].Ink[0].Color);
        Near(.8f, reopened.Pages[0].Ink[0].Points[1].Pressure);
    }

    private static void PressureRendering()
    {
        var page = new PageData { Width = 300, Height = 200, MarginMm = 0,
            Ink = [new InkData { Width = 12, Points = [new() { X = 50, Y = 100, Pressure = .15f }, new() { X = 250, Y = 100, Pressure = .95f }] }] };
        var image = Surface(page).RenderPage(); var data = Pixels(image);
        int Count(int x) => Enumerable.Range(0, 200).Count(y => data[(y * 300 + x) * 4] < 100);
        Assert(Count(230) > Count(70) + 4, "Pressure did not widen the native ink outline.");
    }

    private static void FreshDocumentLifecycle()
    {
        var window = new MainWindow();
        Assert(!(bool)Property(window, "IsDirty")!, "Fresh notebook is dirty.");
        Assert((bool)Call(window, "CanLeave")!, "Unchanged document cannot be left.");
        StopTimers(window);
    }

    private static void PageHistory()
    {
        var window = new MainWindow();
        Call(window, "AddPage"); Equal(2, Document(window).Pages.Count);
        Assert((bool)Property(window, "IsDirty")!, "Added page is not dirty.");
        Call(window, "Undo"); Equal(1, Document(window).Pages.Count);
        Assert(!(bool)Property(window, "IsDirty")!, "Undo to saved state remains dirty.");
        Call(window, "Redo"); Equal(2, Document(window).Pages.Count);
        StopTimers(window);
    }

    private static void TextHistory()
    {
        var window = new MainWindow(); var page = Sample();
        SetField(window, "_document", new DocumentData { Pages = [page] });
        Call(window, "ResetHistory", false); Call(window, "ShowPage", false);
        var surface = (DocumentSurface)Field(window, "_surface")!;
        surface.Width = 1100; surface.Height = 800; Layout(surface);
        Editor(surface, "Text").Text = "Changed in editor";
        Call(window, "CaptureHistory");
        Assert((bool)Property(window, "IsDirty")!, "Typing is not dirty.");
        Call(window, "Undo"); Equal("Editable title", Document(window).Pages[0].Items[0].Text);
        Assert(!(bool)Property(window, "IsDirty")!, "Undo does not return to saved state.");
        Call(window, "Redo"); Equal("Changed in editor", Document(window).Pages[0].Items[0].Text);
        Equal(page.Items[1].Text, Document(window).Pages[0].Items[1].Text);
        StopTimers(window);
    }

    private static void Localization()
    {
        Equal("de", L.ResolveLanguage("System", "de-AT"));
        Equal("en", L.ResolveLanguage("System", "fr-FR"));
        Equal("en", L.ResolveLanguage("en", "de-DE"));
        Equal("de", L.ResolveLanguage("de", "en-US"));
        foreach (var language in new[] { "en", "de" })
        {
            L.Initialize(language);
            foreach (var key in new[] { "PageCanvas", "CollapseNote", "ExpandNote", "MoveItem", "SaveFailed", "OpenFailed", "RecoveryFailed" })
                Assert(L.T(key) != key, $"Missing {language} translation: {key}");
        }
        L.Initialize("en");
    }

    private static void FluentAppearance()
    {
        foreach (var mode in new[] { "Light", "Dark" })
        {
            Settings.Current.Appearance = mode; Appearance.Apply();
            var window = new MainWindow();
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1100, 800)); content.Arrange(new Rect(0, 0, 1100, 800)); content.UpdateLayout();
            var surface = (DocumentSurface)Field(window, "_surface")!;
            surface.LoadPage(Sample()); surface.FitToPage(); content.UpdateLayout();
            var button = Descendants(content).OfType<Button>().First(b => AutomationProperties.GetName(b) == L.T("Save"));
            var combo = Descendants(content).OfType<ComboBox>().First();
            // Theme switching replaces dictionary instances. Static BasedOn
            // keeps the template; its DynamicResource brushes update in place.
            Assert(button.Style?.BasedOn?.TargetType == typeof(ButtonBase) && button.OverridesDefaultStyle,
                "Button lost its Fluent base style.");
            Assert(combo.Style?.BasedOn?.TargetType == typeof(ComboBox) && combo.OverridesDefaultStyle,
                "ComboBox lost its Fluent base style.");
            Assert(button.Template.FindName("ContentBorder", button) is Border { CornerRadius.TopLeft: > 0 },
                "Rounded Fluent button template is missing.");
            Equal("#FF352B18", Editor(surface, "Note").Foreground.ToString());
            Equal("#FF202020", Editor(surface, "Text").Foreground.ToString());
            Equal(1_000_000, Editor(surface, "Text").MaxLength);
            var clean = surface.RenderPage(); Save(clean, $"page-{mode.ToLowerInvariant()}.png");
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, 1100, 800));
                dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, 1100, 800));
            }
            var image = new RenderTargetBitmap(1100, 800, 96, 96, PixelFormats.Pbgra32); image.Render(drawing);
            Save(image, $"shell-{mode.ToLowerInvariant()}.png");
            StopTimers(window);
        }
        Settings.Current.Appearance = "Light"; Appearance.Apply();
    }

    private static void PaperPresets()
    {
        var result = WithPaperDialog("en", "Light", dialog =>
        {
            var pattern = Named<ComboBox>(dialog, "Pattern");
            var spacing = Named<ComboBox>(dialog, "Spacing");
            var custom = Named<TextBox>(dialog, "CustomSpacing");
            Equal(0, pattern.SelectedIndex);
            Assert(!IsVisibleWithin(spacing, dialog) && !IsVisibleWithin(custom, dialog), "Blank paper exposes line spacing.");
            CaptureDialog(dialog, "paper-en-light-blank");
            pattern.SelectedIndex = 1; dialog.UpdateLayout();
            Assert(IsVisibleWithin(spacing, dialog), "Ruled paper hides spacing presets.");
            Assert(!IsVisibleWithin(custom, dialog), "Preset spacing exposes a redundant custom field.");
            spacing.SelectedIndex = 1;
            Named<ComboBox>(dialog, "Format").SelectedIndex = 1;
            Named<ComboBox>(dialog, "Orientation").SelectedIndex = 1;
            Named<TextBox>(dialog, "Margin").Text = 12.5.ToString(CultureInfo.CurrentCulture);
            CaptureDialog(dialog, "paper-en-light-ruled-top");
            ScrollDialogToBottom(dialog);
            CaptureDialog(dialog, "paper-en-light-ruled-bottom");
            ClickCreate(dialog);
        });
        Assert(result != null, "Valid preset creation was canceled.");
        Near(210, Units.DipToMm(result!.Width)); Near(148, Units.DipToMm(result.Height));
        Equal(PaperPattern.Ruled, result.Pattern); Near(10, result.SpacingMm); Near(12.5, result.MarginMm);
    }

    private static void PaperCustom()
    {
        var result = WithPaperDialog("de", "Dark", dialog =>
        {
            var pattern = Named<ComboBox>(dialog, "Pattern");
            var spacing = Named<ComboBox>(dialog, "Spacing");
            var custom = Named<TextBox>(dialog, "CustomSpacing");
            var margin = Named<TextBox>(dialog, "Margin");
            pattern.SelectedIndex = 2; spacing.SelectedIndex = 2; dialog.UpdateLayout();
            Assert(IsVisibleWithin(spacing, dialog) && IsVisibleWithin(custom, dialog), "Custom spacing field is not visible.");
            Equal(1, Descendants(dialog).OfType<TextBox>().Count(t => AutomationProperties.GetName(t) == L.T("CustomSpacing")));
            foreach (var invalid in new[] { "invalid", "0", "101", "NaN" })
            {
                custom.Text = invalid; ClickCreate(dialog);
                Assert(dialog.IsVisible, $"Invalid spacing '{invalid}' closed the dialog.");
                Assert(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == L.T("InvalidPaper")), "Validation feedback is absent.");
            }
            custom.Text = 7.5.ToString(CultureInfo.CurrentCulture); margin.Text = "61"; ClickCreate(dialog);
            Assert(dialog.IsVisible, "Invalid margin closed the dialog.");
            var error = Descendants(dialog).OfType<TextBlock>().Single(t => t.Text == L.T("InvalidPaper"));
            Assert(Contrast(((SolidColorBrush)error.Foreground).Color, ((SolidColorBrush)dialog.Background).Color) >= 4.5,
                "Validation feedback does not have readable contrast.");
            CaptureDialog(dialog, "paper-de-dark-custom-invalid-top");
            ScrollDialogToBottom(dialog);
            CaptureDialog(dialog, "paper-de-dark-custom-invalid-bottom");
            margin.Text = 12.5.ToString(CultureInfo.CurrentCulture);
            ClickCreate(dialog);
        });
        Assert(result != null, "Valid custom creation was canceled.");
        Near(210, Units.DipToMm(result!.Width)); Near(297, Units.DipToMm(result.Height));
        Equal(PaperPattern.Squared, result.Pattern); Near(7.5, result.SpacingMm); Near(12.5, result.MarginMm);
    }

    private static PageData? WithPaperDialog(string language, string appearance, Action<Window> exercise)
    {
        L.Initialize(language); Settings.Current.Appearance = appearance; Appearance.Apply();
        // These windows belong to this test process. They are positioned outside
        // the desktop and never receive injected mouse or keyboard input.
        var owner = new Window { Width = 16, Height = 16, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Opacity = 0 };
        owner.Show();
        Exception? failure = null;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Owner == owner);
            try
            {
                dialog.Opacity = 0; dialog.ShowActivated = false;
                dialog.Left = -10000; dialog.Top = -10000;
                dialog.MaxHeight = 400; dialog.UpdateLayout();
                exercise(dialog);
            }
            catch (Exception exception) { failure = exception; }
            finally { if (dialog.IsVisible) dialog.Close(); }
        }, DispatcherPriority.ContextIdle);
        try
        {
            var type = typeof(MainWindow).Assembly.GetType("Inklane.Dialogs")!;
            var result = (PageData?)type.GetMethod("Paper", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [owner, null]);
            if (failure != null) throw new InvalidOperationException("Paper dialog integration check failed.", failure);
            return result;
        }
        finally { owner.Close(); L.Initialize("en"); Settings.Current.Appearance = "Light"; Appearance.Apply(); }
    }

    private static void ScrollDialogToBottom(Window dialog)
    {
        var scroll = (ScrollViewer)dialog.Content;
        scroll.ScrollToBottom(); scroll.UpdateLayout();
        Assert(scroll.VerticalOffset > 0, "Constrained dialog does not scroll to its actions.");
    }

    private static void CaptureDialog(Window dialog, string name)
    {
        dialog.UpdateLayout();
        var content = (FrameworkElement)dialog.Content;
        Assert(content.ActualWidth > 300 && content.ActualHeight > 100, "Dialog content has no usable layout.");
        Assert(dialog.ActualHeight <= 400.5, "Dialog exceeded its constrained available height.");
        RenderContent(content, dialog.Background, name + ".png");
    }

    private static void ClickCreate(Window dialog) => Descendants(dialog).OfType<Button>()
        .Single(b => Equals(b.Content, L.T("Create"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Named<T>(DependencyObject parent, string key) where T : Control
    {
        var controls = Descendants(parent).OfType<T>().ToArray();
        return controls.SingleOrDefault(c => AutomationProperties.GetName(c) == L.T(key))
            ?? throw new InvalidOperationException($"Missing {typeof(T).Name} '{key}' ({L.T(key)}) in {L.Language}. Found: {string.Join(", ", controls.Select(AutomationProperties.GetName))}");
    }

    private static void ResponsiveLayout()
    {
        foreach (var (language, appearance) in new[] { ("en", "Light"), ("de", "Dark") })
        foreach (var size in new[] { new Size(760, 560), new Size(1200, 880) })
        foreach (var tool in new[] { EditorTool.Pen, EditorTool.Text })
        {
            L.Initialize(language); Settings.Current.Appearance = appearance; Appearance.Apply();
            var window = new MainWindow(); var content = (FrameworkElement)window.Content;
            Call(window, "SetTool", tool);
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            var surface = (DocumentSurface)Field(window, "_surface")!;
            surface.FitToPage(); content.UpdateLayout();
            Assert(surface.ActualHeight >= 150 && surface.ActualWidth > 700, $"Document space collapsed at {size}.");
            foreach (var control in Descendants(content).OfType<Control>().Where(c => c is ButtonBase or ComboBox))
            {
                if (!IsVisibleWithin(control, content) || surface.IsAncestorOf(control)) continue;
                var bounds = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
                Assert(bounds.Left >= -.5 && bounds.Top >= -.5 && bounds.Right <= size.Width + .5 && bounds.Bottom <= size.Height + .5,
                    $"{language} {size}: {control.GetType().Name} '{AutomationProperties.GetName(control)}' exceeds workspace bounds: {bounds}.");
            }
            foreach (var wrap in Descendants(content).OfType<WrapPanel>())
            {
                var items = wrap.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 1)
                    .Select(c => c.TransformToAncestor(wrap).TransformBounds(new Rect(c.RenderSize))).ToArray();
                for (var i = 0; i < items.Length; i++)
                for (var j = i + 1; j < items.Length; j++)
                {
                    var overlap = Rect.Intersect(items[i], items[j]);
                    Assert(overlap.IsEmpty || overlap.Width * overlap.Height < 1, "Toolbar controls overlap.");
                }
            }
            RenderContent(content, window.Background, $"layout-{language}-{appearance.ToLowerInvariant()}-{size.Width}x{size.Height}-{tool.ToString().ToLowerInvariant()}.png");
            StopTimers(window);
        }
        L.Initialize("en"); Settings.Current.Appearance = "Light"; Appearance.Apply();
    }

    private static bool IsVisibleWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            if (current == ancestor) return true;
        }
        return false;
    }

    private static void EditingFit()
    {
        L.Initialize("de"); Settings.Current.Appearance = "Dark"; Appearance.Apply();
        var window = new MainWindow(); var content = (FrameworkElement)window.Content;
        var size = new Size(760, 560);
        content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
        var surface = (DocumentSurface)Field(window, "_surface")!;
        var original = DocumentStore.Serialize(Document(window));
        surface.FitForEditing(); content.UpdateLayout();
        Assert(surface.Zoom >= .7 || Document(window).Pages[0].Width * surface.Zoom >= surface.ActualWidth - 70,
            "Initial editing scale is too small without a width constraint.");
        var scroll = (ScrollViewer)Field(surface, "_scroll")!;
        Near(0, scroll.HorizontalOffset); Near(0, scroll.VerticalOffset);
        var canvas = Descendants(surface).OfType<InkCanvas>().Single();
        var origin = canvas.TranslatePoint(default, surface);
        Assert(origin.Y >= 0 && origin.Y < 70, "Editing fit hides the upper page.");
        Assert(scroll.ScrollableHeight > 0, "Readable editing fit should scroll vertically in a short viewport.");
        RenderContent(content, window.Background, "editing-fit-de-dark-760x560.png");
        surface.FitToPage(); content.UpdateLayout();
        var page = Document(window).Pages[0];
        Assert(page.Width * surface.Zoom + 64 <= surface.ActualWidth + .5 && page.Height * surface.Zoom + 64 <= surface.ActualHeight + .5,
            "Explicit full-page fit no longer fits the page.");
        Equal(original, DocumentStore.Serialize(Document(window)));
        StopTimers(window); L.Initialize("en"); Settings.Current.Appearance = "Light"; Appearance.Apply();
    }

    private static void RenderContent(FrameworkElement content, Brush background, string name)
    {
        var width = (int)Math.Ceiling(content.ActualWidth); var height = (int)Math.Ceiling(content.ActualHeight);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, width, height), Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(drawing); Save(image, name);
    }

    private static void LocalizationCoverage()
    {
        var strings = (Dictionary<string, (string En, string De)>)typeof(L).GetField("Strings", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        foreach (var (key, value) in strings)
        {
            Assert(!string.IsNullOrWhiteSpace(value.En) && !string.IsNullOrWhiteSpace(value.De), $"Incomplete localization: {key}");
            string Placeholders(string text) => string.Join(",", Regex.Matches(text, @"\{(\d+)(?:[^}]*)\}").Select(m => m.Groups[1].Value).Order());
            Equal(Placeholders(value.En), Placeholders(value.De));
        }
        var required = new[] { "Format", "Orientation", "Pattern", "Spacing", "CustomSpacing", "Margin", "InvalidPaper", "Blank", "Ruled", "Squared", "Custom", "Create", "Apply", "Cancel", "Save", "SaveAs", "Open", "Recovery", "PageCanvas", "Note", "CollapseNote", "ExpandNote", "MoveItem" }
            .Concat(Enum.GetValues<EditorTool>().SelectMany(t => new[] { t.ToString(), t + "Hint" }));
        foreach (var key in required) Assert(strings.ContainsKey(key), $"Control label is not localized: {key}");
    }

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static void StopTimers(MainWindow window)
    {
        ((System.Windows.Threading.DispatcherTimer)Field(window, "_editTimer")!).Stop();
        ((System.Windows.Threading.DispatcherTimer)Field(window, "_recoveryTimer")!).Stop();
        // Do not leave hidden document windows for Application.Shutdown to prompt.
        SetField(window, "_savedState", DocumentStore.Serialize(Document(window)));
        window.Close();
    }
    private static DocumentData Document(MainWindow window) => (DocumentData)Field(window, "_document")!;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static object? Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Property(object target, string name) => target.GetType().GetProperty(name, Private)!.GetValue(target);
    private static string State(PageData page) => DocumentStore.Serialize(new DocumentData { Pages = [page] });
    private static TextBox Editor(DependencyObject parent, string key) => Descendants(parent).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == L.T(key));
    private static void Layout(UIElement element) { element.Measure(new Size(1100, 800)); element.Arrange(new Rect(0, 0, 1100, 800)); element.UpdateLayout(); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static byte[] Pixels(BitmapSource image) { var result = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(result, image.PixelWidth * 4, 0); return result; }
    private static void Pixel(byte[] data, int width, int x, int y, byte r, byte g, byte b) { var i = (y * width + x) * 4; Assert(data[i] == b && data[i + 1] == g && data[i + 2] == r, $"Unexpected color at {x},{y}."); }
    private static void Save(BitmapSource image, string name) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(_output, name)); encoder.Save(stream); }
    private static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; actual {actual}.");
    private static void Near(double expected, double actual) => Assert(Math.Abs(expected - actual) < .01, $"Expected {expected}; actual {actual}.");
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void LoadApplicationResources(Application app)
    {
        // Copy the actual App.xaml resource declarations, not a second set of
        // test styles. This keeps theme regression coverage tied to production.
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml"));
        var resource = new XElement(ns + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            source.Root!.Element(ns + "Application.Resources")!.Elements().Select(e => new XElement(e)));
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(resource.ToString()));
    }
}
