using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Inklane.Core;
using Inklane.Controls;

namespace Inklane.SurfaceTests;

// The two WPF processes share an isolated Windows clipboard. Product tests never replace the user's clipboard.
internal static class ClipboardChecks
{
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowStation(string? name, uint flags, uint access, IntPtr attributes);
    [DllImport("user32", SetLastError = true)] private static extern bool SetProcessWindowStation(IntPtr station);
    [DllImport("user32")] private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr station, int index, StringBuilder buffer, int bytes, out int written);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public short Show, ReservedBytes;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string program, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string cwd, ref StartupInfo startup, out ProcessInfo child);
    [DllImport("kernel32")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32")] private static extern bool GetExitCodeProcess(IntPtr handle, out int code);
    [DllImport("kernel32")] private static extern bool TerminateProcess(IntPtr process, int status);

    private static void LaunchOnDesktop(string role, string desktop, string dir)
    {
        var exe = Environment.ProcessPath!;
        var command = new StringBuilder().Append('"').Append(exe).Append('"');
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            command.Append(" \"").Append(Assembly.GetExecutingAssembly().Location).Append('"');
        command.Append(" --clipboard-peer ").Append(role).Append(" \"").Append(desktop).Append("\" \"").Append(dir).Append('"');
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = desktop };
        if (!CreateProcess(exe, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero,
            Environment.CurrentDirectory, ref startup, out var process)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (WaitForSingleObject(process.Process, 45000) != 0)
            { TerminateProcess(process.Process,1); throw new TimeoutException($"Clipboard {role} timed out."); }
            if (!GetExitCodeProcess(process.Process, out var code) || code != 0)
            {
                var log = Path.Combine(dir, role+".log");
                throw new InvalidOperationException(File.Exists(log) ? File.ReadAllText(log) : $"Clipboard {role} exited {code}.");
            }
        }
        finally { CloseHandle(process.Thread); CloseHandle(process.Process); }
    }

    public static void Run(string output)
    {
        var dir = Path.Combine(output, "clipboard");
        Directory.CreateDirectory(dir);
        Launch("review", "InklaneTests-" + Guid.NewGuid().ToString("N"), dir);
    }

    private static void Launch(string role, string station, string dir)
    {
        var exe = Environment.ProcessPath!;
        var argument = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            argument.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in new[] { "--clipboard-peer", role, station, dir }) argument.ArgumentList.Add(arg);
        using var process = Process.Start(argument)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(entireProcessTree:true); throw new TimeoutException($"Clipboard {role} did not finish."); }
        Console.Write(stdout.GetAwaiter().GetResult());
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr.GetAwaiter().GetResult());
        Console.WriteLine("Cross-process clipboard: mixed selection, shortcuts, native text, bitmap/PNG, PDF and image sidecars.");
    }

    public static int Peer(string[] args)
    {
        try
        {
            var role = args[1]; var dir = args[3];
            if (role == "review")
            {
                // Non-elevated tokens can create the session's noninteractive window station.
                var station = CreateWindowStation(null, 0, 0x02000000, IntPtr.Zero);
                if (station == IntPtr.Zero || !SetProcessWindowStation(station)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var name = "Inklane-" + Guid.NewGuid().ToString("N");
                var desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01ff, IntPtr.Zero);
                if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                LaunchOnDesktop("paste", StationName() + "\\" + name, dir);
            }
            else
            {
                // Verify the actual OS station in EACH worker before making any clipboard calls.
                if (StationName() != args[2].Split('\\')[0]) throw new InvalidOperationException("Worker started on an unexpected station.");
                Environment.SetEnvironmentVariable("INKLANE_DATA_HOME", Path.Combine(dir, role));
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode=ThemeMode.System };
                TestResources.Load(app); L.Initialize("en");
                Settings.Current.Appearance="Light"; Appearance.Apply();
                if (role == "paste")
                {
                    using var gate = new Mutex(false, "Local\\Inklane-Isolated-Clipboard-Checks");
                    if (!gate.WaitOne(TimeSpan.FromSeconds(40))) throw new TimeoutException("Another clipboard check is running.");
                    try
                    {
                        if (Clipboard.GetDataObject()?.GetFormats().Length > 0)
                            throw new InvalidOperationException("Noninteractive clipboard has preexisting content.");
                        try { LaunchOnDesktop("copy",args[2],dir); Review(dir); }
                        finally { Clipboard.Clear(); }
                    }
                    finally { gate.ReleaseMutex(); }
                }
                else Copy();
                app.Shutdown();
            }
            return 0;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(args[3],args[1]+".log"),e.ToString()); Console.Error.WriteLine(e); return 1; }
    }

    private static string StationName()
    {
        var name = new StringBuilder(256);
        if (!GetUserObjectInformation(GetProcessWindowStation(), 2, name, 512, out _) ||
            name.ToString().Equals("WinSta0", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to test on the interactive clipboard.");
        return name.ToString();
    }

    private static void Copy()
    {
        var page = new PageData
        {
            Ink = [new InkData { Id = "original-ink", Color = "#406CD4", Width = 4, Points = [new() { X=40,Y=75,Pressure=.33f }, new() { X=96,Y=130,Pressure=.8f }] }],
            Items = [
                new() { Id="original-type", X=55,Y=150,FontSize=27,Width=265,Text="Section A–A" },
                new() { Id="original-note", IsNote=true, IsExpanded=false, X=86,Y=95,Title="Review detail",Text="Check the edge.\nÄnderungen." }]
        };
        var surface = new DocumentSurface { Translate = L.T };
        surface.LoadPage(page);
        Call(surface, "SelectAllObjects");
        Call(surface, "CopyObjects");
    }

    private static void Review(string dir)
    {
        Assert(Clipboard.ContainsText() && Clipboard.GetText().Contains("Review detail"), "Missing interoperable note title and text after process exit.");
        Assert(Clipboard.ContainsData(System.Windows.Ink.StrokeCollection.InkSerializedFormat), "Missing interoperable ink.");
        var probe = new Probe();
        var window = new MainWindow(probe);
        window.Show();
        window.Activate();
        Pump();
        Shortcut(window, Key.V);
        Assert(!(bool)Field(window,"_onHome"), "Paste on Home failed.");
        var surface = (DocumentSurface)Field(window,"_surface");
        var page = Document(window).Pages[0];
        Assert(page.Ink.Count == 1 && page.Ink[0].Id != "original-ink" && page.Ink[0].Width == 4 && Math.Abs(page.Ink[0].Points[0].Pressure-.33f)<.01,
            "Peer ink did not retain geometry, width and pressure with a new identity.");
        Assert(page.Items.Count == 2 && page.Items[0].Id != "original-type" && page.Items[0].FontSize == 27 &&
            page.Items[1].Title == "Review detail" && page.Items[1].Text.Contains("Änderungen") && !page.Items[1].IsExpanded,
            "Peer text, title, note data or presentation lost.");
        Assert(page.Items[1].X-page.Items[0].X == 31 && page.Items[1].Y-page.Items[0].Y == -55, "Group placement distorted.");
        var first = page.Items.Select(i => i.Id).ToHashSet();
        var before = (page.Items[0].X,page.Items[1].X,page.Ink[0].Points[0].X);
        var handle = Find<Thumb>(surface, "TextMove:"+page.Items[0].Id);
        handle.RaiseEvent(new DragStartedEventArgs(0,0) { RoutedEvent=Thumb.DragStartedEvent });
        handle.RaiseEvent(new DragDeltaEventArgs(24,12) { RoutedEvent=Thumb.DragDeltaEvent });
        handle.RaiseEvent(new DragCompletedEventArgs(24,12,false) { RoutedEvent=Thumb.DragCompletedEvent });
        Assert(Math.Abs(page.Items[0].X-before.Item1-24) < .01 && Math.Abs(page.Items[1].X-before.Item2-24) < .01 &&
            Math.Abs(page.Ink[0].Points[0].X-before.Item3-24)<.1,"Dragging one member did not move the entire mixed selection.");
        CaptureVisual((FrameworkElement)window.Content, Path.Combine(dir,"mixed-selection.png"));
        var menu = OpenObjectMenu(window, surface);
        Thread.Sleep(250); Pump(); // Allow the native popup entrance animation to settle.
        CaptureVisual((FrameworkElement)PresentationSource.FromVisual(menu).RootVisual,Path.Combine(dir,"selection-menu.png"));
        Assert(menu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,L.T("DuplicateObjects")) && i.IsEnabled),"Duplicate missing from mixed selection menu.");
        menu.IsOpen=false; surface.Focus(); Pump();
        Shortcut(window,Key.Escape,ModifierKeys.None);
        Assert(!surface.HasSelection, "Escape did not clear mixed selection.");
        Shortcut(window,Key.A);
        Assert(surface.HasSelection, "Select All failed after Escape.");
        Shortcut(window, Key.D);
        Assert(page.Items.Count == 4 && page.Ink.Count == 2 && page.Items.Skip(2).All(i=>!first.Contains(i.Id)), "Duplicate did not create independent objects.");
        Shortcut(window, Key.X);
        Assert(page.Items.Count == 2 && page.Ink.Count == 1, "Grouped cut failed.");
        Shortcut(window,Key.Z);
        Assert(Document(window).Pages[0].Items.Count == 4, "Undo cut failed.");
        Shortcut(window,Key.Y);
        Assert(Document(window).Pages[0].Items.Count == 2, "Redo cut failed.");
        Command(window, Key.V);
        page = Document(window).Pages[0];
        Assert(page.Items.Count == 4 && page.Ink.Count == 2, "Grouped paste failed.");

        var note = page.Items.Last(i=>i.IsNote);
        surface.RenameNote(note.Id);
        Pump();
        var title = Find<TextBox>(surface, "NoteTitle:"+note.Id);
        Assert(title.IsKeyboardFocused, "Rename did not focus the editable title.");
        Clipboard.SetText("Inspection");
        Assert(!(bool)Call(window,"HandleObjectKey",Key.V,ModifierKeys.Control)!, "Object command intercepted native title paste.");
        Shortcut(window,Key.A); Shortcut(window,Key.V);
        Assert(note.Title == "Inspection", "Native text paste did not rename the note.");
        title.Select(0,3); Shortcut(window,Key.C);
        Assert(Clipboard.GetText() == "Ins", "Native text selection copy was replaced by object copy.");
        Shortcut(window,Key.Escape,ModifierKeys.None);
        Assert(!surface.HasTextFocus, "Escape did not leave the text editor.");
        Shortcut(window, Key.D);
        Assert(page.Items.Last().Title=="Inspection" && page.Items.Last().Text==note.Text && page.Items.Last().Id!=note.Id,
            "Renamed note duplication failed.");
        Call(window,"ToggleNotes");
        var search = (TextBox)Field(window,"_noteSearch");
        search.Text="Inspection"; Pump();
        var rows = (StackPanel)Field(window,"_noteRows");
        Assert(rows.Children.OfType<Button>().Count()==2, "Title search did not find the renamed note and its duplicate.");

        // CF_BITMAP-only source simulates a screenshot app without the optional PNG clipboard format.
        var pixels = Enumerable.Repeat((byte)0x55, 40*30*4).ToArray();
        var sample = BitmapSource.Create(40,30,96,96,PixelFormats.Bgr32,null,pixels,40*4);
        // CF_DIB is the device-independent bitmap representation used by Windows screen capture tools.
        var bmp = new BmpBitmapEncoder(); bmp.Frames.Add(BitmapFrame.Create(sample));
        using var bmpFile = new MemoryStream(); bmp.Save(bmpFile);
        var dib = new DataObject();
        dib.SetData(DataFormats.Dib,new MemoryStream(bmpFile.ToArray()[14..]),false);
        Clipboard.SetDataObject(dib,true);
        Shortcut(window,Key.V);
        Assert(page.Images.Count==1, "A real CF_DIB-only clipboard did not paste as a screenshot.");
        var raster = EmbeddedPng.Decode(page.Images[0].PngData);
        var rgb = new FormatConvertedBitmap(raster,PixelFormats.Bgra32,null,0);
        var rgba=new byte[4]; rgb.CopyPixels(new Int32Rect(0,0,1,1),rgba,4,0);
        Assert(rgba.SequenceEqual(new byte[]{0x55,0x55,0x55,255}),"DIB screenshot colors or opacity changed.");
        Shortcut(window,Key.Delete,ModifierKeys.None);
        Assert(page.Images.Count==0,"Delete did not remove the pasted screenshot.");
        Clipboard.SetImage(sample);
        Command(window,Key.V);
        Assert(page.Images.Count==1 && Math.Abs(page.Images[0].Width-40)<.01, "Bitmap clipboard paste failed.");
        var picture = page.Images[0];
        Call(surface,"NudgeSelection",Key.Right,true);
        Assert(picture.X>24,"Image keyboard reposition failed.");
        Command(window,Key.C);
        Assert(Clipboard.ContainsImage() && Clipboard.ContainsData("PNG"),"Selected image lost interoperable representation.");
        Command(window,Key.V);
        Assert(page.Images.Count==2 && page.Images[1].Id!=picture.Id && page.Images[1].PngData.SequenceEqual(picture.PngData), "Editable picture copy failed.");

        var pdf = Path.Combine(dir,"Pasted.pdf"); DocumentStore.SavePdf(Document(window), pdf);
        var reopened = DocumentStore.LoadPdf(pdf).Pages[0];
        Assert(reopened.Images.Count==2 && reopened.Items.Count(i=>i.Title=="Inspection")==2 &&
            reopened.Ink.Count==2, "Saved editable PDF lost mixed paste data.");
        var imageDirectory=Directory.CreateDirectory(Path.Combine(dir,"images")).FullName;
        var png = Path.Combine(imageDirectory,"source.png");
        var originalPixels = new byte[40*30*4];
        var originalBitmap = BitmapSource.Create(40,30,96,96,PixelFormats.Bgr32,null,originalPixels,40*4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(originalBitmap));
        using (var stream=File.Create(png)) encoder.Save(stream);
        var original = File.ReadAllBytes(png);
        var sibling = Path.Combine(imageDirectory,"second.png");
        File.WriteAllBytes(sibling,original);
        window.OpenFile(png); Pump();
        Command(window,Key.V); // transfer the editable picture from notebook to image
        Command(window,Key.PrintScreen);
        Assert(Clipboard.ContainsData("PNG") && Clipboard.GetImage()?.PixelWidth==40, "Annotated image export to clipboard failed.");
        static byte[] ColorsOnClipboard()
        {
            var bitmap = new FormatConvertedBitmap(Clipboard.GetImage(), PixelFormats.Bgr24, null, 0);
            var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
            bitmap.CopyPixels(bytes,bitmap.PixelWidth * 3,0); return bytes;
        }
        Assert(ColorsOnClipboard().Any(v => v != 0),"Flattened clipboard image omitted the placed picture.");
        Call(window,"CopyOriginalPicture");
        Assert(ColorsOnClipboard().All(v => v == 0),"Copy original image included document markup.");
        Command(window,Key.PrintScreen);
        Assert((bool)Call(window,"Save",false)!, "Image annotation save failed.");
        Assert(original.SequenceEqual(File.ReadAllBytes(png)) && DocumentStore.LoadJson(png+".inklane.json").Pages[0].Images.Count==1,
            "Image original or sidecar corrupted.");
        Command(window,Key.V); // paste exported standard PNG into image workspace
        Assert(Document(window).Pages[0].Images.Count==2, "Standard PNG clipboard paste failed.");
        var imagePage = Document(window).Pages[0];
        var pictureX = imagePage.Images.Last().X;
        Shortcut(window,Key.Left,ModifierKeys.None);
        Assert(Document(window).SourceImagePath==png && imagePage.Images.Last().X<=pictureX, "Object arrows navigated away from an image.");
        Shortcut(window,Key.Left); // Ctrl+arrow is reserved and does not change the active file.
        Assert(Document(window).SourceImagePath==png, "Ctrl+arrow changed the active file.");
        Shortcut(window,Key.Escape,ModifierKeys.None);
        Shortcut(window,Key.Left,ModifierKeys.None);
        Assert(Document(window).SourceImagePath==sibling, "Left arrow did not browse to the previous image.");
        Shortcut(window,Key.Right,ModifierKeys.None);
        Assert(Document(window).SourceImagePath==png, "Right arrow did not reopen the next image.");
        var countBeforeFailure = Document(window).Pages[0].Images.Count;
        var invalid = new DataObject();
        invalid.SetData("Inklane.EditableObjects.v1", new MemoryStream([123,33]),false);
        Clipboard.SetDataObject(invalid,true);
        Command(window,Key.V);
        Assert(probe.Errors==1 && Document(window).Pages[0].Images.Count==countBeforeFailure, "Invalid clipboard modified the target or failed silently.");
        window.Close();
        Console.WriteLine("Two independent WPF processes verified persisted rich clipboard, native text, bitmap/PNG, mixed selection, undo/redo, PDF and sidecar round trips.");
    }


    private static ContextMenu OpenObjectMenu(MainWindow window,DocumentSurface surface)
    {
        surface.Focus();
        Shortcut(window,Key.F10,ModifierKeys.Shift);
        for (var tick=0;tick<20;tick++)
        {
            Pump();
            foreach (var source in PresentationSource.CurrentSources.OfType<HwndSource>())
                if (source.RootVisual != null && DescendantMenu(source.RootVisual) is { IsOpen:true } menu) return menu;
            Thread.Sleep(15);
        }
        throw new InvalidOperationException("Keyboard context menu did not open on the selected objects.");

        static ContextMenu? DescendantMenu(DependencyObject node)
        {
            if (node is ContextMenu menu) return menu;
            for (var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                if (DescendantMenu(VisualTreeHelper.GetChild(node,i)) is { } child) return child;
            return null;
        }
    }

    private static void CaptureVisual(FrameworkElement element,string path)
    {
        Pump(); element.UpdateLayout();
        var dpi=VisualTreeHelper.GetDpi(element);
        var rect=new Rect(element.RenderSize);

        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(rect.Width*dpi.DpiScaleX),(int)Math.Ceiling(rect.Height*dpi.DpiScaleY),
            dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(path); encoder.Save(output);
    }

    [DllImport("user32",SetLastError=true)] private static extern bool GetKeyboardState([Out] byte[] keys);
    [DllImport("user32",SetLastError=true)] private static extern bool SetKeyboardState(byte[] keys);

    private static void Shortcut(MainWindow window, Key key, ModifierKeys modifiers = ModifierKeys.Control)
    {
        // Set modifier state only on this detached test thread's input queue, then dispatch
        // the WPF preview and bubbling events to the focused control in the actual editor.
        var saved = new byte[256];
        if (!GetKeyboardState(saved)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var keys = new byte[256];
        if (modifiers.HasFlag(ModifierKeys.Control)) keys[0x11] = keys[0xA2] = 0x80;
        if (modifiers.HasFlag(ModifierKeys.Shift)) keys[0x10] = keys[0xA0] = 0x80;
        if (!SetKeyboardState(keys)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Assert(Keyboard.Modifiers == modifiers, "Thread-local keyboard modifiers did not take effect.");
            var target = Keyboard.FocusedElement as UIElement ?? window;
            var e = new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(e);
            if (!e.Handled) { e.RoutedEvent = Keyboard.KeyDownEvent; target.RaiseEvent(e); }
        }
        finally { SetKeyboardState(saved); }
        Pump();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Command(MainWindow window, Key key) => Call(window,"RunObjectCommand",key,null);
    private static T Find<T>(DependencyObject parent, string id) where T:FrameworkElement
    {
        IEnumerable<T> Walk(DependencyObject node)
        {
            if (node is T element && AutomationProperties.GetAutomationId(element)==id) yield return element;
            for (var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                foreach (var match in Walk(VisualTreeHelper.GetChild(node,i))) yield return match;
        }
        return Walk(parent).Single();
    }
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name,Private)!.Invoke(target,args);
    private static object Field(object target,string name) => target.GetType().GetField(name,Private)!.GetValue(target)!;
    private static DocumentData Document(MainWindow window) => (DocumentData)Field(window,"_document");
    private static void Assert(bool value,string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Probe : IDocumentInteraction
    {
        public int Errors;
        public int Choose(Window owner,string title,string message,params string[] buttons) => 1;
        public string? ChooseSavePath(Window owner,Microsoft.Win32.SaveFileDialog dialog) => throw new InvalidOperationException("Unexpected save dialog.");
        public void ShowMessage(Window owner,string message,string title,MessageBoxImage icon=MessageBoxImage.Information)
        { Errors++; Console.WriteLine("Editor message: "+message); }
    }
}
