<#
.SYNOPSIS
Saves a lossless screenshot of a running Inklane window at its native pixel size.
.EXAMPLE
./scripts/capture-window.ps1 -ProcessId 1234 -OutputPath .scratch/notebook.png
.EXAMPLE
./scripts/capture-window.ps1 -WindowHandle 983440 -OutputPath .scratch/paper-dialog.png
.EXAMPLE
./scripts/capture-window.ps1 -ProcessId 1234 -IncludePopups -OutputPath .scratch/tools-menu.png
#>
[CmdletBinding(DefaultParameterSetName = 'Process')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Process')][int]$ProcessId,
    [Parameter(Mandatory, ParameterSetName = 'Window')][long]$WindowHandle,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$IncludePopups,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($destination) -ine '.png') { throw 'Choose a .png destination.' }
if ([IO.File]::Exists($destination) -and !$Force) { throw 'The destination exists. Use -Force to replace it.' }
Add-Type -AssemblyName System.Drawing.Common
if (!('InklaneCapture.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace InklaneCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    public static class Native {
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect rect, int size);
        [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] private static extern int GetCloaked(IntPtr hwnd, int attribute, out int value, int size);
        public static bool FrameIsUnobscured(IntPtr target, int targetProcess, Rect frame) {
            int foregroundProcess;
            GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcess);
            if (foregroundProcess != targetProcess) return false;
            IntPtr window = GetTopWindow(IntPtr.Zero);
            for (int count = 0; window != IntPtr.Zero && count < 10000; count++, window = GetWindow(window, 2)) {
                if (window == target) return true;
                if (!IsWindowVisible(window) || IsIconic(window)) continue;
                int owner, cloaked;
                GetWindowThreadProcessId(window, out owner);
                if (owner == targetProcess || (GetCloaked(window, 14, out cloaked, 4) == 0 && cloaked != 0)) continue;
                Rect bounds;
                if (!GetWindowRect(window, out bounds)) return false;
                if (bounds.Right > frame.Left && bounds.Left < frame.Right && bounds.Bottom > frame.Top && bounds.Top < frame.Bottom) return false;
            }
            return false;
        }
    }
}
'@
}

if ($PSCmdlet.ParameterSetName -eq 'Process') {
    $process = Get-Process -Id $ProcessId
    $handle = $process.MainWindowHandle
} else {
    $handle = [IntPtr]::new($WindowHandle)
    $ownerId = 0
    [void][InklaneCapture.Native]::GetWindowThreadProcessId($handle, [ref]$ownerId)
    if (!$ownerId) { throw 'The window handle no longer exists.' }
    $process = Get-Process -Id $ownerId
}
if ($process.ProcessName -ne 'Inklane') { throw 'Select a window owned by Inklane.exe.' }
if ($handle -eq [IntPtr]::Zero -or ![InklaneCapture.Native]::IsWindowVisible($handle) -or [InklaneCapture.Native]::IsIconic($handle)) {
    throw 'Show or restore the Inklane window before capturing it.'
}

# GetWindowRect is DPI-virtualized. Use physical pixels for the window and the DWM frame.
$oldContext = [InklaneCapture.Native]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
if ($oldContext -eq [IntPtr]::Zero) { throw 'The capture thread could not enter per-monitor DPI awareness.' }
$canvas = $null
$frameImage = $null
$temporary = $null
try {
    $rect = [InklaneCapture.Rect]::new()
    if (![InklaneCapture.Native]::GetWindowRect($handle, [ref]$rect)) { throw 'Could not read the native window bounds.' }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0 -or [long]$width * $height -gt 100000000) { throw 'Invalid window dimensions.' }
    $canvas = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [Drawing.Graphics]::FromImage($canvas)
    try {
        if ($IncludePopups) {
            # Popup menus can have a separate HWND. Capture the visible frame only while Inklane owns it.
            if (![InklaneCapture.Native]::FrameIsUnobscured($handle, $process.Id, $rect)) { throw 'Popup capture requires an unobscured foreground Inklane window.' }
            $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $canvas.Size, [Drawing.CopyPixelOperation]::SourceCopy)
            if (![InklaneCapture.Native]::FrameIsUnobscured($handle, $process.Id, $rect)) { throw 'The window was obscured during popup capture.' }
        } else {
            $dc = $graphics.GetHdc()
            try {
                # PW_RENDERFULLCONTENT requests the complete native window, including the title bar.
                if (![InklaneCapture.Native]::PrintWindow($handle, $dc, 2)) { throw 'The native PrintWindow capture failed.' }
            } finally { $graphics.ReleaseHdc($dc) }
        }
    } finally { $graphics.Dispose() }

    $frame = [InklaneCapture.Rect]::new()
    $bounds = [Drawing.Rectangle]::new(0, 0, $width, $height)
    if ([InklaneCapture.Native]::DwmGetWindowAttribute($handle, 9, [ref]$frame, 16) -eq 0) {
        $visible = [Drawing.Rectangle]::new($frame.Left - $rect.Left, $frame.Top - $rect.Top, $frame.Right - $frame.Left, $frame.Bottom - $frame.Top)
        $bounds = [Drawing.Rectangle]::Intersect($bounds, $visible)
    }
    if ($bounds.Width -le 0 -or $bounds.Height -le 0) { throw 'The visible frame has no pixels.' }
    $frameImage = $canvas.Clone($bounds, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $dpi = [InklaneCapture.Native]::GetDpiForWindow($handle)
    if ($dpi -gt 0) { $frameImage.SetResolution($dpi, $dpi) }
    $directory = [IO.Path]::GetDirectoryName($destination)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = [IO.Path]::Combine($directory, [Guid]::NewGuid().ToString('N') + '.tmp')
    $frameImage.Save($temporary, [Drawing.Imaging.ImageFormat]::Png)
    [IO.File]::Move($temporary, $destination, $Force.IsPresent)
    $method = if ($IncludePopups) { 'Visible frame' } else { 'PrintWindow' }
    [pscustomobject]@{ Path = $destination; ProcessId = $process.Id; WindowHandle = $handle.ToInt64(); Width = $frameImage.Width; Height = $frameImage.Height; Dpi = $dpi; Method = $method }
} finally {
    if ($frameImage) { $frameImage.Dispose() }
    if ($canvas) { $canvas.Dispose() }
    if ($temporary -and [IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    [void][InklaneCapture.Native]::SetThreadDpiAwarenessContext($oldContext)
}
