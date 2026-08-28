using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesktopOverlayBoard.Services;

internal static class WindowPlacementService
{
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    public static void ApplyPlacementMode(Window window, string mode)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? "topmost" : mode;
        window.Topmost = normalized.Equals("topmost", StringComparison.OrdinalIgnoreCase);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var target = normalized.Equals("topmost", StringComparison.OrdinalIgnoreCase) ? HwndTopmost : HwndNotTopmost;
        SetWindowPos(handle, target, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

        if (normalized.Equals("desktop", StringComparison.OrdinalIgnoreCase))
        {
            SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
