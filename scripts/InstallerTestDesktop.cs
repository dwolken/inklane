using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// GUI programs run on a private, non-input desktop. Tests exercise the real Windows
// shell and HWNDs without taking focus from a person's current desktop session.
public static class InstallerTestDesktop
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string Reserved, Desktop, Title;
        public int X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public short Show, ReservedBytes;
        public IntPtr ReservedData, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr attributes);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string app, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, int flags, IntPtr environment, string workingDirectory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out int exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindow(IntPtr window, IntPtr context);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr context);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);

    // Process.MainWindowHandle can point to an IME or startup helper before WPF has shown its workspace.
    public static IntPtr FindWorkspace(int expectedProcess)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, _) => {
            GetWindowThreadProcessId(window, out int owner);
            if (owner != expectedProcess || !IsWindowVisible(window) || !GetWindowRect(window, out Rect rect)) return true;
            if (rect.Right - rect.Left < 450 || rect.Bottom - rect.Top < 300) return true;
            var name = new StringBuilder(256);
            var title = new StringBuilder(512);
            GetClassName(window, name, name.Capacity);
            GetWindowText(window, title, title.Capacity);
            if (!name.ToString().StartsWith("HwndWrapper[", StringComparison.Ordinal) ||
                title.ToString().IndexOf("Inklane", StringComparison.OrdinalIgnoreCase) < 0) return true;
            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    public static void CloseWorkspace(IntPtr window, int expectedProcess)
    {
        GetWindowThreadProcessId(window, out int owner);
        if (owner != expectedProcess) throw new InvalidOperationException("The workspace changed ownership.");
        if (!PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
    }

    public static int Run(string program, string commandLine, string workingDirectory)
    {
        string name = "InklaneInstallerTest-" + Guid.NewGuid().ToString("N");
        IntPtr desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01FF, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = name };
            const int noWindow = 0x08000000;
            if (!CreateProcess(program, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero,
                    false, noWindow, IntPtr.Zero, workingDirectory, ref startup, out ProcessInfo child))
                throw new Win32Exception();
            try
            {
                // The worker bounds each individual installer and application launch.
                if (WaitForSingleObject(child.Process, 600000) != 0) throw new TimeoutException("Installer checks timed out.");
                if (!GetExitCodeProcess(child.Process, out int exitCode)) throw new Win32Exception();
                return exitCode;
            }
            finally { CloseHandle(child.Thread); CloseHandle(child.Process); }
        }
        finally { CloseDesktop(desktop); }
    }
}
