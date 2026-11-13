using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Dedsec.Monitor;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out int pid);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    internal static void DarkTitleBar(nint handle)
    { var enabled = 1; DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)); }

    internal static void PlaceOverlay(System.Windows.Window window, System.Drawing.Rectangle area, string corner)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var x = corner.EndsWith("Right", StringComparison.Ordinal) ? area.Right - width - 20 : area.Left + 20;
        var y = corner.StartsWith("Bottom", StringComparison.Ordinal) ? area.Bottom - height - 20 : area.Top + 20;
        SetWindowPos(new WindowInteropHelper(window).Handle, -1, x, y, 0, 0, 0x0001 | 0x0010);
    }

    internal static int ForegroundProcess()
    { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid; }

    internal static void MakeClickThrough(System.Windows.Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x20 | 0x80 | 0x08000000);
    }
}
