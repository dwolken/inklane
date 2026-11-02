using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private static string _fixtures = "";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--clipboard-peer") return ClipboardChecks.Peer(args);
        _output = Path.GetFullPath(args.FirstOrDefault() ?? ".scratch/surface-test-output");
        Directory.CreateDirectory(_output);
        var run = Path.Combine(_output, "runs", Guid.NewGuid().ToString("N"));
        _fixtures = Path.Combine(run, "documents");
        Directory.CreateDirectory(_fixtures);
        Environment.SetEnvironmentVariable("INKLANE_DATA_HOME", Path.Combine(run, "profile"));
        // A plain Application avoids invoking product startup/settings/recovery
        // when a real modal dialog enters its nested dispatcher below.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
        TestResources.Load(app);
        L.Initialize("en");
        Settings.Current.Appearance = "Light";
        Appearance.Apply();
        Run("Windows clipboard between independent editor processes", () => ClipboardChecks.Run(run));
        Run("A4 raster dimensions and white page boundaries", PageRaster);
        Run("Original image pixels survive rendering without annotations", ImageRaster);
        Run("View, tool and clean export operations do not mutate document", ViewPurity);
        Run("Typed text edits persist Unicode and multiline content", EditableText);
        Run("Expanded note text and anchor survive collapse and reload", NoteRoundTrip);
        Run("Note marker and editor retain readable size at different zooms", NoteReadability);
        Run("Numbered note reveal scrolls to the anchor and preserves identity", NoteReveal);
        Run("Searching the index reveals a note on another page and restores the first view", NoteIndexNavigation);
        Run("Inline note titles synchronize with markers, search, PDF and undo", NoteTitleWorkflow);
        Run("Note-index duplicate and delete act on the chosen page and are reversible", NoteRowCommands);
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
        Run("View presets resize, manual zoom stays fixed, and page navigation restores position", ViewRestoration);
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
        Assert(!note.IsExpanded, "Note did not collapse."); Equal(0, changes);
        var clone = DocumentStore.Deserialize(State(page)).Pages[0];
        surface.LoadPage(clone); Layout(surface);
        Equal(text, clone.Items[1].Text); Near(x, clone.Items[1].X); Near(y, clone.Items[1].Y);
        var marker = NoteMarker(surface);
        marker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(clone.Items[1].IsExpanded, "Note did not expand.");
        Equal(text, Editor(surface, "Note").Text);
    }

    private static void NoteReadability()
    {
        var page = Sample(); var surface = Surface(page);
        var marker = NoteMarker(surface);
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

    private static void NoteReveal()
    {
        var page = Sample();
        var first = page.Items.Single(i => i.IsNote);
        var second = new TextItem { IsNote = true, IsExpanded = false, X = 670, Y = 1010,
            Width = 280, Height = 180, Text = "Second note\nGrüße — preserved." };
        page.Items.Add(second);
        var surface = Surface(page);
        surface.Zoom = 2;
        var changes = 0;
        surface.Changed += (_, _) => changes++;
        var selected = new List<string?>();
        surface.NoteSelectionChanged += (_, _) => selected.Add(surface.SelectedNoteId);

        Assert(surface.RevealNote(second.Id), "Existing note could not be revealed.");
        Equal(second.Id, surface.SelectedNoteId);
        Assert(selected.Contains(second.Id) && second.IsExpanded, "Revealed note was not selected and expanded.");
        var marker = Descendants(surface).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "NoteMarker:" + second.Id);
        Equal(L.F("NoteNumber", 2), AutomationProperties.GetName(marker));
        Equal("2", marker.Content.ToString());
        var bounds = marker.TransformToAncestor(surface).TransformBounds(new Rect(marker.RenderSize));
        Assert(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight).Contains(bounds), "Offscreen note anchor was not brought into view.");
        Near(670, second.X); Near(1010, second.Y);
        surface.RevealNote(second.Id);
        Equal(0, changes);
        Assert(!surface.RevealNote("missing"), "Unknown note reported success.");

        surface.RevealNote(first.Id);
        surface.DeleteSelection();
        Equal("1", marker.Content.ToString());
        Equal(L.F("NoteNumber", 1), AutomationProperties.GetName(marker));
        Equal("Second note\nGrüße — preserved.", second.Text);
    }

    private static void ObjectMovement()
    {
        var page = Sample(); var surface = Surface(page); surface.Zoom = 1.7; surface.Tool = EditorTool.Select; Layout(surface);
        var grip = Descendants(surface).OfType<Thumb>().Single(t => AutomationProperties.GetAutomationId(t) == "TextMove:" + page.Items[0].Id);
        Assert(grip.Visibility == Visibility.Visible && Editor(surface, "Text").IsReadOnly, "Select mode does not expose direct text movement.");
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
        surface.Tool = EditorTool.Text;
        var editor = Editor(surface, "Text");
        Assert(grip.Visibility == Visibility.Collapsed && !editor.IsReadOnly, "Text tool cannot edit a moved object.");
        editor.Text = "The moved text stays editable.";
        Equal(editor.Text, item.Text); Near(x + 20, item.X); Near(y + 30, item.Y);
    }

    private static void NoteViewportPlacement()
    {
        var page = Sample(); page.Items[1].Y = 550;
        var surface = Surface(page); var original = State(page); var changes = 0;
        surface.Changed += (_, _) => changes++;
        var editor = Editor(surface, "Note");
        var card = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(editor));
        var marker = NoteMarker(surface);
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
        var content = (FrameworkElement)window.Content;
        Layout(content);
        var imageEntry = Named<Button>(content, "OpenImage");
        var bookEntry = Named<Button>(content, "NewNotebook");
        Assert(IsVisibleWithin(imageEntry, content) && IsVisibleWithin(bookEntry, content), "Home is missing a workspace entry.");
        Near(imageEntry.ActualWidth, bookEntry.ActualWidth);
        RenderContent(content, window.Background, "home.png");
        StopTimers(window);
    }

    private static void PageHistory()
    {
        var window = OpenNotebook();
        Named<Button>(window.Content as DependencyObject ?? window, "AddPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Equal(2, Document(window).Pages.Count);
        Assert((bool)Property(window, "IsDirty")!, "Added page is not dirty.");
        Call(window, "Undo"); Equal(1, Document(window).Pages.Count);
        Assert(!(bool)Property(window, "IsDirty")!, "Undo to saved state remains dirty.");
        Call(window, "Redo"); Equal(2, Document(window).Pages.Count);
        StopTimers(window);
    }

    private static void TextHistory()
    {
        var window = OpenNotebook(withSample: true); var page = Document(window).Pages[0];
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
            var window = OpenNotebook(withSample: true);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1100, 800)); content.Arrange(new Rect(0, 0, 1100, 800)); content.UpdateLayout();
            var surface = (DocumentSurface)Field(window, "_surface")!;
            surface.FitForEditing(); content.UpdateLayout();
            var button = Descendants(content).OfType<Button>().First(b => AutomationProperties.GetName(b) == L.T("Save"));
            var combo = Descendants(content).OfType<ComboBox>().First();
            // Theme switching replaces dictionary instances. Static BasedOn
            // keeps the template; its DynamicResource brushes update in place.
            Assert(button.Style != null && button.OverridesDefaultStyle, "Save command lost its studio button style.");
            Assert(combo.Style?.BasedOn?.TargetType == typeof(ComboBox) && combo.OverridesDefaultStyle,
                "ComboBox lost its Fluent base style.");
            Assert(button.Template.FindName("Shell", button) is Border { CornerRadius.TopLeft: > 0 },
                "Studio command template is missing.");
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
            var blank = Named<RadioButton>(dialog, "Blank");
            var spacing = Named<ComboBox>(dialog, "Spacing");
            var custom = Named<TextBox>(dialog, "CustomSpacing");
            Assert(blank.IsChecked == true, "Blank paper is not selected initially.");
            foreach (var key in new[] { "Blank", "Ruled", "Squared" })
                Assert(IsVisibleWithin(Named<RadioButton>(dialog, key), dialog), $"Paper choice {key} is hidden.");
            Assert(!IsVisibleWithin(spacing, dialog) && !IsVisibleWithin(custom, dialog), "Blank paper exposes line spacing.");
            CaptureDialog(dialog, "paper-en-light-blank");
            Named<RadioButton>(dialog, "Ruled").IsChecked = true; dialog.UpdateLayout();
            Assert(IsVisibleWithin(spacing, dialog), "Ruled paper hides spacing presets.");
            Assert(!IsVisibleWithin(custom, dialog), "Preset spacing exposes a redundant custom field.");
            spacing.SelectedIndex = 1;
            Named<RadioButton>(dialog, "A5").IsChecked = true;
            Named<RadioButton>(dialog, "Landscape").IsChecked = true;
            Assert(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "210 × 148 mm"), "Paper preview did not update its dimensions.");
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
            var spacing = Named<ComboBox>(dialog, "Spacing");
            var custom = Named<TextBox>(dialog, "CustomSpacing");
            var margin = Named<TextBox>(dialog, "Margin");
            Named<RadioButton>(dialog, "Squared").IsChecked = true; spacing.SelectedIndex = 2; dialog.UpdateLayout();
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
        var margin = Named<TextBox>(dialog, "Margin");
        var scroll = Descendants(dialog).OfType<ScrollViewer>().Single(s => s.Content is Panel panel && panel.IsAncestorOf(margin));
        scroll.ScrollToBottom(); scroll.UpdateLayout();
        Near(scroll.ScrollableHeight, scroll.VerticalOffset);
        var action = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, L.T("Create")));
        var body = (FrameworkElement)dialog.Content;
        var bounds = action.TransformToAncestor(body).TransformBounds(new Rect(action.RenderSize));
        Assert(new Rect(body.RenderSize).Contains(bounds), "Dialog actions are clipped after scrolling the form.");
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
            var window = OpenNotebook(); var content = (FrameworkElement)window.Content;
            Call(window, "SetTool", tool);
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            var surface = (DocumentSurface)Field(window, "_surface")!;
            surface.FitToPage(); content.UpdateLayout();
            Assert(surface.ActualHeight >= size.Height * .65 && surface.ActualWidth >= size.Width * .85, $"Document space collapsed at {size}.");
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
        var window = OpenNotebook(); var content = (FrameworkElement)window.Content;
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

    private static void ViewRestoration()
    {
        var window = OpenNotebook();
        var content = (FrameworkElement)window.Content;
        var surface = (DocumentSurface)Field(window, "_surface")!;
        var scroll = (ScrollViewer)Field(surface, "_scroll")!;
        void Resize(double width, double height)
        {
            var size = new Size(width, height);
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
        }
        surface.FitToWidth();
        foreach (var size in new[] { new Size(760, 560), new Size(1600, 880) })
        {
            Resize(size.Width, size.Height);
            Equal(PageViewMode.FitWidth, surface.ViewMode);
            var gutter = scroll.ViewportWidth - Document(window).Pages[0].Width * surface.Zoom;
            Assert(Math.Abs(gutter - 68) < 2, $"Fit width left {gutter} DIPs outside the page.");
            Assert(scroll.ScrollableWidth < 1, "Width fit introduced horizontal scrolling.");
        }
        Assert(surface.Zoom > 1.4, "True width fit was limited by the editing cap.");
        surface.FitForEditing(); Near(1.4, surface.Zoom);
        Resize(760, 560);
        Assert(surface.Zoom < 1, "Editing fit did not adapt to the narrower viewport.");
        surface.Zoom = 1.75;
        Resize(1000, 720); Near(1.75, surface.Zoom); Equal(PageViewMode.Manual, surface.ViewMode);
        scroll.ScrollToHorizontalOffset(150); scroll.ScrollToVerticalOffset(420); surface.UpdateLayout();
        var before = surface.CaptureView();
        Assert(before.HorizontalOffset > 0 && before.VerticalOffset > 0, "Test did not scroll the source page.");
        Named<Button>(content, "AddPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Equal(2, Document(window).Pages.Count);
        Near(1.75, surface.Zoom); Near(0, scroll.VerticalOffset); Near(0, scroll.HorizontalOffset);
        scroll.ScrollToVerticalOffset(60); surface.UpdateLayout();
        Named<Button>(content, "PreviousPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Near(before.Zoom, surface.Zoom); Near(before.HorizontalOffset, scroll.HorizontalOffset); Near(before.VerticalOffset, scroll.VerticalOffset);
        Named<Button>(content, "NextPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Near(60, scroll.VerticalOffset);
        Named<Button>(content, "PreviousPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        surface.Zoom = 1.3;
        Named<Button>(content, "AddPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Near(1.3, surface.Zoom); Near(0, scroll.VerticalOffset);
        Named<Button>(content, "NextPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Near(1.75, surface.Zoom); Near(60, scroll.VerticalOffset);
        StopTimers(window);
    }

    private static void NoteIndexNavigation()
    {
        var first = Sample();
        var second = new PageData();
        var target = new TextItem { IsNote = true, IsExpanded = false, X = 550, Y = 980, Text = "Schnittkante\nCheck the lower edge." };
        second.Items.Add(target);
        var path = Path.Combine(_fixtures, "Indexed notebook.pdf");
        DocumentStore.SavePdf(new DocumentData { Pages = [first, second] }, path);
        var interaction = new PromptProbe();
        var window = new MainWindow(interaction); window.OpenFile(path);
        var content = (FrameworkElement)window.Content; Layout(content);
        var surface = (DocumentSurface)Field(window, "_surface")!;
        surface.Zoom = 1.75;
        var scroll = (ScrollViewer)Field(surface, "_scroll")!;
        scroll.ScrollToVerticalOffset(230); surface.UpdateLayout();
        var previous = surface.CaptureView();
        Named<Button>(content, "NoteIndex").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        content.UpdateLayout();
        Named<TextBox>(content, "NoteSearch").Text = "Schnittkante";
        var row = Descendants(content).OfType<Button>().Single(b => AutomationProperties.GetName(b).Contains("Schnittkante", StringComparison.Ordinal));
        row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        content.UpdateLayout();
        Equal(1, (int)Field(window, "_pageIndex")!);
        Equal(target.Id, surface.SelectedNoteId);
        Assert(Document(window).Pages[1].Items[0].IsExpanded, "Index selection left the target note collapsed.");
        var marker = NoteMarker(surface);
        var bounds = marker.TransformToAncestor(surface).TransformBounds(new Rect(marker.RenderSize));
        Assert(new Rect(surface.RenderSize).Contains(bounds), "Note index jumped to a page with an offscreen anchor.");
        Assert((bool)Call(window, "CanLeave")!, "Reading a saved note prevents leaving.");
        Equal(0, interaction.Prompts);
        Equal(1, ((System.Collections.ICollection)Field(window, "_history")!).Count);
        var editor = Editor(surface, "Note");
        editor.Text += "\nConfirmed.";
        Assert((bool)Property(window, "IsDirty")!, "Editing note content is not dirty.");
        Assert(!(bool)Call(window, "CanLeave")!, "Canceling the unsaved-note prompt did not keep the document open.");
        Equal(1, interaction.Prompts);
        Assert((bool)Call(window, "Save", false)!, "Edited note failed to save.");
        var saved = DocumentStore.LoadPdf(path).Pages[1].Items[0];
        Equal(editor.Text, saved.Text); Assert(saved.IsExpanded, "Save did not retain the current note presentation.");
        Named<Button>(content, "PreviousPage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Near(previous.Zoom, surface.Zoom); Near(previous.VerticalOffset, scroll.VerticalOffset);
        StopTimers(window);
    }

    private static void NoteTitleWorkflow()
    {
        var window = OpenNotebook(withSample: true);
        var content = (FrameworkElement)window.Content;
        var surface = (DocumentSurface)Field(window, "_surface")!;
        var note = Document(window).Pages[0].Items.Single(item => item.IsNote);
        Named<Button>(content, "NoteIndex").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(content);
        var rowId = "NoteRow:" + note.Id;
        Button Row() => Descendants(content).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == rowId);
        var row = Row();
        row.ContextMenu!.PlacementTarget = row;
        var rename = row.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header,L.T("RenameNote")));
        Equal(L.T("RenameNote"),rename.Header);
        rename.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Equal(note.Id,surface.SelectedNoteId);
        var title = Descendants(surface).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "NoteTitle:" + note.Id);
        title.Text = "Threshold detail";
        Equal("Threshold detail",note.Title);
        Equal(note.Title,AutomationProperties.GetName(NoteMarker(surface)));
        if (((Border)Field(window,"_notesPanel")!).Visibility != Visibility.Visible)
            Named<Button>(content,"NoteIndex").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var search = Named<TextBox>(content,"NoteSearch");
        search.Text = "THRESHOLD"; Layout(content);
        Assert(AutomationProperties.GetName(Row()).Contains(note.Title),"Title query did not match the renamed note.");
        search.Text = "Grüße"; Layout(content);
        Assert(AutomationProperties.GetName(Row()).Contains(note.Title),"Body query did not match the named note.");
        search.Text = "";
        Layout(content);
        RenderContent(content, Brushes.White, "named-note-workspace.png");
        Call(window,"Undo"); Layout(content);
        Equal("",Document(window).Pages[0].Items.Single(i => i.IsNote).Title);
        Call(window,"Redo"); Layout(content);
        Equal("Threshold detail",Document(window).Pages[0].Items.Single(i => i.IsNote).Title);
        Assert((bool)Call(window,"Save",false)!,"Renamed note was not saved.");
        string file = (string)Field(window,"_filePath")!;
        window.OpenFile(file); Layout(content);
        Equal("Threshold detail",AutomationProperties.GetName(NoteMarker(surface)));
        StopTimers(window);
    }

    private static void NoteRowCommands()
    {
        var path = Path.Combine(_fixtures,"Shelf details.pdf");
        var first = Sample();
        var target = new TextItem { IsNote = true, IsExpanded = false, Title = "Shelf pin", Text = "Drill 25 mm deep.", X = 90, Y = 400 };
        DocumentStore.SavePdf(new DocumentData { Pages = [first,new PageData { Items = [target] }] },path);
        var window = new MainWindow(new PromptProbe());
        window.OpenFile(path);
        var content = (FrameworkElement)window.Content;
        void ActionOn(string id,string command)
        {
            if (((Border)Field(window,"_notesPanel")!).Visibility != Visibility.Visible)
                Named<Button>(content,"NoteIndex").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(content);
            var row = Descendants(content).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "NoteRow:" + id);
            row.ContextMenu!.PlacementTarget = row;
            row.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header,L.T(command)))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        ActionOn(target.Id,"DuplicateObjects");
        Equal(1,(int)Field(window,"_pageIndex")!);
        var copy = Document(window).Pages[1].Items.Single(i => i.Id != target.Id);
        Equal(target.Title,copy.Title); Equal(target.Text,copy.Text);
        ActionOn(target.Id,"Delete");
        Equal(copy.Id,Document(window).Pages[1].Items.Single().Id);
        Call(window,"Undo");
        Assert(Document(window).Pages[1].Items.Any(i => i.Id == target.Id),"Undo lost the original note.");
        Equal(first.Items.Count,Document(window).Pages[0].Items.Count);
        StopTimers(window);
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
        SetField(window, "_savedState", DocumentStore.SerializeForHistory(Document(window)));
        window.Close();
    }
    private static DocumentData Document(MainWindow window) => (DocumentData)Field(window, "_document")!;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static object? Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Property(object target, string name) => target.GetType().GetProperty(name, Private)!.GetValue(target);
    private static string State(PageData page) => DocumentStore.Serialize(new DocumentData { Pages = [page] });
    private static TextBox Editor(DependencyObject parent, string key) => Descendants(parent).OfType<TextBox>().Single(t =>
        AutomationProperties.GetAutomationId(t).StartsWith(key == "Note" ? "NoteEditor:" : "TextEditor:", StringComparison.Ordinal));
    private static Button NoteMarker(DependencyObject parent) => Descendants(parent).OfType<Button>()
        .Single(b => AutomationProperties.GetAutomationId(b).StartsWith("NoteMarker:", StringComparison.Ordinal));
    private static MainWindow OpenNotebook(bool withSample = false)
    {
        var window = new MainWindow();
        var path = Path.Combine(_fixtures, withSample ? "Field notebook.pdf" : "Blank notebook.pdf");
        if (!File.Exists(path)) DocumentStore.SavePdf(new DocumentData { Pages = [withSample ? Sample() : new PageData()] }, path);
        window.OpenFile(path);
        Layout((FrameworkElement)window.Content);
        return window;
    }
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

    private sealed class PromptProbe : IDocumentInteraction
    {
        public int Prompts { get; private set; }
        public int Choose(Window owner, string title, string message, params string[] buttons) { Prompts++; return buttons.Length - 1; }
        public string? ChooseSavePath(Window owner, Microsoft.Win32.SaveFileDialog dialog) => throw new InvalidOperationException("Unexpected Save As dialog.");
        public void ShowMessage(Window owner, string message, string title, MessageBoxImage icon = MessageBoxImage.Information) => throw new InvalidOperationException(message);
    }
}
