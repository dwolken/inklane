using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Inklane;
using Inklane.Core;
using Microsoft.Win32;

namespace Inklane.LifecycleTests;

/// <summary>Exercise the real window's lifecycle and filesystem, substituting only user decisions.</summary>
internal static class Program
{
    private static string output = "";
    private static int checks, failures;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.FirstOrDefault() ?? ".scratch/lifecycle-tests");
        Directory.CreateDirectory(output);
        var dataOverride = typeof(App).GetProperty("DataDirectoryOverride", BindingFlags.Static | BindingFlags.NonPublic);
        if (dataOverride is null) throw new InvalidOperationException("A data-directory seam is required to isolate lifecycle tests from user data.");
        dataOverride.SetValue(null, Path.Combine(output, "app-data"));
        var app = new App();
        app.InitializeComponent();
        L.Initialize("en");
        Settings.Current.Appearance = "Light";
        Appearance.Apply();
        Run("Fresh notebook leaves without asking to save", UnchangedDocument);
        Run("Failed save preserves dirty document and original file", SaveFailure);
        Run("Same-file reopen honors cancel and then saves current edits", SameFileReopen);
        Run("Notebook JSON import chooses PDF instead of replacing JSON", NotebookJsonImport);
        Run("New image sidecar collision preserves external data", SidecarCollision);
        Run("Canceling unsaved changes or save path prevents switching", CancelUnsaved);
        Run("Failed recovery replacement retains the previous recovery file", RecoveryFailure);
        Run("Undo to saved state clears stale recovery snapshot", UndoClearsRecovery);
        Run("Invalid open leaves the active document and path intact", InvalidOpen);
        Run("Save As rejects the wrong extension without changing existing data", WrongSaveExtension);
        Run("JSON export cannot replace the active notebook PDF", ExportJsonOverPdf);
        Run("PNG export cannot replace the active image sidecar", ExportImageOverSidecar);
        Run("JSON export cannot replace its active JSON source", ExportJsonOverSidecar);
        Run("Changed source image blocks Save while JSON export preserves work", ChangedSourceImage);
        Run("Crossing a document limit rolls back safely without ending the session", DocumentLimitRollback);
        Console.WriteLine($"{checks - failures}/{checks} checks passed. Artifacts: {output}");
        app.Shutdown();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string description, Action test)
    {
        checks++;
        try { test(); Console.WriteLine("PASS " + description); }
        catch (Exception ex) { failures++; Console.WriteLine($"FAIL {description}\n{ex}"); }
    }

    private static void UnchangedDocument()
    {
        using var state = new WindowCase("unchanged");
        Assert(!Dirty(state.Window), "New notebook is marked dirty.");
        Assert(Call<bool>(state.Window, "CanLeave"), "Cannot leave unchanged notebook.");
        Equal(0, state.Interaction.ChoiceCalls);
    }

    private static void SaveFailure()
    {
        using var state = new WindowCase("save-failure");
        var path = state.Path("existing.pdf");
        DocumentStore.SavePdf(Notebook("Previously saved"), path);
        state.Window.OpenFile(path);
        AddText(state.Window, "Meaningful unsaved edit");
        var beforeFile = DocumentStore.GetFileHash(path);
        var beforeState = State(state.Window);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            Assert(!Call<bool>(state.Window, "Save", false), "Read-only save reported success.");
            Assert(Dirty(state.Window), "Failed save marked document clean.");
            Equal(beforeState, State(state.Window));
            Equal(beforeFile, DocumentStore.GetFileHash(path));
            Assert(!Field<bool>(state.Window, "_saving"), "Save flag remains stuck after failure.");
            Assert(state.Interaction.Messages.Any(m => m.Icon == MessageBoxImage.Error), "Save failure was not reported.");
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        Assert(Call<bool>(state.Window, "Save", false), "Document cannot save after the failure is resolved.");
        Assert(!Dirty(state.Window), "Successful save remains dirty.");
        Assert(DocumentStore.LoadPdf(path).Pages[0].Items.Any(i => i.Text == "Meaningful unsaved edit"), "Retry lost current edit.");
    }

    private static void SameFileReopen()
    {
        using var state = new WindowCase("same-file");
        var path = state.Path("same.pdf");
        DocumentStore.SavePdf(Notebook("Original"), path);
        state.Window.OpenFile(path);
        AddText(state.Window, "Save before rereading this file");
        var current = State(state.Window);
        state.Interaction.Choices.Enqueue(2);
        state.Window.OpenFile(path);
        Equal(current, State(state.Window));
        Assert(Dirty(state.Window), "Canceled reopen discarded changes.");
        state.Interaction.Choices.Enqueue(0);
        state.Window.OpenFile(path);
        Equal(current, State(state.Window));
        Assert(!Dirty(state.Window), "Saved reopen remains dirty.");
        Equal(current, DocumentStore.Serialize(DocumentStore.LoadPdf(path)));
        Equal(2, state.Interaction.ChoiceCalls);
    }

    private static void NotebookJsonImport()
    {
        using var state = new WindowCase("json-import");
        var json = state.Path("export.annotations.json");
        var pdf = state.Path("imported.pdf");
        DocumentStore.SaveJson(Notebook("Imported annotations"), json);
        var beforeJson = DocumentStore.GetFileHash(json);
        state.Window.OpenFile(json);
        Assert(Field<string?>(state.Window, "_filePath") is null, "JSON became the notebook's PDF save target.");
        AddText(state.Window, "New notebook content");
        state.Interaction.SavePaths.Enqueue(pdf);
        Assert(Call<bool>(state.Window, "Save", false), "Imported notebook save failed.");
        Equal(beforeJson, DocumentStore.GetFileHash(json));
        Equal(pdf, Field<string?>(state.Window, "_filePath"));
        Assert(state.Interaction.SaveFilters.Single().Contains("*.pdf", StringComparison.OrdinalIgnoreCase), "Notebook import did not request a PDF path.");
        Equal(State(state.Window), DocumentStore.Serialize(DocumentStore.LoadPdf(pdf)));
    }

    private static void SidecarCollision()
    {
        using var state = new WindowCase("sidecar-collision");
        var image = state.Path("source.png");
        WriteImage(image);
        var sourceHash = DocumentStore.GetFileHash(image);
        state.Window.OpenFile(image);
        AddText(state.Window, "My image annotations");
        var sidecar = image + ".inklane.json";
        const string externallyCreated = "External work created after Inklane opened the image.";
        File.WriteAllText(sidecar, externallyCreated);
        state.Interaction.SavePaths.Enqueue(sidecar);
        Assert(!Call<bool>(state.Window, "Save", false), "Save replaced a sidecar that appeared externally.");
        Equal(externallyCreated, File.ReadAllText(sidecar));
        Assert(Dirty(state.Window), "Rejected collision marked edits saved.");
        var alternative = state.Path("safe-annotations.json");
        state.Interaction.SavePaths.Enqueue(alternative);
        Assert(Call<bool>(state.Window, "Save", false), "Saving a separate sidecar failed.");
        Equal(externallyCreated, File.ReadAllText(sidecar));
        Equal(sourceHash, DocumentStore.GetFileHash(image));
        Equal("My image annotations", DocumentStore.LoadJson(alternative).Pages[0].Items.Single().Text);
    }

    private static void CancelUnsaved()
    {
        using var state = new WindowCase("cancel-unsaved");
        AddText(state.Window, "Keep this unsaved notebook");
        var before = State(state.Window);
        var target = state.Path("other.pdf");
        DocumentStore.SavePdf(Notebook("Different document"), target);
        state.Interaction.Choices.Enqueue(2);
        state.Window.OpenFile(target);
        Equal(before, State(state.Window));
        Assert(Dirty(state.Window), "Canceled switch dropped dirty state.");
        state.Interaction.Choices.Enqueue(0);
        state.Interaction.SavePaths.Enqueue(null);
        state.Window.OpenFile(target);
        Equal(before, State(state.Window));
        Assert(Field<string?>(state.Window, "_filePath") is null, "Canceled save changed current path.");
        Assert(Dirty(state.Window), "Canceled save dropped dirty state.");
    }

    private static void RecoveryFailure()
    {
        var recoveryFolder = System.IO.Path.Combine(output, "recovery-failure", "Recovery");
        Directory.CreateDirectory(recoveryFolder);
        var originalPath = System.IO.Path.Combine(recoveryFolder, "previous.json");
        using (var original = new WindowCase("recovery-original"))
        {
            SetField(original.Window, "_recoveryPath", originalPath);
            AddText(original.Window, "Recover all this text");
            Assert(Call<bool>(original.Window, "WriteRecovery"), "Initial recovery write failed.");
        }
        var previousHash = DocumentStore.GetFileHash(originalPath);
        using var restored = new WindowCase("recovery-restored");
        var blockedPath = System.IO.Path.Combine(recoveryFolder, "new.json");
        Directory.CreateDirectory(blockedPath); // A directory cannot be replaced with the recovery file.
        SetField(restored.Window, "_recoveryPath", blockedPath);
        restored.Interaction.Choices.Enqueue(0);
        restored.Window.OfferRecovery();
        Equal("Recover all this text", Document(restored.Window).Pages[0].Items.Single().Text);
        Assert(Dirty(restored.Window), "Restored unsaved work is not dirty.");
        Assert(File.Exists(originalPath), "Failed recovery replacement deleted the only previous snapshot.");
        Equal(previousHash, DocumentStore.GetFileHash(originalPath));
        Equal(1, restored.Interaction.ChoiceCalls);
    }

    private static void UndoClearsRecovery()
    {
        using var state = new WindowCase("undo-recovery");
        Call(state.Window, "AddPage");
        Assert(Call<bool>(state.Window, "WriteRecovery"), "Modified notebook did not create recovery snapshot.");
        var recovery = Field<string>(state.Window, "_recoveryPath");
        Assert(File.Exists(recovery), "Recovery snapshot is missing.");
        Call(state.Window, "Undo");
        Assert(!Dirty(state.Window), "Undo did not return to saved state.");
        // Invoke the same callback the recovery timer uses, without a timing-dependent sleep.
        Assert(Call<bool>(state.Window, "WriteRecovery"), "Clean recovery callback failed.");
        Assert(!File.Exists(recovery), "Stale recovery could resurrect an edit already undone.");
    }

    private static void InvalidOpen()
    {
        using var state = new WindowCase("invalid-open");
        var original = state.Path("original.pdf");
        DocumentStore.SavePdf(Notebook("Existing work"), original);
        state.Window.OpenFile(original);
        var before = State(state.Window);
        var invalid = state.Path("broken.pdf");
        File.WriteAllText(invalid, "%PDF-1.7\n1 0 obj\n<< /Type /Catalog");
        state.Window.OpenFile(invalid);
        Equal(before, State(state.Window));
        Equal(original, Field<string?>(state.Window, "_filePath"));
        Assert(state.Interaction.Messages.Any(m => m.Icon == MessageBoxImage.Error), "Invalid PDF error was not reported.");
    }

    private static void WrongSaveExtension()
    {
        using var state = new WindowCase("wrong-save-extension");
        AddText(state.Window, "My unsaved notebook");
        var wrongPath = state.Path("existing.json");
        const string priorWork = "Unrelated preexisting JSON data";
        File.WriteAllText(wrongPath, priorWork);
        var before = State(state.Window);
        state.Interaction.SavePaths.Enqueue(wrongPath);
        Assert(!Call<bool>(state.Window, "Save", true), "Notebook Save As accepted a JSON extension.");
        Equal(priorWork, File.ReadAllText(wrongPath));
        Equal(before, State(state.Window));
        Assert(Field<string?>(state.Window, "_filePath") is null && Dirty(state.Window), "Rejected save changed path or dirty state.");
        Assert(state.Interaction.Messages.Any(m => m.Icon == MessageBoxImage.Error), "Wrong extension was not explained.");
    }

    private static void ExportJsonOverPdf()
    {
        using var state = new WindowCase("json-over-pdf");
        var path = state.Path("notebook.pdf");
        DocumentStore.SavePdf(Notebook("Protect this notebook"), path);
        state.Window.OpenFile(path);
        var beforeHash = DocumentStore.GetFileHash(path);
        var beforeState = State(state.Window);
        state.Interaction.SavePaths.Enqueue(path);
        Call(state.Window, "ExportJson");
        Equal(beforeHash, DocumentStore.GetFileHash(path));
        Equal(beforeState, State(state.Window));
        Assert(!Dirty(state.Window), "Rejected export changed document state.");
        Equal(beforeState, DocumentStore.Serialize(DocumentStore.LoadPdf(path)));
        Assert(state.Interaction.Messages.Any(m => m.Icon == MessageBoxImage.Error), "Unsafe export was not explained.");
    }

    private static void ExportImageOverSidecar()
    {
        using var state = new WindowCase("png-over-sidecar");
        var source = state.Path("image.png"); WriteImage(source);
        state.Window.OpenFile(source); AddText(state.Window, "Persist these annotations");
        Assert(Call<bool>(state.Window, "Save", false), "Initial sidecar save failed.");
        var path = Field<string>(state.Window, "_filePath");
        var beforeHash = DocumentStore.GetFileHash(path);
        var sourceHash = DocumentStore.GetFileHash(source);
        state.Interaction.SavePaths.Enqueue(path);
        Call(state.Window, "ExportImage");
        Equal(beforeHash, DocumentStore.GetFileHash(path));
        Equal(sourceHash, DocumentStore.GetFileHash(source));
        Equal("Persist these annotations", DocumentStore.LoadJson(path).Pages[0].Items.Single().Text);
        Assert(state.Interaction.Messages.Any(m => m.Icon == MessageBoxImage.Error), "Unsafe PNG target was not explained.");
    }

    private static void ExportJsonOverSidecar()
    {
        using var state = new WindowCase("json-over-sidecar");
        var source = state.Path("image.png"); WriteImage(source);
        state.Window.OpenFile(source); AddText(state.Window, "Existing saved annotations");
        Assert(Call<bool>(state.Window, "Save", false), "Initial sidecar save failed.");
        var path = Field<string>(state.Window, "_filePath");
        var beforeHash = DocumentStore.GetFileHash(path);
        AddText(state.Window, "Later unsaved edit");
        var beforeState = State(state.Window);
        state.Interaction.SavePaths.Enqueue(path);
        Call(state.Window, "ExportJson");
        Equal(beforeHash, DocumentStore.GetFileHash(path));
        Equal(beforeState, State(state.Window));
        Assert(Dirty(state.Window), "Rejected export marked unsaved edits clean.");
        Equal(1, DocumentStore.LoadJson(path).Pages[0].Items.Count);
        Assert(state.Interaction.Messages.Any(m => m.Icon == MessageBoxImage.Error), "Active sidecar overwrite was not explained.");
    }

    private static void ChangedSourceImage()
    {
        using var state = new WindowCase("changed-source-image");
        var source = state.Path("image.png"); WriteImage(source);
        state.Window.OpenFile(source); AddText(state.Window, "Important annotations on the loaded image");
        var before = State(state.Window);
        File.AppendAllText(source, "External image modification");
        var externalHash = DocumentStore.GetFileHash(source);
        Assert(!Call<bool>(state.Window, "Save", false), "Save reported success against a changed image.");
        Assert(Dirty(state.Window), "Changed-source refusal marked annotations saved.");
        Equal(before, State(state.Window));
        Equal(externalHash, DocumentStore.GetFileHash(source));
        Assert(!File.Exists(source + ".inklane.json"), "Refused save created a misleading sidecar.");
        var export = state.Path("preserved.annotations.json");
        state.Interaction.SavePaths.Enqueue(export);
        Call(state.Window, "ExportJson");
        Equal(before, DocumentStore.Serialize(DocumentStore.LoadJson(export)));
        Assert(Dirty(state.Window), "Preservation export marked the invalid source relationship saved.");
        Assert(state.Interaction.Messages.Any(m => m.Icon == MessageBoxImage.Error), "Source change was not explained.");
    }

    private static void DocumentLimitRollback()
    {
        using var state = new WindowCase("document-limit");
        AddText(state.Window, "Last valid editing state");
        var valid = State(state.Window);
        // Exercise the real change callback with a model beyond the declared per-page item limit.
        // No fake success or modified limit: the core validator and window rollback both run.
        Document(state.Window).Pages[0].Items.AddRange(Enumerable.Range(0, 10000).Select(_ => new TextItem()));
        Call(state.Window, "DocumentChanged");
        Equal(valid, State(state.Window));
        Assert(Dirty(state.Window), "Rollback lost the existing unsaved-state indication.");
        Assert(state.Interaction.Messages.Any(m => m.Icon == MessageBoxImage.Error), "Limit rejection was not explained.");
        AddText(state.Window, "Editing can continue");
        Equal(2, Document(state.Window).Pages[0].Items.Count);
        var path = state.Path("after-limit.pdf");
        state.Interaction.SavePaths.Enqueue(path);
        Assert(Call<bool>(state.Window, "Save", false), "Session could not save after safe rollback.");
        Equal(2, DocumentStore.LoadPdf(path).Pages[0].Items.Count);
    }

    private static DocumentData Notebook(string text) => new() { Pages = [new PageData { Items = [new TextItem { Text = text, X = 60, Y = 60 }] }] };
    private static void AddText(MainWindow window, string text)
    {
        Document(window).Pages[0].Items.Add(new TextItem { Text = text, X = 80, Y = 180 });
        Call(window, "ShowPage", false);
        Call(window, "CaptureHistory");
        Call(window, "DocumentChanged");
    }

    private static void WriteImage(string path)
    {
        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgr32, null, Enumerable.Repeat((byte)220, 64 * 64 * 4).ToArray(), 64 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    private sealed class WindowCase : IDisposable
    {
        public readonly MainWindow Window;
        public readonly FakeInteraction Interaction = new();
        private readonly string directory;
        public WindowCase(string name)
        {
            directory = System.IO.Path.Combine(output, name); Directory.CreateDirectory(directory);
            Window = new MainWindow(Interaction);
            SetField(Window, "_recoveryPath", System.IO.Path.Combine(directory, "Recovery", "session.json"));
        }
        public string Path(string filename) => System.IO.Path.Combine(directory, filename);
        public void Dispose()
        {
            Window.StopBackgroundWork();
            SetField(Window, "_closing", true);
            Window.Close();
            Assert(Interaction.UnexpectedCalls.Count == 0, string.Join("\n", Interaction.UnexpectedCalls));
            Assert(Interaction.Choices.Count == 0 && Interaction.SavePaths.Count == 0, "An expected user decision was never requested.");
        }
    }

    private sealed class FakeInteraction : IDocumentInteraction
    {
        public readonly Queue<int> Choices = new();
        public readonly Queue<string?> SavePaths = new();
        public readonly List<(string Text, MessageBoxImage Icon)> Messages = [];
        public readonly List<string> SaveFilters = [];
        public readonly List<string> UnexpectedCalls = [];
        public int ChoiceCalls;
        public int Choose(Window owner, string title, string message, params string[] buttons)
        {
            ChoiceCalls++;
            if (Choices.Count == 0)
            {
                UnexpectedCalls.Add("Unexpected decision dialog: " + message);
                throw new InvalidOperationException(UnexpectedCalls[^1]);
            }
            return Choices.Dequeue();
        }
        public string? ChooseSavePath(Window owner, SaveFileDialog dialog)
        {
            SaveFilters.Add(dialog.Filter);
            if (SavePaths.Count == 0)
            {
                UnexpectedCalls.Add("Unexpected Save As dialog.");
                throw new InvalidOperationException(UnexpectedCalls[^1]);
            }
            return SavePaths.Dequeue();
        }
        public void ShowMessage(Window owner, string message, string title, MessageBoxImage icon = MessageBoxImage.Information)
            => Messages.Add((message, icon));
    }

    private static object? Call(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, Private)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static T Call<T>(object target, string name, params object[] args) => (T)Call(target, name, args)!;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static DocumentData Document(MainWindow window) => Field<DocumentData>(window, "_document");
    private static string State(MainWindow window) => DocumentStore.Serialize(Document(window));
    private static bool Dirty(MainWindow window) => (bool)typeof(MainWindow).GetProperty("IsDirty", Private)!.GetValue(window)!;
    private static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; actual {actual}.");
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
